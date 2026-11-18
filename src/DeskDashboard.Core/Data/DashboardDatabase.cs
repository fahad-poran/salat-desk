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
        CREATE TABLE IF NOT EXISTS "AlertPreferences" (
            "Key" TEXT NOT NULL CONSTRAINT "PK_AlertPreferences" PRIMARY KEY,
            "Notify" INTEGER NOT NULL,
            "Minutes" INTEGER NOT NULL
        );
        """;

    // Columns added after 0.1.0, as (table, column, SQL type). Must match DashboardDbContext's model.
    private static readonly (string Table, string Column, string Type)[] AddedColumns =
    [
        ("MosqueTimetables", "FajrRule", "TEXT NULL"),
        ("MosqueTimetables", "DhuhrRule", "TEXT NULL"),
        ("MosqueTimetables", "AsrRule", "TEXT NULL"),
        ("MosqueTimetables", "MaghribRule", "TEXT NULL"),
        ("MosqueTimetables", "IshaRule", "TEXT NULL"),
        ("ContentItems", "RefBook", "INTEGER NULL"),
        ("ContentItems", "RefNumber", "INTEGER NULL")
    ];

    /// <summary>Adds columns introduced by later versions to an existing database (SQLite ALTER TABLE).</summary>
    public static void MigrateSchema(DashboardDbContext db)
    {
        var connection = db.Database.GetDbConnection();
        var opened = connection.State != System.Data.ConnectionState.Open;
        if (opened)
        {
            connection.Open();
        }

        try
        {
            foreach (var group in AddedColumns.GroupBy(c => c.Table))
            {
                var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"PRAGMA table_info(\"{group.Key}\")";
                    using var reader = command.ExecuteReader();
                    while (reader.Read())
                    {
                        existing.Add(reader.GetString(1));
                    }
                }

                foreach (var (table, column, type) in group.Where(c => !existing.Contains(c.Column)))
                {
                    using var alter = connection.CreateCommand();
                    alter.CommandText = $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {type}";
                    alter.ExecuteNonQuery();
                }
            }
        }
        finally
        {
            if (opened)
            {
                connection.Close();
            }
        }
    }

    /// <summary>
    /// Creates the SQLite schema if needed, adds tables introduced by later versions, and seeds
    /// the single settings row on first run. Returns true when the settings seed was written.
    /// </summary>
    public static bool EnsureCreatedAndSeeded(DashboardDbContext db, PrayerSettings? seed)
    {
        db.Database.EnsureCreated();
        db.Database.ExecuteSqlRaw(ContentSchemaSql);
        MigrateSchema(db);

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
