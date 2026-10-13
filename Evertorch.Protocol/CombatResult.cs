namespace Evertorch.Protocol
{
/// <summary>
///     How a swing ended. A perfect dodge travels as a miss; a hit whose element deals nothing to its target is immune
///     (Gameplay Systems §3). Zero is never sent.
/// </summary>
public enum CombatResult : byte
{
    None = 0,
    Hit = 1,
    Miss = 2,
    Critical = 3,
    Immune = 4
}
}
