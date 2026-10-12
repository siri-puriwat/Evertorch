using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The weapon and the armor the player <see cref="Entity" /> wears, each with its refine level and cards, sent to the
///     asker of an <see cref="InspectPlayer" /> alone (Network Protocol §9); the inspected player hears nothing.
/// </summary>
public sealed class InspectedEquipment
{
    /// <summary>
    ///     The longest message: two items of the longest ID, each with four cards of the longest ID.
    /// </summary>
    public const int MaxEncodedLength = sizeof(ushort) + sizeof(long)
        + 2 * (WireText.LengthPrefixBytes + ProtocolLimits.MaxDefinitionIdBytes + 2 * sizeof(byte)
            + ProtocolLimits.MaxCardsPerItem * (WireText.LengthPrefixBytes + ProtocolLimits.MaxDefinitionIdBytes));

    public InspectedEquipment(EntityId entity, InspectedItem weapon, InspectedItem armor)
    {
        if (entity.Value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(entity), "A player's entity ID is at least 1.");
        }

        Entity = entity;
        Weapon = weapon;
        Armor = armor;
    }

    public EntityId Entity { get; }

    public InspectedItem Weapon { get; }

    public InspectedItem Armor { get; }

    /// <summary>
    ///     False for a malformed message, including an entity ID below 1 and a level or a card on an empty slot.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out InspectedEquipment? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (source.Length > MaxEncodedLength
            || !reader.TryReadOpcode(MessageOpcode.InspectedEquipment)
            || !reader.TryReadInt64(out long entity)
            || !InspectedItem.TryRead(ref reader, out InspectedItem weapon)
            || !InspectedItem.TryRead(ref reader, out InspectedItem armor)
            || !reader.IsAtEnd
            || entity <= 0)
        {
            return false;
        }

        message = new InspectedEquipment(new EntityId(entity), weapon, armor);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort) + sizeof(long) + Weapon.GetEncodedLength() + Armor.GetEncodedLength();
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.InspectedEquipment);
        writer.WriteInt64(Entity.Value);
        Weapon.Write(ref writer);
        Armor.Write(ref writer);
        return writer.Position;
    }
}
}
