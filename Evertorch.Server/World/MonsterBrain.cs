using Evertorch.Game;

namespace Evertorch.Server
{
public enum MonsterAiState
{
    Idle = 0,
    Roam = 1,
    Chase = 2,
    ReturnHome = 3,
    Dead = 4
}

/// <summary>
///     What one monster's AI remembers between ticks (Gameplay Systems §10). Only the tick thread reads or writes it.
/// </summary>
public sealed class MonsterBrain
{
    public MonsterAiState State { get; set; } = MonsterAiState.Idle;

    /// <summary>
    ///     The time of the next decision; each one adds the scan interval to the last, from the monster's spawn.
    /// </summary>
    public long NextDecisionMs { get; set; } = long.MinValue;

    public long IdleUntilMs { get; set; }

    public long DiedAtMs { get; set; }

    public int Decisions { get; set; }

    /// <summary>
    ///     The last player that damaged this monster, which a passive monster takes as its target.
    /// </summary>
    public EntityId LastAttacker { get; set; }

    public PathFollower Path { get; } = new();

    /// <summary>
    ///     When the monster may next call its kin, and when it may next answer a call: each at most once a second
    ///     (Gameplay Systems §10).
    /// </summary>
    public long NextCallMs { get; set; } = long.MinValue;

    public long NextAnswerMs { get; set; } = long.MinValue;

    /// <summary>
    ///     Whether <see cref="Path" /> leads away from a target that came too close, which the monster walks although
    ///     the target is in reach (Gameplay Systems §10).
    /// </summary>
    public bool IsRetreating { get; set; }

    public WorldPosition ChaseGoal { get; set; }

    /// <summary>
    ///     Whether the walk home began at the leash's edge; a boss that arrives home from it recovers (Gameplay Systems
    ///     §10), and one sent home by a lost target does not.
    /// </summary>
    public bool IsLeashed { get; set; }

    /// <summary>
    ///     Where the AI wants to move next tick; the movement phase steps the monster with it.
    /// </summary>
    public WorldDirection DesiredDirection { get; set; }
}
}
