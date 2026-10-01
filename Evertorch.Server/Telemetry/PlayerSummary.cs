using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     One player as an operator sees it. It deliberately has no field for a token or a sign-in identity.
/// </summary>
public sealed class PlayerSummary
{
    public PlayerSummary(
        ConnectionId connection,
        CharacterId character,
        EntityId entity,
        MapDefinitionId map,
        WorldPosition position,
        int roundTripMilliseconds,
        int queuedInputs,
        long staleInputs,
        long droppedInputs,
        long refusedCommands,
        int level,
        long experience,
        long otherEpochInputs,
        long coins,
        int jobLevel = 1,
        JobDefinitionId job = default,
        string name = "")
    {
        Connection = connection;
        Character = character;
        Entity = entity;
        Map = map;
        Position = position;
        RoundTripMilliseconds = roundTripMilliseconds;
        QueuedInputs = queuedInputs;
        StaleInputs = staleInputs;
        DroppedInputs = droppedInputs;
        RefusedCommands = refusedCommands;
        Level = level;
        Experience = experience;
        OtherEpochInputs = otherEpochInputs;
        Coins = coins;
        JobLevel = jobLevel;
        Job = job;
        Name = name;
    }

    public ConnectionId Connection { get; }

    public CharacterId Character { get; }

    public EntityId Entity { get; }

    public MapDefinitionId Map { get; }

    public WorldPosition Position { get; }

    /// <summary>
    ///     -1 until the transport has measured one.
    /// </summary>
    public int RoundTripMilliseconds { get; }

    public int QueuedInputs { get; }

    public long StaleInputs { get; }

    public long DroppedInputs { get; }

    /// <summary>
    ///     Well-formed commands the server refused, such as a target the client may not select.
    /// </summary>
    public long RefusedCommands { get; }

    public int Level { get; }

    /// <summary>
    ///     Experience toward the next level.
    /// </summary>
    public long Experience { get; }

    /// <summary>
    ///     Movement inputs dropped because they were made for the map before a transfer.
    /// </summary>
    public long OtherEpochInputs { get; }

    /// <summary>
    ///     The character's coins as last committed.
    /// </summary>
    public long Coins { get; }

    public int JobLevel { get; }

    /// <summary>
    ///     The player's job, which the console's players line names.
    /// </summary>
    public JobDefinitionId Job { get; }

    /// <summary>
    ///     The character's name, which the console's players line shows beside its number.
    /// </summary>
    public string Name { get; }
}
}
