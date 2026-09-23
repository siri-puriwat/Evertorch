using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks the server to target an entity and attack it repeatedly. The server validates the target and the command
///     sequence; the client only walks into range.
/// </summary>
public readonly struct AttackEntity
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long) + sizeof(uint);

    public AttackEntity(EntityId target, uint commandSequence)
    {
        Target = target;
        CommandSequence = commandSequence;
    }

    public EntityId Target { get; }

    public uint CommandSequence { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out AttackEntity message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.AttackEntity)
            || !reader.TryReadInt64(out long target)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd)
        {
            return false;
        }

        message = new AttackEntity(new EntityId(target), sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.AttackEntity);
        writer.WriteInt64(Target.Value);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
