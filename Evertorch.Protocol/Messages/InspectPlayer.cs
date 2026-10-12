using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks to see the weapon and the armor the player <see cref="Entity" /> wears (Gameplay Systems §11.7), answered
///     with <see cref="InspectedEquipment" />. Inspection changes nothing and cannot be refused by the inspected.
/// </summary>
public sealed class InspectPlayer
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long) + sizeof(uint);

    public InspectPlayer(EntityId entity, uint commandSequence)
    {
        if (entity.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entity), "A player's entity ID is at least 1.");
        }

        Entity = entity;
        CommandSequence = commandSequence;
    }

    public EntityId Entity { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including an entity ID below 1.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out InspectPlayer? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.InspectPlayer)
            || !reader.TryReadInt64(out long entity)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || entity <= 0)
        {
            return false;
        }

        message = new InspectPlayer(new EntityId(entity), sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.InspectPlayer);
        writer.WriteInt64(Entity.Value);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
