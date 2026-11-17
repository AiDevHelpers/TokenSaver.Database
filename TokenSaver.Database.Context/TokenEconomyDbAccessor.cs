using System.Collections.Concurrent;

namespace TokenSaver.Database.Context;

/// <summary>
///     DB-access seam for the token-economy services (SavingsTracker, RepoIndexer,
///     SemanticSearchService) — owns one cached <see cref="WorkspaceIndexContext" /> per workspace
///     root (a process can legitimately be asked about more than one root — e.g. an internal
///     warm-up call against the default root racing an explicit caller-supplied
///     <c>workspaceRoot</c> for a different one) plus the lock that serializes access to it (EF
///     Core's DbContext isn't thread-safe), mirroring
///     <c>TokenSaver.CodeHelper.Indexing.WorkspaceIndexDbAccessor</c>'s pattern exactly.
///     Previously this cached a single context/root pair and disposed+replaced it whenever a
///     different root came in — safe only as long as nothing else held a reference to the old
///     context. Once <see cref="WithDbLockAsync{T}(Func{Task{T}},CancellationToken)" /> callers
///     started running for two different roots close together (observed directly: a background
///     semantic-index run for one root racing a foreground call for another), the swap disposed a
///     context a concurrent caller was still using, throwing ObjectDisposedException. Each root now
///     keeps its own entry for the accessor's lifetime instead.
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
    private readonly ConcurrentDictionary<string, WorkspaceIndexContext> _contexts = new(StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        foreach (WorkspaceIndexContext ctx in _contexts.Values)
        {
            ctx.Dispose();
        }

        _ctxOpenLock.Dispose();
        _dbLock.Dispose();
    }

    public async Task<WorkspaceIndexContext> GetDbAsync(string workspaceRoot, CancellationToken ct)
    {
        // One context per workspace, however the root was spelled (trailing separator, relative path).
        workspaceRoot = WorkspaceIndexContextFactory.NormalizeRoot(workspaceRoot);
        if (_contexts.TryGetValue(workspaceRoot, out WorkspaceIndexContext? existing))
        {
            return existing;
        }

        await _ctxOpenLock.WaitAsync(ct);
        try
        {
            if (_contexts.TryGetValue(workspaceRoot, out WorkspaceIndexContext? existingAfterLock))
            {
                return existingAfterLock;
            }

            WorkspaceIndexContext ctx = await WorkspaceIndexContextFactory.CreateAsync(workspaceRoot, ct);
            _contexts[workspaceRoot] = ctx;
            return ctx;
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
