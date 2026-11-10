using DeskDashboard.Core.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace DeskDashboard.Core.Data;

public static class DashboardDatabase
{
    // EnsureCreated only creates a schema for a brand-new database. Installs from before the
    // content feature get these tables added here. Must match DashboardDbContext's model.
    internal const string ContentSchemaSql = """
        CREATE TABLE IF NOT EXISTS "ContentItems" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ContentItems" PRIMARY KEY AUTOINCREMENT,
            "Type" TEXT NOT NULL,
            "SourceKey" TEXT NOT NULL,
            "Ref" TEXT NOT NULL,
            "Source" TEXT NOT NULL,
            "Grade" TEXT NULL,
            "Arabic" TEXT NOT NULL,
            "English" TEXT NOT NULL,
            "Batch" TEXT NOT NULL,
            "AddedAtMs" INTEGER NOT NULL,
            "LastShownAtMs" INTEGER NULL,
            "ShowCount" INTEGER NOT NULL,
            "Hidden" INTEGER NOT NULL,
            "HiddenAtMs" INTEGER NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS "IX_ContentItems_SourceKey" ON "ContentItems" ("SourceKey");
        CREATE INDEX IF NOT EXISTS "IX_ContentItems_Type_Hidden" ON "ContentItems" ("Type", "Hidden");
        CREATE TABLE IF NOT EXISTS "ContentStates" (
            "Id" INTEGER NOT NULL CONSTRAINT "PK_ContentStates" PRIMARY KEY AUTOINCREMENT,
            "SeededAtMs" INTEGER NULL,
            "LastRefreshAtMs" INTEGER NULL,
            "LastAttemptAtMs" INTEGER NULL,
            "LastResult" TEXT NULL,
            "LastAddedQuran" INTEGER NOT NULL,
            "LastAddedHadith" INTEGER NOT NULL
        );
        """;

    /// <summary>
    /// Creates the SQLite schema if needed, adds tables introduced by later versions, and seeds
    /// the single settings row on first run. Returns true when the settings seed was written.
    /// </summary>
    public static bool EnsureCreatedAndSeeded(DashboardDbContext db, PrayerSettings? seed)
    {
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw(ContentSchemaSql);

        // The starter Ayat/Hadith are imported separately (ContentService.SeedIfNeededAsync), off the
        // startup path, so a first launch on a slow phone is not delayed.

        if (db.PrayerSettings.Any())
        {
            return false;
        }

        seed ??= new PrayerSettings();
        seed.Id = 1;
        seed.UpdatedAtUtc = DateTimeOffset.UtcNow;
        db.PrayerSettings.Add(seed);
        db.SaveChanges();
        return true;
    }
}
