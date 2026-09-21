using System;
using System.Buffers.Binary;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
/// Writes little-endian fields into a caller-owned buffer. Running out of room or exceeding a string limit is a
/// programming error on the sending side, so it throws.
/// </summary>
public ref struct WireWriter
{
    private readonly Span<byte> m_destination;
    private int m_position;

    public WireWriter(Span<byte> destination)
    {
        m_destination = destination;
        m_position = 0;
    }

    public int Position => m_position;

    public void WriteOpcode(MessageOpcode opcode)
    {
        WriteUInt16((ushort)opcode);
    }

    public void WriteByte(byte value)
    {
        Reserve(sizeof(byte))[0] = value;
    }

    public void WriteUInt16(ushort value)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(Reserve(sizeof(ushort)), value);
    }

    public void WriteUInt32(uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(Reserve(sizeof(uint)), value);
    }

    public void WriteInt64(long value)
    {
        BinaryPrimitives.WriteInt64LittleEndian(Reserve(sizeof(long)), value);
    }

    public void WriteSingle(float value)
    {
        // The target framework has no single-precision BinaryPrimitives overload.
        BinaryPrimitives.WriteInt32LittleEndian(Reserve(sizeof(int)), BitConverter.SingleToInt32Bits(value));
    }

    public void WriteString(string value, int maxBytes)
    {
        int length = WireText.GetByteCount(value, maxBytes);
        WriteUInt16((ushort)length);
        WireText.StrictUtf8.GetBytes(value.AsSpan(), Reserve(length));
    }

    public void WritePosition(WorldPosition position)
    {
        WriteSingle(position.X);
        WriteSingle(position.Y);
        WriteSingle(position.Z);
    }

    public void WriteDirection(WorldDirection direction)
    {
        WriteSingle(direction.X);
        WriteSingle(direction.Z);
    }

    private Span<byte> Reserve(int length)
    {
        if (m_destination.Length - m_position < length)
        {
            throw new ArgumentException("Destination is smaller than the encoded message.");
        }

        Span<byte> slice = m_destination.Slice(m_position, length);
        m_position += length;
        return slice;
    }
}
}
