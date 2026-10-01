using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     An entity entered this client's area of interest. It always precedes any other reliable message about that
///     entity; snapshots travel separately and may mention it earlier, which the client ignores.
/// </summary>
public sealed class EntitySpawn
{
    public EntitySpawn(
        EntityId entity,
        EntityKind kind,
        string definitionId,
        WorldPosition position,
        WorldDirection facing,
        EntityStateFlags stateFlags,
        ushort healthPermille,
        string wornWeapon = "",
        string name = "")
    {
        Entity = entity;
        Kind = kind;
        DefinitionId = definitionId ?? throw new ArgumentNullException(nameof(definitionId));
        Position = position;
        Facing = facing;
        StateFlags = stateFlags;
        HealthPermille = healthPermille;
        WornWeapon = wornWeapon ?? throw new ArgumentNullException(nameof(wornWeapon));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    public EntityId Entity { get; }

    public EntityKind Kind { get; }

    /// <summary>
    ///     The definition the client presents this entity with. Its kind is decided by <see cref="Kind" />.
    /// </summary>
    public string DefinitionId { get; }

    public WorldPosition Position { get; }

    public WorldDirection Facing { get; }

    public EntityStateFlags StateFlags { get; }

    /// <summary>
    ///     A monster's HP in thousandths of its maximum, for a health bar; 0 for every other kind, whose HP is not
    ///     shared (Network Protocol §9).
    /// </summary>
    public ushort HealthPermille { get; }

    /// <summary>
    ///     The item ID of the weapon a player wears, which everyone near sees in its hand; empty when it wears none and
    ///     for every other kind (Network Protocol §6).
    /// </summary>
    public string WornWeapon { get; }

    /// <summary>
    ///     A player's character name, which everyone who sees it reads; empty for every other kind (Network Protocol §6).
    /// </summary>
    public string Name { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out EntitySpawn? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.EntitySpawn)
            || !reader.TryReadInt64(out long entity)
            || !reader.TryReadByte(out byte kindValue)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string definitionId)
            || !reader.TryReadPosition(out WorldPosition position)
            || !reader.TryReadDirection(out WorldDirection facing)
            || !reader.TryReadUInt16(out ushort flagsValue)
            || !reader.TryReadUInt16(out ushort healthPermille)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string wornWeapon)
            || !reader.TryReadString(ProtocolLimits.MaxCharacterNameBytes, out string name)
            || !reader.IsAtEnd)
        {
            return false;
        }

        var kind = (EntityKind)kindValue;
        var stateFlags = (EntityStateFlags)flagsValue;
        if (!WireEnums.IsDefined(kind)
            || !WireEnums.IsDefined(stateFlags)
            || !IsDefinitionOfKind(kind, definitionId)
            || !IsHealthValid(kind, healthPermille)
            || !IsWornWeaponValid(kind, wornWeapon)
            || !IsNameValid(kind, name))
        {
            return false;
        }

        message = new EntitySpawn(
            new EntityId(entity),
            kind,
            definitionId,
            position,
            facing,
            stateFlags,
            healthPermille,
            wornWeapon,
            name);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + sizeof(long)
            + sizeof(byte)
            + WireText.GetEncodedLength(DefinitionId, ProtocolLimits.MaxDefinitionIdBytes)
            + 3 * sizeof(float)
            + 2 * sizeof(float)
            + sizeof(ushort)
            + sizeof(ushort)
            + WireText.GetEncodedLength(WornWeapon, ProtocolLimits.MaxDefinitionIdBytes)
            + WireText.GetEncodedLength(Name, ProtocolLimits.MaxCharacterNameBytes);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.EntitySpawn);
        writer.WriteInt64(Entity.Value);
        writer.WriteByte((byte)Kind);
        writer.WriteString(DefinitionId, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WritePosition(Position);
        writer.WriteDirection(Facing);
        writer.WriteUInt16((ushort)StateFlags);
        writer.WriteUInt16(HealthPermille);
        writer.WriteString(WornWeapon, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteString(Name, ProtocolLimits.MaxCharacterNameBytes);
        return writer.Position;
    }

    private static bool IsHealthValid(EntityKind kind, ushort healthPermille)
    {
        return kind == EntityKind.Monster ? healthPermille <= HealthRatio.Full : healthPermille == 0;
    }

    private static bool IsWornWeaponValid(EntityKind kind, string wornWeapon)
    {
        return wornWeapon.Length == 0
            || (kind == EntityKind.Player && ItemDefinitionId.TryCreate(wornWeapon, out ItemDefinitionId _));
    }

    private static bool IsNameValid(EntityKind kind, string name)
    {
        return kind == EntityKind.Player ? CharacterNames.IsValid(name) : name.Length == 0;
    }

    private static bool IsDefinitionOfKind(EntityKind kind, string definitionId)
    {
        switch (kind)
        {
            case EntityKind.Player:
                return JobDefinitionId.TryCreate(definitionId, out JobDefinitionId _);
            case EntityKind.Monster:
                return MonsterDefinitionId.TryCreate(definitionId, out MonsterDefinitionId _);
            case EntityKind.ItemDrop:
                return ItemDefinitionId.TryCreate(definitionId, out ItemDefinitionId _);
            case EntityKind.Npc:
                return NpcDefinitionId.TryCreate(definitionId, out NpcDefinitionId _);
            default:
                return false;
        }
    }
}
}
