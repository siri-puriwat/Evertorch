namespace Evertorch.Client
{
/// <summary>
///     Which trade command a sequence numbered, so a refusal of it can be put into words.
/// </summary>
public enum TradeCommand
{
    None,
    Request,
    Reply,
    Offer,
    Lock,
    Confirm,
    Cancel
}
}
