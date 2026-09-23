using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     A swing began on <see cref="StartTick" /> with the authoritative timing, which client presentation is scaled
///     to. The target is 0 for a receiver that has no spawn for it.
/// </summary>
public readonly struct AttackStarted
{
    public const int EncodedLength = sizeof(ushort) + 2 * sizeof(long) + sizeof(uint) + 4 * sizeof(uint);

    public AttackStarted(EntityId attacker, EntityId target, uint startTick, AttackTiming timing)
    {
        Attacker = attacker;
        Target = target;
        StartTick = startTick;
        Timing = timing;
    }

    public EntityId Attacker { get; }

    public EntityId Target { get; }

    public uint StartTick { get; }

    public AttackTiming Timing { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out AttackStarted message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.AttackStarted)
            || !reader.TryReadInt64(out long attacker)
            || !reader.TryReadInt64(out long target)
            || !reader.TryReadUInt32(out uint startTick)
            || !reader.TryReadUInt32(out uint interval)
            || !reader.TryReadUInt32(out uint windup)
            || !reader.TryReadUInt32(out uint impact)
            || !reader.TryReadUInt32(out uint recovery)
            || !reader.IsAtEnd
            || attacker == 0
            || impact > interval)
        {
            return false;
        }

        var timing = new AttackTiming(
            TimeSpan.FromMilliseconds(interval),
            TimeSpan.FromMilliseconds(windup),
            TimeSpan.FromMilliseconds(impact),
            TimeSpan.FromMilliseconds(recovery));
        message = new AttackStarted(new EntityId(attacker), new EntityId(target), startTick, timing);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.AttackStarted);
        writer.WriteInt64(Attacker.Value);
        writer.WriteInt64(Target.Value);
        writer.WriteUInt32(StartTick);
        writer.WriteUInt32(ToMilliseconds(Timing.Interval));
        writer.WriteUInt32(ToMilliseconds(Timing.Windup));
        writer.WriteUInt32(ToMilliseconds(Timing.Impact));
        writer.WriteUInt32(ToMilliseconds(Timing.Recovery));
        return writer.Position;
    }

    private static uint ToMilliseconds(TimeSpan value)
    {
        return checked((uint)(value.Ticks / TimeSpan.TicksPerMillisecond));
    }
}
}
