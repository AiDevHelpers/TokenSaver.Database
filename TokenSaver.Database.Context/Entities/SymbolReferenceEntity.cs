namespace AiDevTools.TokenSaver.Database.Context.Entities;

/// <summary>
///     A directed edge between two symbols (e.g. "method A calls method B"). Deliberately has no
///     EF-modeled FK relationship to <see cref="SymbolEntity" /> — during incremental reindexing a
///     reference can transiently point at a symbol id that hasn't been re-parsed yet, same as the
///     old code-index.db `references_` table (which also had no FK constraint).
/// </summary>
public class SymbolReferenceEntity
{
    public int Id { get; set; }

    public string SourceSymbolId { get; set; } = "";

    public string TargetSymbolId { get; set; } = "";

    public int Kind { get; set; }

    public int Line { get; set; }
}
