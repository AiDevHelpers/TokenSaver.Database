namespace AiDevTools.TokenSaver.Database.Context.Entities;

/// <summary>
///     One row per workspace folder. Replaces the old context.db `folder_indices` table, which
///     stored one JSON blob per folder — normalized here so folder/file/symbol counts can be
///     queried with real joins (see <see cref="FolderOverview" />) instead of parsing JSON client-side.
/// </summary>
public class FolderEntity
{
    public int Id { get; set; }

    /// <summary>Workspace-relative path; "." for the workspace root.</summary>
    public string RelativePath { get; set; } = "";

    /// <summary>Null for the workspace root folder.</summary>
    public int? ParentFolderId { get; set; }

    /// <summary>
    ///     One-paragraph description of this folder's contents, detailed enough for the draft
    ///     model to decide whether it needs to descend into it for a given request.
    /// </summary>
    public string Description { get; set; } = "";

    /// <summary>
    ///     Unix ms. For folders under a git repo, staleness is primarily driven by
    ///     <see cref="RepoStalenessEntity" /> instead; this is the fallback / display value.
    /// </summary>
    public long LastModified { get; set; }

    public long UpdatedAt { get; set; }

    public FolderEntity? ParentFolder { get; set; }
    public ICollection<FolderEntity> ChildFolders { get; set; } = [];
    public ICollection<FileEntity> Files { get; set; } = [];
}
