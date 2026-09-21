using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
/// Tells the client which entity it controls and where it stands. The rest of the baseline follows on the same
/// reliable stream as one <see cref="EntitySpawn"/> per visible entity.
/// </summary>
public sealed class WorldEntered
{
    public WorldEntered(
        MapDefinitionId map,
        uint mapInstance,
        EntityId localEntity,
        uint serverTick,
        WorldPosition position,
        WorldDirection facing,
        float movementSpeed)
    {
        Map = map;
        MapInstance = mapInstance;
        LocalEntity = localEntity;
        ServerTick = serverTick;
        Position = position;
        Facing = facing;
        MovementSpeed = movementSpeed;
    }

    public MapDefinitionId Map { get; }

    public uint MapInstance { get; }

    public EntityId LocalEntity { get; }

    public uint ServerTick { get; }

    public WorldPosition Position { get; }

    public WorldDirection Facing { get; }

    /// <summary>
    /// Authoritative world units per second for the local entity; the one number prediction needs from the rules.
    /// </summary>
    public float MovementSpeed { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out WorldEntered? message)
    {
        message = null;
        WireReader reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.WorldEntered)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string mapText)
            || !reader.TryReadUInt32(out uint mapInstance)
            || !reader.TryReadInt64(out long localEntity)
            || !reader.TryReadUInt32(out uint serverTick)
            || !reader.TryReadPosition(out WorldPosition position)
            || !reader.TryReadDirection(out WorldDirection facing)
            || !reader.TryReadSingle(out float movementSpeed)
            || !reader.IsAtEnd
            || movementSpeed < 0f
            || !MapDefinitionId.TryCreate(mapText, out MapDefinitionId map))
        {
            return false;
        }

        message = new WorldEntered(
            map,
            mapInstance,
            new EntityId(localEntity),
            serverTick,
            position,
            facing,
            movementSpeed);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
            + WireText.GetEncodedLength(Map.Value, ProtocolLimits.MaxDefinitionIdBytes)
            + sizeof(uint)
            + sizeof(long)
            + sizeof(uint)
            + (3 * sizeof(float))
            + (2 * sizeof(float))
            + sizeof(float);
    }

    public int Write(Span<byte> destination)
    {
        WireWriter writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.WorldEntered);
        writer.WriteString(Map.Value, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WriteUInt32(MapInstance);
        writer.WriteInt64(LocalEntity.Value);
        writer.WriteUInt32(ServerTick);
        writer.WritePosition(Position);
        writer.WriteDirection(Facing);
        writer.WriteSingle(MovementSpeed);
        return writer.Position;
    }
}
}
