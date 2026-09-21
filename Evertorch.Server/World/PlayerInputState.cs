using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
/// Everything the movement phase remembers about one player's input between ticks.
/// </summary>
public sealed class PlayerInputState
{
    public PlayerInputState(int queueCapacity)
    {
        Queue = new PlayerInputQueue(queueCapacity);
    }

    public PlayerInputQueue Queue { get; }

    /// <summary>
    /// Sequence of the newest input already applied; 0 before any. Sent back in every snapshot.
    /// </summary>
    public uint LastProcessedSequence { get; set; }

    /// <summary>
    /// The direction being applied. It outlives the input that set it for the hold timeout and is then cleared.
    /// </summary>
    public WorldDirection Direction { get; set; }

    public int TicksSinceInput { get; set; }

    public bool HasClientTickOffset { get; set; }

    /// <summary>
    /// Client tick minus server tick when the offset was last set. The client clock is advisory only.
    /// </summary>
    public uint ClientTickOffset { get; set; }

    /// <summary>
    /// Times the client's tick wandered beyond the allowed drift and the offset was set anew.
    /// </summary>
    public long TickDriftRebases { get; set; }
}
}
