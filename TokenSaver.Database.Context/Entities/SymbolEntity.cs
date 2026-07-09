namespace TokenSaver.Database.Context.Entities;

/// <summary>
///     One row per function/type/section within a file. Replaces code-index.db's `symbols` table.
///     Id keeps the existing stable-hash scheme (SHA1 of FilePath+Name+Line) from
///     TokenSaver.CodeHelper's SymbolEntry so existing symbol ids remain valid across the swap.
/// </summary>
public class SymbolEntity
{
    public string Id { get; set; } = "";

    public int FileId { get; set; }

    public string Name { get; set; } = "";

    public string? FullName { get; set; }

    /// <summary>
    ///     Maps to TokenSaver.CodeHelper.Models.Index.SymbolKind. Stored as int here so
    ///     TokenSaver.Library doesn't need a project reference to TokenSaver.CodeHelper.
    /// </summary>
    public int Kind { get; set; }

    public int Line { get; set; }

    public int Col { get; set; }

    public string? ContainingType { get; set; }

    /// <summary>XML-doc summary or AI/static-inferred description.</summary>
    public string? Description { get; set; }

    /// <summary>Inherited from the parent File's LastModified at index time.</summary>
    public long LastModified { get; set; }

    public long UpdatedAt { get; set; }

    public FileEntity? File { get; set; }
}
