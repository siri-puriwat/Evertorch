namespace Evertorch.Protocol
{
public enum MessageDelivery
{
    ReliableOrdered = 0,

    /// <summary>
    /// May be lost; an older packet arriving after a newer one is dropped.
    /// </summary>
    UnreliableSequenced = 1,
}
}
