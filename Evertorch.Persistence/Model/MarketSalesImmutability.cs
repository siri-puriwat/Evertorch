namespace Evertorch.Persistence
{
/// <summary>
///     The triggers that make <c>market_sales</c> append-only like <c>economy_ledger</c> (Persistence §4), through the
///     ledger's own trigger function, whose message now names whichever table refused.
/// </summary>
internal static class MarketSalesImmutability
{
    public const string CreateSql = @"
CREATE OR REPLACE FUNCTION economy_ledger_reject_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION '% is append-only: % is not allowed', TG_TABLE_NAME, TG_OP
        USING ERRCODE = 'restrict_violation';
END;
$$;

CREATE TRIGGER market_sales_no_update_or_delete
    BEFORE UPDATE OR DELETE ON market_sales
    FOR EACH ROW EXECUTE FUNCTION economy_ledger_reject_change();

CREATE TRIGGER market_sales_no_truncate
    BEFORE TRUNCATE ON market_sales
    FOR EACH STATEMENT EXECUTE FUNCTION economy_ledger_reject_change();
";

    // The table's own drop takes its triggers; the function gets back the words it had.
    public const string DropSql = @"
CREATE OR REPLACE FUNCTION economy_ledger_reject_change() RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    RAISE EXCEPTION 'economy_ledger is append-only: % is not allowed', TG_OP
        USING ERRCODE = 'restrict_violation';
END;
$$;
";
}
}
