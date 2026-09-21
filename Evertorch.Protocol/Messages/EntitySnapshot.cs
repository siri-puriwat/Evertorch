using System;
using System.Collections.Generic;

namespace Evertorch.Protocol
{
/// <summary>
/// Authoritative transforms for some of the entities a client can see, and the last of that client's inputs the
/// server has applied. Snapshots are disposable: each is complete for the entities it lists and supersedes older ones.
/// </summary>
public sealed class EntitySnapshot
{
    /// <summary>
    /// Keeps the largest snapshot inside one unreliable datagram. A larger area of interest is sent as several
    /// snapshots with the same tick.
    /// </summary>
    public const int MaxEntities = 24;

    public const int HeaderLength = sizeof(ushort) + sizeof(uint) + sizeof(uint) + sizeof(byte);
    public const int MaxEncodedLength = HeaderLength + (MaxEntities * EntityState.EncodedLength);

    public EntitySnapshot(uint serverTick, uint lastProcessedInputSequence, IReadOnlyList<EntityState> entities)
    {
        if (entities == null)
        {
            throw new ArgumentNullException(nameof(entities));
        }

        if (entities.Count > MaxEntities)
        {
            throw new ArgumentException("A snapshot carries at most " + MaxEntities + " entities.", nameof(entities));
        }

        ServerTick = serverTick;
        LastProcessedInputSequence = lastProcessedInputSequence;
        Entities = entities;
    }

    public uint ServerTick { get; }

    /// <summary>
    /// Sequence of the newest input from the receiving client that this state already includes; 0 before any.
    /// </summary>
    public uint LastProcessedInputSequence { get; }

    public IReadOnlyList<EntityState> Entities { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out EntitySnapshot? message)
    {
        message = null;
        WireReader reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.EntitySnapshot)
            || !reader.TryReadUInt32(out uint serverTick)
            || !reader.TryReadUInt32(out uint lastProcessedInputSequence)
            || !reader.TryReadByte(out byte count)
            || count > MaxEntities
            || source.Length != HeaderLength + (count * EntityState.EncodedLength))
        {
            // The count is checked against the limit and the actual length before anything is allocated for it.
            return false;
        }

        EntityState[] entities = new EntityState[count];
        for (int index = 0; index < count; index++)
        {
            if (!EntityState.TryRead(ref reader, out entities[index]))
            {
                return false;
            }
        }

        message = new EntitySnapshot(serverTick, lastProcessedInputSequence, entities);
        return true;
    }

    public int GetEncodedLength()
    {
        return HeaderLength + (Entities.Count * EntityState.EncodedLength);
    }

    public int Write(Span<byte> destination)
    {
        WireWriter writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.EntitySnapshot);
        writer.WriteUInt32(ServerTick);
        writer.WriteUInt32(LastProcessedInputSequence);
        writer.WriteByte((byte)Entities.Count);
        for (int index = 0; index < Entities.Count; index++)
        {
            Entities[index].Write(ref writer);
        }

        return writer.Position;
    }
}
}
