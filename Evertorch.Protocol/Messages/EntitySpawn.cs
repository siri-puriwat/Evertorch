using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
/// An entity entered this client's area of interest. It always precedes any other reliable message about that
/// entity; snapshots travel separately and may mention it earlier, which the client ignores.
/// </summary>
public sealed class EntitySpawn
{
    public EntitySpawn(
        EntityId entity,
        EntityKind kind,
        string definitionId,
        WorldPosition position,
        WorldDirection facing,
        EntityStateFlags stateFlags)
    {
        Entity = entity;
        Kind = kind;
        DefinitionId = definitionId ?? throw new ArgumentNullException(nameof(definitionId));
        Position = position;
        Facing = facing;
        StateFlags = stateFlags;
    }

    public EntityId Entity { get; }

    public EntityKind Kind { get; }

    /// <summary>
    /// The definition the client presents this entity with. Its kind is decided by <see cref="Kind"/>.
    /// </summary>
    public string DefinitionId { get; }

    public WorldPosition Position { get; }

    public WorldDirection Facing { get; }

    public EntityStateFlags StateFlags { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out EntitySpawn? message)
    {
        message = null;
        WireReader reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.EntitySpawn)
            || !reader.TryReadInt64(out long entity)
            || !reader.TryReadByte(out byte kindValue)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string definitionId)
            || !reader.TryReadPosition(out WorldPosition position)
            || !reader.TryReadDirection(out WorldDirection facing)
            || !reader.TryReadUInt16(out ushort flagsValue)
            || !reader.IsAtEnd)
        {
            return false;
        }

        EntityKind kind = (EntityKind)kindValue;
        EntityStateFlags stateFlags = (EntityStateFlags)flagsValue;
        if (!WireEnums.IsDefined(kind) || !WireEnums.IsDefined(stateFlags) || !IsDefinitionOfKind(kind, definitionId))
        {
            return false;
        }

        message = new EntitySpawn(new EntityId(entity), kind, definitionId, position, facing, stateFlags);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort)
               + sizeof(long)
               + sizeof(byte)
               + WireText.GetEncodedLength(DefinitionId, ProtocolLimits.MaxDefinitionIdBytes)
               + (3 * sizeof(float))
               + (2 * sizeof(float))
               + sizeof(ushort);
    }

    public int Write(Span<byte> destination)
    {
        WireWriter writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.EntitySpawn);
        writer.WriteInt64(Entity.Value);
        writer.WriteByte((byte)Kind);
        writer.WriteString(DefinitionId, ProtocolLimits.MaxDefinitionIdBytes);
        writer.WritePosition(Position);
        writer.WriteDirection(Facing);
        writer.WriteUInt16((ushort)StateFlags);
        return writer.Position;
    }

    private static bool IsDefinitionOfKind(EntityKind kind, string definitionId)
    {
        return kind == EntityKind.Player && JobDefinitionId.TryCreate(definitionId, out JobDefinitionId _);
    }
}
}
