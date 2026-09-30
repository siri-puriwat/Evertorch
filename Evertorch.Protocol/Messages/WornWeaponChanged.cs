using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     A player this client sees now wears another weapon, or none (Network Protocol §6, §9): the item ID of the weapon
///     in its hand, empty when it is unarmed. Its owner learns the change from its inventory instead.
/// </summary>
public sealed class WornWeaponChanged
{
    public WornWeaponChanged(EntityId entity, string wornWeapon)
    {
        Entity = entity;
        WornWeapon = wornWeapon ?? throw new ArgumentNullException(nameof(wornWeapon));
    }

    public EntityId Entity { get; }

    public string WornWeapon { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out WornWeaponChanged? message)
    {
        message = null;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.WornWeaponChanged)
            || !reader.TryReadInt64(out long entity)
            || !reader.TryReadString(ProtocolLimits.MaxDefinitionIdBytes, out string wornWeapon)
            || !reader.IsAtEnd
            || entity <= 0
            || (wornWeapon.Length > 0 && !ItemDefinitionId.TryCreate(wornWeapon, out ItemDefinitionId _)))
        {
            return false;
        }

        message = new WornWeaponChanged(new EntityId(entity), wornWeapon);
        return true;
    }

    public int GetEncodedLength()
    {
        return sizeof(ushort) + sizeof(long) +
            WireText.GetEncodedLength(WornWeapon, ProtocolLimits.MaxDefinitionIdBytes);
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.WornWeaponChanged);
        writer.WriteInt64(Entity.Value);
        writer.WriteString(WornWeapon, ProtocolLimits.MaxDefinitionIdBytes);
        return writer.Position;
    }
}
}
