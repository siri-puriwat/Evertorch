using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     A player's authoritative presence on one map.
/// </summary>
public sealed class PlayerEntity : WorldEntity
{
    public PlayerEntity(
        EntityId id,
        CharacterId character,
        ConnectionId owner,
        JobDefinitionId job,
        WorldPosition position,
        WorldDirection facing,
        float movementSpeed,
        PrimaryStats primary,
        DerivedStats stats,
        float attackRange)
        : base(id, position, facing, movementSpeed, stats.MaxHp, attackRange)
    {
        Character = character;
        Owner = owner;
        Job = job;
        Primary = primary;
        Stats = stats;
    }

    public CharacterId Character { get; }

    public ConnectionId Owner { get; }

    public JobDefinitionId Job { get; }

    /// <summary>
    ///     The character's derived statistics from the rules; recalculated only when their inputs change.
    /// </summary>
    public DerivedStats Stats { get; }

    public PrimaryStats Primary { get; }

    public override EntityKind Kind => EntityKind.Player;

    public override string DefinitionId => Job.Value;
}
}
