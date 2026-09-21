using System;
using System.Buffers.Binary;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
/// Client request to select an entity as the current target. The server validates the target before accepting it.
/// </summary>
public readonly struct TargetEntity
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long);

    public TargetEntity(EntityId target)
    {
        Target = target;
    }

    public EntityId Target { get; }

    /// <summary>
    /// Reads a complete payload. Truncated input, trailing bytes, and any other opcode are rejected.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out TargetEntity message)
    {
        message = default;
        if (source.Length != EncodedLength)
        {
            return false;
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(source) != (ushort)MessageOpcode.TargetEntity)
        {
            return false;
        }

        long target = BinaryPrimitives.ReadInt64LittleEndian(source.Slice(sizeof(ushort)));
        message = new TargetEntity(new EntityId(target));
        return true;
    }

    public void Write(Span<byte> destination)
    {
        if (destination.Length < EncodedLength)
        {
            throw new ArgumentException("Destination is smaller than the encoded message.", nameof(destination));
        }

        BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)MessageOpcode.TargetEntity);
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(sizeof(ushort)), Target.Value);
    }
}
}
