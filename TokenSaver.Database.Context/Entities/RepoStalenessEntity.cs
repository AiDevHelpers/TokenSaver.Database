namespace TokenSaver.Database.Context.Entities;

/// <summary>
///     One row per git repo root detected under the workspace (the workspace root itself, plus any
///     nested/vendored repos). Checked on first DB load per process: if HEAD sha and dirty-flag are
///     unchanged since <see cref="CheckedAt" />, that repo's files are skipped entirely rather than
///     re-walked. See plans/TokenSaver.UnifiedIndex.plan.md §2.
/// </summary>
public class RepoStalenessEntity
{
    /// <summary>Workspace-relative path to the repo root; "." if the workspace root itself is the repo.</summary>
    public string RepoRootPath { get; set; } = "";

    public string LastHeadSha { get; set; } = "";

    /// <summary>True if `git status --porcelain` was non-empty at the last check.</summary>
    public bool LastDirty { get; set; }

    public long CheckedAt { get; set; }
}
