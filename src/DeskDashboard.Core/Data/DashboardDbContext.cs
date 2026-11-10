using DeskDashboard.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeskDashboard.Core.Data;

public sealed class DashboardDbContext(DbContextOptions<DashboardDbContext> options) : DbContext(options)
{
    public DbSet<PrayerSettings> PrayerSettings => Set<PrayerSettings>();
    public DbSet<MosqueTimetable> MosqueTimetables => Set<MosqueTimetable>();
    public DbSet<ContentItem> ContentItems => Set<ContentItem>();
    public DbSet<ContentState> ContentStates => Set<ContentState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PrayerSettings>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Country).HasMaxLength(100);
            entity.Property(x => x.City).HasMaxLength(100);
            entity.Property(x => x.TimeZoneId).HasMaxLength(100);
            entity.Property(x => x.MethodKey).HasMaxLength(60);
            entity.Property(x => x.AsrMadhab).HasMaxLength(20);
        });

        modelBuilder.Entity<MosqueTimetable>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.EffectiveFrom).IsUnique();
            entity.Property(x => x.Note).HasMaxLength(200);
        });

        // Keep in sync with DashboardDatabase.ContentSchemaSql (used for databases created
        // before these tables existed, where EnsureCreated does nothing).
        modelBuilder.Entity<ContentItem>(entity =>
        {
            entity.ToTable("ContentItems");
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.SourceKey).IsUnique();
            entity.HasIndex(x => new { x.Type, x.Hidden });
        });

        modelBuilder.Entity<ContentState>(entity =>
        {
            entity.ToTable("ContentStates");
            entity.HasKey(x => x.Id);
        });
    }
}
