using System.Data.Common;

using Microsoft.EntityFrameworkCore;

namespace AiDevTools.TokenSaver.Database.Context;

/// <summary>
///     Rebuild strategy for a stored index whose Chunks table predates the current columns.
///     CREATE TABLE IF NOT EXISTS never adds columns to an existing table, so such an index fails
///     every semantic query with "no such column". A forced rebuild backs the database up, then
///     recreates the Chunks table with the current schema so the re-index can run. Nothing here
///     runs on connect; it is reached only from a forced (user-requested) rebuild.
/// </summary>
public static class ChunkSchemaRecovery
{
    /// <summary>True when the stored Chunks table has the current FileLastModified column.</summary>
    public static async Task<bool> HasCurrentChunkSchemaAsync(WorkspaceIndexContext ctx, CancellationToken ct)
    {
        DbConnection conn = ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open)
        {
            await conn.OpenAsync(ct);
        }

        using DbCommand cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('Chunks') WHERE name = 'FileLastModified';";
        return Convert.ToInt64(await cmd.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>
    ///     Backs the whole database up to a timestamped file next to it (VACUUM INTO), then drops and
    ///     recreates the Chunks table with the current columns and indexes. Returns the backup path.
    /// </summary>
    public static async Task<string> BackupAndRecreateChunksAsync(WorkspaceIndexContext ctx, CancellationToken ct)
    {
        string dbPath = ctx.Database.GetDbConnection().DataSource;
        string backup = Path.Combine(
            Path.GetDirectoryName(dbPath)!,
            $"{Path.GetFileName(dbPath)}.before-chunks-recreate-{DateTime.UtcNow:yyyyMMddHHmmss}.bak");

        _ = await ctx.Database.ExecuteSqlRawAsync("VACUUM INTO {0};", new object[] { backup }, ct);

        _ = await ctx.Database.ExecuteSqlRawAsync("DROP TABLE IF EXISTS \"Chunks\";", ct);
        _ = await ctx.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE "Chunks" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_Chunks" PRIMARY KEY AUTOINCREMENT,
                "RelativePath" TEXT NOT NULL,
                "ChunkType" TEXT NOT NULL,
                "Name" TEXT NOT NULL,
                "StartLine" INTEGER NOT NULL,
                "EndLine" INTEGER NOT NULL,
                "ContentHash" TEXT NOT NULL,
                "Summary" TEXT NOT NULL,
                "EmbeddingVector" TEXT NOT NULL,
                "UpdatedAt" INTEGER NOT NULL,
                "FileLastModified" INTEGER NOT NULL DEFAULT 0,
                "FileSize" INTEGER NOT NULL DEFAULT 0,
                "FileHash" TEXT NOT NULL DEFAULT ''
            );
            """, ct);
        _ = await ctx.Database.ExecuteSqlRawAsync(
            """CREATE INDEX IF NOT EXISTS "IX_Chunks_RelativePath" ON "Chunks" ("RelativePath");""", ct);
        _ = await ctx.Database.ExecuteSqlRawAsync(
            """CREATE UNIQUE INDEX IF NOT EXISTS "IX_Chunks_RelativePath_StartLine_EndLine" ON "Chunks" ("RelativePath", "StartLine", "EndLine");""",
            ct);

        return backup;
    }
}
