namespace TokenSaver.Database.Context.Entities;

/// <summary>
///     One row per function/class/markdown-section chunk within an indexed file — finer-grained
///     than <see cref="FileEntity" />'s single per-file <see cref="FileEntity.Description" />.
///     Populated by RepoIndexer (TokenSaver.Library.Services.IRepoIndexer) and queried by
///     SemanticGrep via cosine similarity over <see cref="EmbeddingVector" />. Unlike
///     <see cref="SymbolEntity" /> (exact/substring name search over statically-parsed symbols),
///     this table exists specifically for natural-language semantic search, so it carries an
///     AI-generated summary and an embedding vector rather than a parsed symbol kind.
/// </summary>
public class ChunkEntity
{
    public int Id { get; set; }

    /// <summary>
    ///     Workspace-relative file path. Deliberately NOT a foreign key into
    ///     <see cref="FileEntity" /> — RepoIndexer/SemanticGrep are independently runnable from
    ///     the CodeIndex/WorkspaceIndexService pipeline (no requirement that a file has been
    ///     symbol-indexed before it can be chunk-indexed), so this table has no dependency on
    ///     that one's lifecycle. Coincidentally overlapping rows in both tables for the same file
    ///     are expected and fine.
    /// </summary>
    public string RelativePath { get; set; } = "";

    /// <summary>"function" | "class" | "md_section" | "module" (whole-file fallback for unsupported extensions).</summary>
    public string ChunkType { get; set; } = "";

    /// <summary>Function/class name, or markdown heading text, or the file's base name for a "module" chunk.</summary>
    public string Name { get; set; } = "";

    public int StartLine { get; set; }

    public int EndLine { get; set; }

    /// <summary>Hash of the chunk's own text (not the whole file) — re-summarizing/re-embedding is skipped when unchanged.</summary>
    public string ContentHash { get; set; } = "";

    /// <summary>One-line AI-generated summary. This is what SemanticGrep shows Claude — not the raw chunk text.</summary>
    public string Summary { get; set; } = "";

    /// <summary>
    ///     JSON-encoded float[] embedding. Embeds <see cref="Summary" /> when
    ///     TokenEconomyConfig.EnableSummaryEmbedding is true (default), or the raw chunk text
    ///     otherwise — see RepoIndexer.
    /// </summary>
    public string EmbeddingVector { get; set; } = "";

    public long UpdatedAt { get; set; }
}
