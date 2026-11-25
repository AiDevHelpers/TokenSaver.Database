namespace AiDevTools.TokenSaver.Database.Context.Entities;

/// <summary>
///     One row per indexed file. Absorbs both the old context.db `files`/`sections` tables
///     (previously split: file metadata vs. minified content) and the old code-index.db
///     `file_manifest` table — merged into a single table per the unified-index design.
/// </summary>
public class FileEntity
{
    public int Id { get; set; }

    public int FolderId { get; set; }

    /// <summary>Workspace-relative path, unique.</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>Extension-derived type/language, e.g. "csharp", "markdown", "json", "binary".</summary>
    public string FileType { get; set; } = "";

    /// <summary>Static-extraction and/or AI-generated one-paragraph summary of contents.</summary>
    public string Description { get; set; } = "";

    public string ContentHash { get; set; } = "";

    public long Size { get; set; }

    public long LastModified { get; set; }

    public long UpdatedAt { get; set; }

    public FolderEntity? Folder { get; set; }
    public ICollection<SymbolEntity> Symbols { get; set; } = [];
}
