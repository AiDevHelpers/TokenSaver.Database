namespace AiDevTools.TokenSaver.Database.Context.Entities;

/// <summary>
///     Keyless entity mapped to the `folder_overview` SQL view (created manually by
///     <see cref="AiDevTools.TokenSaver.Database.Context.WorkspaceIndexContextFactory" />, since EF Core's
///     EnsureCreated does not generate DDL for entities configured with ToView). One row per
///     folder: file/symbol counts and freshest child mtime in a single query, so
///     "does the draft model need to descend into this folder" doesn't require walking children.
/// </summary>
public class FolderOverview
{
    public int Id { get; set; }
    public string RelativePath { get; set; } = "";
    public string Description { get; set; } = "";
    public long LastModified { get; set; }
    public int FileCount { get; set; }
    public int SymbolCount { get; set; }
    public long? MaxChildLastModified { get; set; }
}
