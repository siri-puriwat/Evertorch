namespace Evertorch.Protocol
{
/// <summary>
///     How a swing ended. A perfect dodge travels as a miss. Zero is never sent.
/// </summary>
public enum CombatResult : byte
{
    None = 0,
    Hit = 1,
    Miss = 2,
    Critical = 3
}
}
