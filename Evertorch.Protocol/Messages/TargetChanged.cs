using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The server accepted or cleared an actor's target. Sent to the actor's owner only; a refused request gets no
///     reply. Target 0 means the actor has none.
/// </summary>
public readonly struct TargetChanged
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long) + sizeof(long);

    public TargetChanged(EntityId actor, EntityId target)
    {
        Actor = actor;
        Target = target;
    }

    public EntityId Actor { get; }

    public EntityId Target { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out TargetChanged message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.TargetChanged)
            || !reader.TryReadInt64(out long actor)
            || !reader.TryReadInt64(out long target)
            || !reader.IsAtEnd
            || actor == 0)
        {
            return false;
        }

        message = new TargetChanged(new EntityId(actor), new EntityId(target));
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.TargetChanged);
        writer.WriteInt64(Actor.Value);
        writer.WriteInt64(Target.Value);
        return writer.Position;
    }
}
}
