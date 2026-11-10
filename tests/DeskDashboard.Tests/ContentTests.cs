using System.Net;
using System.Text;
using System.Text.Json;
using DeskDashboard.Core;
using DeskDashboard.Core.Content;
using DeskDashboard.Core.Data;
using DeskDashboard.Core.Location;
using DeskDashboard.Core.Prayer;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeskDashboard.Tests;

public sealed class ContentTests : IDisposable
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly DbContextOptions<DashboardDbContext> options;
    private readonly FakeClock clock = new(DateTimeOffset.Parse("2026-11-10T08:00:00Z"));

    public ContentTests()
    {
        connection.Open();
        options = new DbContextOptionsBuilder<DashboardDbContext>().UseSqlite(connection).Options;
        using var db = Db();
        DashboardDatabase.EnsureCreatedAndSeeded(db, null);
        new ContentService(db).SeedIfNeededAsync().GetAwaiter().GetResult();
    }

    public void Dispose() => connection.Dispose();

    private DashboardDbContext Db() => new(options);

    [Fact]
    public void Seed_is_imported_once()
    {
        using var db = Db();
        var count = db.ContentItems.Count();
        Assert.True(count > 300, $"seeded {count}");
        Assert.Contains(db.ContentItems, x => x.Type == ContentTypes.Quran);
        Assert.Contains(db.ContentItems, x => x.SourceKey.StartsWith("muslim:"));

        DashboardDatabase.EnsureCreatedAndSeeded(db, null);
        Assert.Equal(0, new ContentService(db).SeedIfNeededAsync().GetAwaiter().GetResult());
        Assert.Equal(count, db.ContentItems.Count());
    }

    [Fact]
    public void Content_tables_are_added_to_a_database_from_before_the_feature()
    {
        using (var db = Db())
        {
            db.Database.ExecuteSqlRaw("DROP TABLE ContentItems; DROP TABLE ContentStates;");
        }

        using (var db = Db())
        {
            DashboardDatabase.EnsureCreatedAndSeeded(db, null);
            Assert.Equal(0, db.ContentItems.Count());
            new ContentService(db).SeedIfNeededAsync().GetAwaiter().GetResult();
            Assert.True(db.ContentItems.Count() > 300);
            Assert.Equal(1, db.PrayerSettings.Count());
        }
    }

    [Fact]
    public async Task Library_lists_most_recently_shown_first_then_never_shown()
    {
        await using var db = Db();
        var service = new ContentService(db, clock);
        var quran = await service.ListAsync(ContentTypes.Quran, hidden: false);
        var first = quran[5].Id;
        var second = quran[9].Id;

        await service.MarkShownAsync(first);
        clock.Advance(TimeSpan.FromMinutes(2));
        await service.MarkShownAsync(second);

        var listed = await service.ListAsync(ContentTypes.Quran, hidden: false);
        Assert.Equal(second, listed[0].Id);
        Assert.Equal(first, listed[1].Id);
        Assert.Null(listed[2].LastShownAt);
        Assert.Equal(1, listed[0].ShowCount);
    }

    [Fact]
    public async Task Removed_items_leave_rotation_and_can_be_restored()
    {
        await using var db = Db();
        var service = new ContentService(db, clock);
        var target = (await service.ListAsync(ContentTypes.Hadith, hidden: false))[0];

        await service.SetHiddenAsync(target.Id, true);
        Assert.DoesNotContain(await service.ActiveAsync(), x => x.Id == target.Id);
        Assert.Contains(await service.ListAsync(ContentTypes.Hadith, hidden: true), x => x.Id == target.Id);
        Assert.Equal(1, (await service.StatusAsync(false)).HiddenCount);

        await service.SetHiddenAsync(target.Id, false);
        Assert.Contains(await service.ActiveAsync(), x => x.Id == target.Id);
    }

    [Fact]
    public async Task Refresh_adds_new_quran_and_filtered_hadith_without_duplicates()
    {
        var handler = new FakeApi();
        var refresh = Refresh(handler);

        var outcome = await refresh.RefreshAsync();

        Assert.True(outcome.Success, outcome.Message);
        Assert.Equal(3, outcome.AddedQuran);
        Assert.Equal(2, outcome.AddedHadith);

        await using var db = Db();
        var added = db.ContentItems.Where(x => x.Batch == "2026-W46").ToList();
        var hadith = added.Where(x => x.Type == ContentTypes.Hadith).ToList();
        Assert.Contains(hadith, x => x.Ref == "Sahih Muslim 9001a" && x.Grade == "Sahih");
        Assert.Contains(hadith, x => x.SourceKey == "bukhari:9100");
        Assert.DoesNotContain(hadith, x => x.English.Contains("rest of the hadith"));
        Assert.All(hadith, x => Assert.DoesNotContain("<br>", x.Arabic));
        Assert.Contains("Narrated Anas: The Prophet", hadith.Single(x => x.SourceKey == "bukhari:9100").English);

        // A second run adds nothing already stored.
        clock.Advance(TimeSpan.FromDays(8));
        var again = await refresh.RefreshAsync();
        Assert.Equal(0, again.AddedHadith);
        Assert.Equal(3, again.AddedQuran); // the next three pool references
    }

    [Fact]
    public async Task Refresh_runs_only_when_due_and_failures_keep_existing_content()
    {
        var handler = new FakeApi();
        var refresh = Refresh(handler);
        Assert.True((await refresh.RefreshIfDueAsync()).Ran);

        clock.Advance(TimeSpan.FromDays(3));
        Assert.False((await refresh.RefreshIfDueAsync()).Ran);

        int before;
        await using (var db = Db())
        {
            before = db.ContentItems.Count();
        }

        handler.Fail = true;
        clock.Advance(TimeSpan.FromDays(5));
        var failed = await refresh.RefreshIfDueAsync();
        Assert.True(failed.Ran);
        Assert.False(failed.Success);

        await using (var db = Db())
        {
            Assert.Equal(before, db.ContentItems.Count());
            var state = await new ContentService(db, clock).GetStateAsync();
            Assert.NotEqual(state.LastAttemptAtMs, state.LastRefreshAtMs);
        }

        // Still due, so the next check retries.
        handler.Fail = false;
        Assert.True((await refresh.RefreshIfDueAsync()).Ran);
    }

    [Fact]
    public async Task Refresh_waits_when_downloads_are_not_allowed()
    {
        var refresh = Refresh(new FakeApi());
        refresh.CanDownload = () => false;
        var outcome = await refresh.RefreshAsync();
        Assert.False(outcome.Ran);
    }

    [Fact]
    public async Task Removed_items_are_never_added_back_by_a_refresh()
    {
        var handler = new FakeApi();
        var refresh = Refresh(handler);
        await refresh.RefreshAsync();

        await using (var db = Db())
        {
            var item = db.ContentItems.Single(x => x.SourceKey == "bukhari:9100");
            await new ContentService(db, clock).SetHiddenAsync(item.Id, true);
        }

        clock.Advance(TimeSpan.FromDays(8));
        await refresh.RefreshAsync();

        await using (var db = Db())
        {
            var item = db.ContentItems.Single(x => x.SourceKey == "bukhari:9100");
            Assert.True(item.Hidden);
        }
    }

    [Fact]
    public async Task Local_api_serves_content_routes()
    {
        var api = new LocalApi(
            Db,
            new PrayerTimeCalculator(),
            new GeocodingClient(new HttpClient { BaseAddress = new Uri("https://invalid.test/") }),
            NullLoggerFactory.Instance,
            Refresh(new FakeApi()));

        var list = await api.HandleAsync("GET", "/api/content?type=quran", null);
        Assert.Equal(200, list.Status);
        var id = JsonDocument.Parse(list.Body).RootElement[0].GetProperty("id").GetInt32();

        Assert.Equal(204, (await api.HandleAsync("POST", $"/api/content/{id}/shown", null)).Status);
        var hidden = await api.HandleAsync("PUT", $"/api/content/{id}/hidden", """{"hidden":true}""");
        Assert.Equal(200, hidden.Status);
        Assert.True(JsonDocument.Parse(hidden.Body).RootElement.GetProperty("hidden").GetBoolean());

        var active = await api.HandleAsync("GET", "/api/content/active", null);
        Assert.DoesNotContain($"\"id\":{id},", active.Body);

        var status = await api.HandleAsync("GET", "/api/content/status", null);
        Assert.Equal(200, status.Status);
        Assert.Equal(1, JsonDocument.Parse(status.Body).RootElement.GetProperty("hiddenCount").GetInt32());

        Assert.Equal(400, (await api.HandleAsync("GET", "/api/content?type=poems", null)).Status);
        Assert.Equal(404, (await api.HandleAsync("POST", "/api/content/999999/shown", null)).Status);
        Assert.Equal(404, (await api.HandleAsync("DELETE", $"/api/content/{id}/hidden", null)).Status);
    }

    [Theory]
    [InlineData("2609.01", 6643, "2609a")]
    [InlineData("2560", 6532, "2560")]
    [InlineData("2702.02", 6859, "2702b")]
    [InlineData(null, 17, "17")]
    public void Reference_numbers_follow_the_standard_numbering(string? arabic, int number, string expected) =>
        Assert.Equal(expected, ContentText.ReferenceNumber(arabic, number));

    [Fact]
    public void Basmala_is_removed_only_where_the_api_prefixes_it()
    {
        const string withBasmala = "بِسْمِ ٱللَّهِ ٱلرَّحْمَٰنِ ٱلرَّحِيمِ ٱقْرَأْ بِٱسْمِ رَبِّكَ";
        Assert.Equal("ٱقْرَأْ بِٱسْمِ رَبِّكَ", ContentText.StripBasmala(withBasmala, 96, 1));
        Assert.Equal(withBasmala, ContentText.StripBasmala(withBasmala, 1, 1));
        Assert.Equal(withBasmala, ContentText.StripBasmala(withBasmala, 96, 2));
    }

    [Fact]
    public void Pool_is_valid_and_has_no_overlapping_ayat()
    {
        var pool = QuranPool.All;
        Assert.True(pool.Count(r => r.IsSeed) >= 70);
        Assert.True(pool.Count(r => !r.IsSeed) >= 250);
        var seen = new HashSet<string>();
        foreach (var reference in pool)
        {
            for (var ayah = reference.First; ayah <= reference.Last; ayah += 1)
            {
                Assert.True(seen.Add($"{reference.Surah}:{ayah}"), $"{reference.Label} overlaps");
            }
        }
    }

    private ContentRefreshService Refresh(FakeApi handler) =>
        new(Db, new HttpClient(handler), NullLogger<ContentRefreshService>.Instance, clock)
        {
            QuranPerRefresh = 3,
            HadithPerRefresh = 10,
            QuranRequestGap = TimeSpan.Zero
        };

    private sealed class FakeClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset now = start;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan by) => now += by;
    }

    /// <summary>Answers AlQuran.cloud ayah requests and hadith-api section requests.</summary>
    private sealed class FakeApi : HttpMessageHandler
    {
        public bool Fail { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Fail)
            {
                throw new HttpRequestException("offline");
            }

            var url = request.RequestUri!.ToString();
            if (url.Contains("/ara-bukhari/") && url.EndsWith(".min.json"))
            {
                // Like jsDelivr sometimes does: the client must fall back to .json.
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            }

            string json;
            if (url.StartsWith(ContentRefreshService.QuranApi))
            {
                var reference = url[ContentRefreshService.QuranApi.Length..].Split('/')[0].Split(':');
                var surah = int.Parse(reference[0]);
                var ayah = int.Parse(reference[1]);
                var arabic = ayah == 1 && surah != 1 ? "بِسْمِ ٱللَّهِ ٱلرَّحْمَٰنِ ٱلرَّحِيمِ نص" : "نص الآية";
                json = JsonSerializer.Serialize(new
                {
                    code = 200,
                    data = new object[]
                    {
                        new { text = arabic, numberInSurah = ayah, surah = new { number = surah, englishName = "Surah" + surah }, edition = new { identifier = "quran-uthmani" } },
                        new { text = $"English {surah}:{ayah}", numberInSurah = ayah, surah = new { number = surah, englishName = "Surah" + surah }, edition = new { identifier = "en.sahih" } }
                    }
                });
            }
            else if (url.Contains("/eng-muslim/"))
            {
                json = Section(
                    (9001, "9001.01", "Abu Huraira reported Allah's Messenger (ﷺ) as saying: The strong man is the one who controls himself when angry."),
                    (9002, "9002", "Abu Huraira reported: the rest of the hadith is the same as narrated above by this chain."));
            }
            else if (url.Contains("/ara-muslim/"))
            {
                json = Section((9001, "9001.01", "نص عربي<br>للحديث"), (9002, "9002", "نص"));
            }
            else if (url.Contains("/eng-bukhari/"))
            {
                json = Section(
                    (9100, "9100", "Narrated Anas:The Prophet (ﷺ) said, \"None of you will have faith till he wishes for his brother what he likes for himself.\""),
                    (9101, "9101", "Narrated X: " + new string('a', 500)));
            }
            else if (url.Contains("/ara-bukhari/"))
            {
                json = Section((9100, "9100", "نص عربي"), (9101, "9101", "نص"));
            }
            else
            {
                json = Section(); // nawawi, qudsi: empty in this fake
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }

        private static string Section(params (int Number, string Arabic, string Text)[] hadiths) =>
            JsonSerializer.Serialize(new
            {
                metadata = new { name = "x" },
                hadiths = hadiths.Select(h => new { hadithnumber = h.Number, arabicnumber = h.Arabic, text = h.Text, grades = Array.Empty<object>() })
            });
    }
}
