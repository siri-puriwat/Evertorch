namespace Evertorch.Persistence
{
/// <summary>
///     The trigger that makes <c>economy_ledger</c> append-only (Persistence §4). It rejects row updates and deletes and
///     a whole-table truncate, whoever issues them, so no code path can rewrite an audited transfer.
/// </summary>
internal static class LedgerImmutability
{
    public const string CreateSql = @"
CREATE FUNCTION economy_ledger_reject_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'economy_ledger is append-only: % is not allowed', TG_OP
        USING ERRCODE = 'restrict_violation';
END;
$$;

CREATE TRIGGER economy_ledger_no_update_or_delete
    BEFORE UPDATE OR DELETE ON economy_ledger
    FOR EACH ROW EXECUTE FUNCTION economy_ledger_reject_change();

CREATE TRIGGER economy_ledger_no_truncate
    BEFORE TRUNCATE ON economy_ledger
    FOR EACH STATEMENT EXECUTE FUNCTION economy_ledger_reject_change();
";

    public const string DropSql = @"
DROP TRIGGER economy_ledger_no_truncate ON economy_ledger;
DROP TRIGGER economy_ledger_no_update_or_delete ON economy_ledger;
DROP FUNCTION economy_ledger_reject_change();
";
}
}
