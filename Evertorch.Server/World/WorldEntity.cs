using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     Anything with an authoritative presence on a map: its identity, transform, and state flags. Only the tick
///     thread reads or writes it.
/// </summary>
public abstract class WorldEntity
{
    protected WorldEntity(
        EntityId id,
        WorldPosition position,
        WorldDirection facing,
        float movementSpeed,
        int maxHealth,
        float attackRange)
    {
        Id = id;
        Position = position;
        Facing = facing;
        MovementSpeed = movementSpeed;
        MaxHealth = maxHealth;
        CurrentHealth = maxHealth;
        AttackRange = attackRange;
    }

    public EntityId Id { get; }

    public abstract EntityKind Kind { get; }

    /// <summary>
    ///     The definition clients present this entity with; its kind follows <see cref="Kind" />.
    /// </summary>
    public abstract string DefinitionId { get; }

    public WorldPosition Position { get; set; }

    public WorldDirection Facing { get; set; }

    public float VelocityX { get; set; }

    public float VelocityY { get; set; }

    public float VelocityZ { get; set; }

    public EntityStateFlags StateFlags { get; set; }

    /// <summary>
    ///     The entity this one has selected; the default value means none.
    /// </summary>
    public EntityId Target { get; set; }

    /// <summary>
    ///     World units per second after the movement rules were applied; never a value a client supplied.
    /// </summary>
    public float MovementSpeed { get; }

    public int MaxHealth { get; }

    public int CurrentHealth { get; set; }

    /// <summary>
    ///     How far, centre to centre on X/Z, a basic attack reaches.
    /// </summary>
    public float AttackRange { get; }

    public bool IsDead => (StateFlags & EntityStateFlags.Dead) != 0;

    /// <summary>
    ///     The HP ratio clients may see: a monster's, for its health bar. Other kinds share none.
    /// </summary>
    public ushort SharedHealthPermille =>
        Kind == EntityKind.Monster ? HealthRatio.ToPermille(CurrentHealth, MaxHealth) : (ushort)0;
}
}
