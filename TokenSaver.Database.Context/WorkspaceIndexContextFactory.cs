using Microsoft.EntityFrameworkCore;

namespace TokenSaver.Database.Context;

/// <summary>
///     Opens the unified per-workspace index database, creating the schema, enabling WAL mode,
///     and (re)creating the folder_overview view on first open. Callers should create one context
///     per workspace per process and reuse it (mirrors the old single-connection-per-process
///     pattern in LocalContextService / CodeIndexService).
/// </summary>
public static class WorkspaceIndexContextFactory
{
    public const string TokenSaverFolderName = ".tokensaver";
    public const string IndexFileName = "index.db";

    public static string GetDbPath(string workspaceRoot)
    {
        return Path.Combine(workspaceRoot, TokenSaverFolderName, IndexFileName);
    }

    /// <summary>
    ///     Opens (creating if needed) the unified workspace index for the given workspace root,
    ///     ensures the schema exists, enables WAL, and (re)creates the folder_overview view.
    /// </summary>
    public static async Task<WorkspaceIndexContext> CreateAsync(string workspaceRoot, CancellationToken ct = default)
    {
        string dbPath = GetDbPath(workspaceRoot);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);

        DbContextOptions<WorkspaceIndexContext> options = new DbContextOptionsBuilder<WorkspaceIndexContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        WorkspaceIndexContext ctx = new(options);
        try
        {
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

    private static async Task InitializeSchemaAsync(WorkspaceIndexContext ctx, CancellationToken ct)
    {
        _ = await ctx.Database.EnsureCreatedAsync(ct);

        // WAL: lets MCP tool reads proceed while a background indexing write is in flight.
        _ = await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);

        // EnsureCreatedAsync only emits DDL when the database file doesn't exist yet — a workspace
        // whose index.db predates the Chunks/TokenSavings tables (added for the token-economy tool
        // surface) will NOT get them from EnsureCreatedAsync alone. Create both defensively with
        // IF NOT EXISTS, matching the DDL EF's Sqlite provider would generate from the ChunkEntity/
        // TokenSavingsEntity configuration in WorkspaceIndexContext.OnModelCreating — harmless no-op
        // on a freshly-created database where EnsureCreatedAsync already made them.
        _ = await ctx.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "Chunks" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Chunks" PRIMARY KEY AUTOINCREMENT,
                "RelativePath" TEXT NOT NULL,
                "ChunkType" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "StartLine" INTEGER NOT NULL,
                "EndLine" INTEGER NOT NULL,
                "ContentHash" TEXT NOT NULL,
                "Summary" TEXT NOT NULL,
                "EmbeddingVector" TEXT NOT NULL,
                "UpdatedAt" INTEGER NOT NULL
            );
            """, ct);
        _ = await ctx.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Chunks_RelativePath" ON "Chunks" ("RelativePath");""", ct);
        _ = await ctx.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Chunks_RelativePath_StartLine_EndLine" ON "Chunks" ("RelativePath", "StartLine", "EndLine");""",
            ct);

        _ = await ctx.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "TokenSavings" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_TokenSavings" PRIMARY KEY AUTOINCREMENT,
                "Tool" TEXT NOT NULL,
                "Role" TEXT NOT NULL,
                "Terse" INTEGER NOT NULL,
                "RawTokens" INTEGER NOT NULL,
                "DistilledTokens" INTEGER NOT NULL,
                "SavedTokens" INTEGER NOT NULL,
                "DurationMs" INTEGER NOT NULL,
                "CreatedAt" INTEGER NOT NULL,
                "ConversationTurns" INTEGER NOT NULL DEFAULT 0,
                "ConversationTokens" INTEGER NOT NULL DEFAULT 0
            );
            """, ct);
        _ = await ctx.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_TokenSavings_Tool" ON "TokenSavings" ("Tool");""", ct);
        _ = await ctx.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_TokenSavings_Role" ON "TokenSavings" ("Role");""", ct);
        _ = await ctx.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_TokenSavings_CreatedAt" ON "TokenSavings" ("CreatedAt");""", ct);

        // TokenSavings predates ConversationTurns/ConversationTokens — CREATE TABLE IF NOT EXISTS
        // above is a no-op against an existing table missing these columns, so add them here too.
        // SQLite has no "ADD COLUMN IF NOT EXISTS"; swallow the duplicate-column error instead.
        await AddColumnIfMissingAsync(ctx, "TokenSavings", "ConversationTurns", "INTEGER NOT NULL DEFAULT 0", ct);
        await AddColumnIfMissingAsync(ctx, "TokenSavings", "ConversationTokens", "INTEGER NOT NULL DEFAULT 0", ct);

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
    }

    private static async Task AddColumnIfMissingAsync(
        WorkspaceIndexContext ctx, string table, string column, string columnDef, CancellationToken ct)
    {
        try
        {
#pragma warning disable EF1002 // DDL identifiers are fixed schema names passed as literals from InitializeSchemaAsync; no user input reaches this SQL.
            _ = await ctx.Database.ExecuteSqlRawAsync(
                $"""ALTER TABLE "{table}" ADD COLUMN "{column}" {columnDef};""", ct);
#pragma warning restore EF1002
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex) when (
            ex.Message.Contains("duplicate column name", StringComparison.OrdinalIgnoreCase))
        {
            // Column already exists from a previous CreateAsync call — nothing to do.
        }
    }
}
