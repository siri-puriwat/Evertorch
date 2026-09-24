namespace Evertorch.Server
{
/// <summary>
///     The abuse controls as the status publisher last saw them (Network Protocol §11).
/// </summary>
public readonly struct AbuseStatus
{
    public AbuseStatus(long overBudgetMessages, long throttledCommands, long violations, long violationDisconnects)
    {
        OverBudgetMessages = overBudgetMessages;
        ThrottledCommands = throttledCommands;
        Violations = violations;
        ViolationDisconnects = violationDisconnects;
    }

    /// <summary>
    ///     Messages dropped, or that closed their connection, because their peer was over its budget (layer 1).
    /// </summary>
    public long OverBudgetMessages { get; }

    /// <summary>
    ///     Commands refused or dropped by the per-connection buckets (layer 2).
    /// </summary>
    public long ThrottledCommands { get; }

    public long Violations { get; }

    /// <summary>
    ///     Connections closed for violations or for rate excess.
    /// </summary>
    public long ViolationDisconnects { get; }
}
}
