using AiDevTools.TokenSaver.Database.Context.Entities;

using Microsoft.EntityFrameworkCore;

namespace AiDevTools.TokenSaver.Database.Context;

/// <summary>
///     EF Core-backed replacement for the old context.db (files/sections/summaries/folder_indices,
///     the latter a JSON blob per folder) and code-index.db (file_manifest/symbols/references_)
///     SQLite caches. One instance per workspace, stored at ".tokensaver/index.db".
///     See plans/AiDevTools.TokenSaver.UnifiedIndex.plan.md for the design rationale.
/// </summary>
public class WorkspaceIndexContext(DbContextOptions<WorkspaceIndexContext> options) : DbContext(options)
{
    public DbSet<FolderEntity> Folders => Set<FolderEntity>();
    public DbSet<FileEntity> Files => Set<FileEntity>();
    public DbSet<SymbolEntity> Symbols => Set<SymbolEntity>();
    public DbSet<SymbolReferenceEntity> SymbolReferences => Set<SymbolReferenceEntity>();
    public DbSet<RepoStalenessEntity> RepoStaleness => Set<RepoStalenessEntity>();

    /// <summary>Function/class/markdown-section chunks — see ChunkEntity. Populated by RepoIndexer, queried by SemanticGrep.</summary>
    public DbSet<ChunkEntity> Chunks => Set<ChunkEntity>();

    /// <summary>Per-distill()-call savings log — see TokenSavingsEntity. Populated by SavingsTracker, queried by TokenSavingsReport/WorthKeeping.</summary>
    public DbSet<TokenSavingsEntity> TokenSavings => Set<TokenSavingsEntity>();

    /// <summary>
    ///     Keyless entity backed by the folder_overview view. Read-only — see
    ///     <see cref="WorkspaceIndexContextFactory.CreateAsync" /> for where the view is created.
    /// </summary>
    public DbSet<FolderOverview> FolderOverviews => Set<FolderOverview>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _ = modelBuilder.Entity<FolderEntity>(e =>
        {
            _ = e.HasIndex(f => f.RelativePath).IsUnique();
            _ = e.HasOne(f => f.ParentFolder)
                .WithMany(f => f.ChildFolders)
                .HasForeignKey(f => f.ParentFolderId)
                .OnDelete(DeleteBehavior.Restrict); // self-referencing — avoid cascade cycles
        });

        _ = modelBuilder.Entity<FileEntity>(e =>
        {
            _ = e.HasIndex(f => f.RelativePath).IsUnique();
            _ = e.HasOne(f => f.Folder)
                .WithMany(f => f.Files)
                .HasForeignKey(f => f.FolderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<SymbolEntity>(e =>
        {
            _ = e.HasIndex(s => s.Name);
            _ = e.HasIndex(s => s.FileId);
            _ = e.HasOne(s => s.File)
                .WithMany(f => f.Symbols)
                .HasForeignKey(s => s.FileId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        _ = modelBuilder.Entity<SymbolReferenceEntity>(e =>
        {
            _ = e.HasIndex(r => r.SourceSymbolId);
            _ = e.HasIndex(r => r.TargetSymbolId);
        });

        _ = modelBuilder.Entity<RepoStalenessEntity>(e => { _ = e.HasKey(r => r.RepoRootPath); });

        _ = modelBuilder.Entity<ChunkEntity>(e =>
        {
            _ = e.HasIndex(c => c.RelativePath);
            _ = e.HasIndex(c => new { c.RelativePath, c.StartLine, c.EndLine }).IsUnique();
        });

        _ = modelBuilder.Entity<TokenSavingsEntity>(e =>
        {
            _ = e.HasIndex(t => t.Tool);
            _ = e.HasIndex(t => t.Role);
            _ = e.HasIndex(t => t.CreatedAt);
        });

        _ = modelBuilder.Entity<FolderOverview>(e =>
        {
            _ = e.HasNoKey();
            _ = e.ToView("folder_overview");
        });
    }
}
