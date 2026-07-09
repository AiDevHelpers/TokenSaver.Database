namespace TokenSaver.Database.Context.Entities;

/// <summary>
///     One row per distill() call, recorded by SavingsTracker (when
///     TokenEconomyConfig.EnableSavingsTracking is true) so TokenSavingsReport/WorthKeeping can
///     answer "what's actually saving the most tokens" from a query instead of a guess.
/// </summary>
public class TokenSavingsEntity
{
    public int Id { get; set; }

    /// <summary>Calling tool name, e.g. "runTests", "containerLogs", "diffSummarize".</summary>
    public string Tool { get; set; } = "";

    /// <summary>Distill role used, e.g. "test_failure", "log_errors".</summary>
    public string Role { get; set; } = "";

    /// <summary>Whether terse-mode was active for this specific call.</summary>
    public bool Terse { get; set; }

    public int RawTokens { get; set; }

    public int DistilledTokens { get; set; }

    /// <summary>RawTokens - DistilledTokens, stored directly so reports don't recompute it per query.</summary>
    public int SavedTokens { get; set; }

    /// <summary>Ollama call latency for this distillation, in milliseconds.</summary>
    public int DurationMs { get; set; }

    public long CreatedAt { get; set; }
}
