namespace Evertorch.Server
{
/// <summary>
///     The stable order in which one simulation tick processes work. Values are ordered; do not renumber them.
/// </summary>
public enum TickPhase
{
    DrainCommands = 0,
    ApplyCommands = 1,
    Movement = 2,
    Combat = 3,
    MonsterAi = 4,
    FinalizeWorld = 5,
    BuildSnapshots = 6,
    SchedulePersistence = 7
}
}
