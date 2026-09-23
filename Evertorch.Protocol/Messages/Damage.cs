using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     A swing resolved at impact. Sent to every client that knows the target; the source is 0 for a receiver that
///     has no spawn for it. A miss deals nothing, a hit or a critical at least 1.
/// </summary>
public readonly struct Damage
{
    public const int EncodedLength =
        sizeof(ushort) + 2 * sizeof(long) + sizeof(byte) + sizeof(uint) + sizeof(uint) + sizeof(ushort);

    public Damage(
        EntityId source,
        EntityId target,
        CombatResult result,
        uint amount,
        uint serverTick,
        ushort targetHealthPermille)
    {
        Source = source;
        Target = target;
        Result = result;
        Amount = amount;
        ServerTick = serverTick;
        TargetHealthPermille = targetHealthPermille;
    }

    public EntityId Source { get; }

    public EntityId Target { get; }

    public CombatResult Result { get; }

    public uint Amount { get; }

    public uint ServerTick { get; }

    /// <summary>
    ///     A monster target's HP after this damage, in thousandths; 0 when the target is a player, whose HP is not
    ///     shared this way.
    /// </summary>
    public ushort TargetHealthPermille { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out Damage message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.Damage)
            || !reader.TryReadInt64(out long damageSource)
            || !reader.TryReadInt64(out long target)
            || !reader.TryReadByte(out byte resultValue)
            || !reader.TryReadUInt32(out uint amount)
            || !reader.TryReadUInt32(out uint serverTick)
            || !reader.TryReadUInt16(out ushort healthPermille)
            || !reader.IsAtEnd
            || target == 0
            || healthPermille > HealthRatio.Full)
        {
            return false;
        }

        var result = (CombatResult)resultValue;
        if (!WireEnums.IsDefined(result) || result == CombatResult.Miss != (amount == 0))
        {
            return false;
        }

        message = new Damage(
            new EntityId(damageSource),
            new EntityId(target),
            result,
            amount,
            serverTick,
            healthPermille);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.Damage);
        writer.WriteInt64(Source.Value);
        writer.WriteInt64(Target.Value);
        writer.WriteByte((byte)Result);
        writer.WriteUInt32(Amount);
        writer.WriteUInt32(ServerTick);
        writer.WriteUInt16(TargetHealthPermille);
        return writer.Position;
    }
}
}
