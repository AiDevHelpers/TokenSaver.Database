namespace TokenSaver.Database.Context;

/// <summary>
///     DB-access seam for the token-economy services (SavingsTracker, RepoIndexer,
///     SemanticSearchService) — owns a single cached <see cref="WorkspaceIndexContext" /> per
///     workspace root plus the lock that serializes access to it (EF Core's DbContext isn't
///     thread-safe), mirroring <c>TokenSaver.CodeHelper.Indexing.WorkspaceIndexDbAccessor</c>'s
///     pattern exactly.
///     Deliberately a separate accessor/instance rather than reusing
///     <c>WorkspaceIndexDbAccessor</c> directly: that type lives in TokenSaver.CodeHelper, which
///     already depends on TokenSaver.Library — depending on it from here would be a backwards/
///     circular project reference. Both accessors open the same underlying SQLite file
///     (".tokensaver/index.db", the one DB file for this workspace) via
///     <see cref="WorkspaceIndexContextFactory" />; WAL mode (enabled by the factory) allows
///     multiple connections against it safely, and this accessor's tables (Chunks, TokenSavings)
///     don't overlap with WorkspaceIndexDbAccessor's writes (Folders/Files/Symbols) — only a
///     read-only FK reference from Chunks to Files.
/// </summary>
public sealed class TokenEconomyDbAccessor : IDisposable
{
    private readonly SemaphoreSlim _dbLock = new(1, 1);
    private readonly SemaphoreSlim _ctxOpenLock = new(1, 1);
    private WorkspaceIndexContext? _ctx;
    private string? _ctxWorkspaceRoot;

    public void Dispose()
    {
        _ctx?.Dispose();
        _ctxOpenLock.Dispose();
        _dbLock.Dispose();
    }

    public async Task<WorkspaceIndexContext> GetDbAsync(string workspaceRoot, CancellationToken ct)
    {
        if (_ctx != null && _ctxWorkspaceRoot == workspaceRoot)
        {
            return _ctx;
        }

        await _ctxOpenLock.WaitAsync(ct);
        try
        {
            if (_ctx != null && _ctxWorkspaceRoot == workspaceRoot)
            {
                return _ctx;
            }

            if (_ctx is not null)
            {
                await _ctx.DisposeAsync();
            }

            _ctx = await WorkspaceIndexContextFactory.CreateAsync(workspaceRoot, ct);
            _ctxWorkspaceRoot = workspaceRoot;
            return _ctx;
        }
        finally
        {
            _ = _ctxOpenLock.Release();
        }
    }

    /// <summary>Runs <paramref name="action" /> while holding the DB lock, returning its result.</summary>
    public async Task<T> WithDbLockAsync<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            return await action();
        }
        finally
        {
            _ = _dbLock.Release();
        }
    }

    /// <summary>Void-returning overload of <see cref="WithDbLockAsync{T}(Func{Task{T}},CancellationToken)" />.</summary>
    public async Task WithDbLockAsync(Func<Task> action, CancellationToken ct)
    {
        await _dbLock.WaitAsync(ct);
        try
        {
            await action();
        }
        finally
        {
            _ = _dbLock.Release();
        }
    }
}
