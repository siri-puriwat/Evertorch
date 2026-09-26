namespace Evertorch.Protocol
{
/// <summary>
///     Where a quest stands for a character (Gameplay Systems §2.2). An available quest has no entry, and an active one
///     is ready once its progress reaches its count. Zero is never sent.
/// </summary>
public enum QuestState : byte
{
    None = 0,
    Active = 1,
    Completed = 2
}
}
