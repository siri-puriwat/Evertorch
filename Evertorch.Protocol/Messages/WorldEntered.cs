using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Tells the client which character and entity it controls, where it stands, and its HP, SP, level, and
///     experience. The rest of the baseline follows on the same reliable stream as one <see cref="EntitySpawn" /> per
///     visible entity.
/// </summary>
public sealed class WorldEntered
{
    public WorldEntered(
        MapDefinitionId map,
        uint mapInstance,
        EntityId localEntity,
        JobDefinitionId job,
        uint serverTick,
        WorldPosition position,
        WorldDirection facing,
        float movementSpeed,
        uint currentHealth,
        uint maximumHealth,
        float attackRange,
        uint lastCommandSequence,
        CharacterId character,
        ushort level,
        ulong experience,
        ulong experienceToNextLevel,
        uint currentSpirit,
        uint maximumSpirit)
    {
        Map = map;
        MapInstance = mapInstance;
        LocalEntity = localEntity;
        Job = job;
        ServerTick = serverTick;
        Position = position;
        Facing = facing;
        MovementSpeed = movementSpeed;
        CurrentHealth = currentHealth;
        MaximumHealth = maximumHealth;
        AttackRange = attackRange;
        LastCommandSequence = lastCommandSequence;
        Character = character;
        Level = level;
        Experience = experience;
        ExperienceToNextLevel = experienceToNextLevel;
        CurrentSpirit = currentSpirit;
        MaximumSpirit = maximumSpirit;
    }

    public MapDefinitionId Map { get; }

    public uint MapInstance { get; }

    public EntityId LocalEntity { get; }

    /// <summary>
    ///     The local entity's job. The server never sends a client an <see cref="EntitySpawn" /> for its own entity, so
    ///     this is how the client learns which view to show for itself.
    /// </summary>
    public JobDefinitionId Job { get; }

    public uint ServerTick { get; }

    public WorldPosition Position { get; }

    public WorldDirection Facing { get; }

    /// <summary>
    ///     Authoritative world units per second for the local entity; the one number prediction needs from the rules.
    /// </summary>
    public float MovementSpeed { get; }

    /// <summary>
    ///     The local character's exact HP; never more than <see cref="MaximumHealth" />.
    /// </summary>
    public uint CurrentHealth { get; }

    /// <summary>
    ///     At least 1.
    /// </summary>
    public uint MaximumHealth { get; }

    /// <summary>
    ///     The basic attack's range in world units, which the client walks within before the server lets a swing
    ///     begin. The server still checks range itself.
    /// </summary>
    public float AttackRange { get; }

    /// <summary>
    ///     The newest command sequence the server has processed for the character, 0 when none. The sequence belongs
    ///     to the character session and continues across a reconnect, so the client numbers its next command after it
    ///     (Network Protocol §8).
    /// </summary>
    public uint LastCommandSequence { get; }

    /// <summary>
    ///     The character the server entered, so a client that asked twice knows which one it got (Network Protocol
    ///     §5); never 0.
    /// </summary>
    public CharacterId Character { get; }

    /// <summary>
    ///     The base level; at least 1.
    /// </summary>
    public ushort Level { get; }

    /// <summary>
    ///     Experience toward the next level.
    /// </summary>
    public ulong Experience { get; }

    /// <summary>
    ///     What the next level needs in all; 0 at the level cap.
    /// </summary>
    public ulong ExperienceToNextLevel { get; }

    /// <summary>
    ///     Never more than <see cref="MaximumSpirit" />.
    /// </summary>
    public uint CurrentSpirit { get; }

    public uint MaximumSpirit { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out WorldEntered? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.WorldEntered)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string mapText)
            || !reader.TryReadUInt32(out uint mapInstance)
            || !reader.TryReadInt64(out long localEntity)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string jobText)
            || !reader.TryReadUInt32(out uint serverTick)
            || !reader.TryReadPosition(out WorldPosition position)
            || !reader.TryReadDirection(out WorldDirection facing)
            || !reader.TryReadSingle(out float movementSpeed)
            || !reader.TryReadUInt32(out uint currentHealth)
            || !reader.TryReadUInt32(out uint maximumHealth)
            || !reader.TryReadSingle(out float attackRange)
            || !reader.TryReadUInt32(out uint lastCommandSequence)
            || !reader.TryReadInt64(out long character)
            || !reader.TryReadUInt16(out ushort level)
            || !reader.TryReadUInt64(out ulong experience)
            || !reader.TryReadUInt64(out ulong experienceToNextLevel)
            || !reader.TryReadUInt32(out uint currentSpirit)
            || !reader.TryReadUInt32(out uint maximumSpirit)
            || !reader.IsAtEnd
            || movementSpeed < 0f
            || maximumHealth == 0
            || currentHealth > maximumHealth
            || attackRange < 0f
            || character == 0
            || level == 0
            || currentSpirit > maximumSpirit
            || !MapDefinitionId.TryCreate(mapText, out MapDefinitionId map)
            || !JobDefinitionId.TryCreate(jobText, out JobDefinitionId job))
        {
            return false;
        }

        message = new WorldEntered(
            map,
            mapInstance,
            new EntityId(localEntity),
            job,
            serverTick,
            position,
            facing,
            movementSpeed,
            currentHealth,
            maximumHealth,
            attackRange,
            lastCommandSequence,
            new CharacterId(character),
            level,
            experience,
            experienceToNextLevel,
            currentSpirit,
            maximumSpirit);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + WireText.GetEncodedLength(Map.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(uint)
            + sizeof(long)
            + WireText.GetEncodedLength(Job.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(uint)
            + 3 * sizeof(float)
            + 2 * sizeof(float)
            + sizeof(float)
            + sizeof(uint)
            + sizeof(uint)
            + sizeof(float)
            + sizeof(uint)
            + sizeof(long)
            + sizeof(ushort)
            + 2 * sizeof(ulong)
            + 2 * sizeof(uint);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.WorldEntered);
        writer.WriteString(Map.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(MapInstance);
        writer.WriteInt64(LocalEntity.Value);
        writer.WriteString(Job.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(ServerTick);
        writer.WritePosition(Position);
        writer.WriteDirection(Facing);
        writer.WriteSingle(MovementSpeed);
        writer.WriteUInt32(CurrentHealth);
        writer.WriteUInt32(MaximumHealth);
        writer.WriteSingle(AttackRange);
        writer.WriteUInt32(LastCommandSequence);
        writer.WriteInt64(Character.Value);
        writer.WriteUInt16(Level);
        writer.WriteUInt64(Experience);
        writer.WriteUInt64(ExperienceToNextLevel);
        writer.WriteUInt32(CurrentSpirit);
        writer.WriteUInt32(MaximumSpirit);
        return writer.Position;
    }
}
}
