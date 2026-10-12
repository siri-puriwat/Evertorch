namespace Evertorch.Persistence
{
/// <summary>
///     Keeps <c>card_changes</c> append-only with the ledger's own trigger function (Persistence §4).
/// </summary>
internal static class CardChangesImmutability
{
    public const string CreateSql = @"
CREATE TRIGGER card_changes_no_update_or_delete
    BEFORE UPDATE OR DELETE ON card_changes
    FOR EACH ROW EXECUTE FUNCTION economy_ledger_reject_change();

CREATE TRIGGER card_changes_no_truncate
    BEFORE TRUNCATE ON card_changes
    FOR EACH STATEMENT EXECUTE FUNCTION economy_ledger_reject_change();
";
}
}
