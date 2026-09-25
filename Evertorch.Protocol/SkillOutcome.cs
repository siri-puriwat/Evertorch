namespace Evertorch.Protocol
{
/// <summary>
///     How a cast ended on its target: the combat results a swing can have, a heal, or a status effect applied. A
///     perfect dodge travels as a miss. Zero is never sent.
/// </summary>
public enum SkillOutcome : byte
{
    None = 0,
    Hit = 1,
    Miss = 2,
    Critical = 3,
    Healed = 4,
    Applied = 5
}
}
