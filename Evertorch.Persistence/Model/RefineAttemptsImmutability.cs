namespace Evertorch.Persistence
{
/// <summary>
///     The triggers that make <c>refine_attempts</c> append-only like <c>economy_ledger</c> (Persistence §4), through
///     the ledger's own trigger function, which names whichever table refused.
/// </summary>
internal static class RefineAttemptsImmutability
{
    public const string CreateSql = @"
CREATE TRIGGER refine_attempts_no_update_or_delete
    BEFORE UPDATE OR DELETE ON refine_attempts
    FOR EACH ROW EXECUTE FUNCTION economy_ledger_reject_change();

CREATE TRIGGER refine_attempts_no_truncate
    BEFORE TRUNCATE ON refine_attempts
    FOR EACH STATEMENT EXECUTE FUNCTION economy_ledger_reject_change();
";
}
}
