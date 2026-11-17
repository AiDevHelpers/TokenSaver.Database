using System.Data.Common;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace TokenSaver.Database.Context;

/// <summary>
///     Opens the unified per-workspace index database. The schema comes from the EF model (EnsureCreated) and is
///     versioned with PRAGMA user_version. The index is a cache of the workspace, so when an existing database
///     carries a different schema version (or none, as databases from before versioning did) it is deleted and
///     rebuilt instead of being patched with hand-written DDL. Only the folder_overview view, which EF cannot create
///     from a ToView mapping, is created here. Callers should create one context per workspace per process and
///     reuse it.
/// </summary>
public static class WorkspaceIndexContextFactory
{
    public const string TokenSaverFolderName = ".tokensaver";
    public const string IndexFileName = "index.db";

    /// <summary>
    ///     Version of the index schema this build writes. Bump it whenever the EF model changes incompatibly; every
    ///     existing index is then rebuilt on its next open (a full reindex, no data migration). Version 0 is what
    ///     databases from before versioning carry.
    /// </summary>
    public const int SchemaVersion = 1;

    public static string GetDbPath(string workspaceRoot)
    {
        return Path.Combine(workspaceRoot, TokenSaverFolderName, IndexFileName);
    }

    /// <summary>
    ///     The cache key for a workspace root: full path, no trailing separator, so "F:\x" and "F:\x\" name the same
    ///     index and share one open context.
    /// </summary>
    public static string NormalizeRoot(string workspaceRoot)
    {
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspaceRoot));
    }

    /// <summary>
    ///     Opens (creating if needed) the unified workspace index for the given workspace root. A database whose
    ///     schema version differs from <see cref="SchemaVersion" /> is removed and recreated first.
    /// </summary>
    public static async Task<WorkspaceIndexContext> CreateAsync(string workspaceRoot, CancellationToken ct = default)
    {
        string dbPath = GetDbPath(workspaceRoot);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        WorkspaceIndexContext ctx = Open(dbPath);
        try
        {
            if (await HasStaleSchemaAsync(ctx, ct))
            {
                await ctx.DisposeAsync();
                DeleteFiles(dbPath);
                ctx = Open(dbPath);
            }

            await InitializeSchemaAsync(ctx, ct);
            return ctx;
        }
        catch
        {
            // Dispose on failure: a leaked connection keeps the file open, so a corrupt index.db
            // cannot be deleted and rebuilt by the caller (Windows sharing violation).
            await ctx.DisposeAsync();
            throw;
        }
    }

    private static WorkspaceIndexContext Open(string dbPath)
    {
        DbContextOptions<WorkspaceIndexContext> options = new DbContextOptionsBuilder<WorkspaceIndexContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new WorkspaceIndexContext(options);
    }

    /// <summary>True when the file already holds an index written under a different schema version.</summary>
    private static async Task<bool> HasStaleSchemaAsync(WorkspaceIndexContext ctx, CancellationToken ct)
    {
        long tables = await ScalarAsync(ctx, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Folders';", ct);
        if (tables == 0)
        {
            return false; // brand new file: nothing to rebuild
        }

        long version = await ScalarAsync(ctx, "PRAGMA user_version;", ct);
        return version != SchemaVersion;
    }

    private static async Task InitializeSchemaAsync(WorkspaceIndexContext ctx, CancellationToken ct)
    {
        _ = await ctx.Database.EnsureCreatedAsync(ct);

        // WAL: lets MCP tool reads proceed while a background indexing write is in flight.
        _ = await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);

        // EnsureCreated does not emit DDL for entities mapped ToView — create it ourselves.
        // Table names below match EF's default naming (DbSet property names): Folders, Files, Symbols.
        _ = await ctx.Database.ExecuteSqlRawAsync(
            """
            CREATE VIEW IF NOT EXISTS folder_overview AS
            SELECT
              f."Id"            AS "Id",
              f."RelativePath"  AS "RelativePath",
              f."Description"   AS "Description",
              f."LastModified"  AS "LastModified",
              COUNT(DISTINCT fi."Id")  AS "FileCount",
              COUNT(DISTINCT sy."Id")  AS "SymbolCount",
              MAX(fi."LastModified")   AS "MaxChildLastModified"
            FROM "Folders" f
            LEFT JOIN "Files" fi   ON fi."FolderId" = f."Id"
            LEFT JOIN "Symbols" sy ON sy."FileId"   = fi."Id"
            GROUP BY f."Id";
            """, ct);

        // Stamp the version last, so an interrupted open is retried (and rebuilt) rather than trusted.
        _ = await ctx.Database.ExecuteSqlRawAsync($"PRAGMA user_version = {SchemaVersion};", ct);
    }

    private static async Task<long> ScalarAsync(WorkspaceIndexContext ctx, string sql, CancellationToken ct)
    {
        DbConnection conn = ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        using DbCommand cmd = conn.CreateCommand();
#pragma warning disable CA2100 // Only literal SQL from this class reaches here (fixed PRAGMA and sqlite_master queries); no caller input.
        cmd.CommandText = sql;
#pragma warning restore CA2100
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static void DeleteFiles(string dbPath)
    {
        // Pooled connections keep the file open on Windows; release them before deleting.
        SqliteConnection.ClearAllPools();
        foreach (string file in new[] { dbPath, dbPath + "-wal", dbPath + "-shm" })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }
}
