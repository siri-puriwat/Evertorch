using System;
using System.Buffers.Binary;
using System.Text;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Reads little-endian fields from untrusted bytes. Every read checks the remaining length first and reports
///     failure instead of throwing; nothing is allocated before its size has been checked against a limit.
/// </summary>
public ref struct WireReader
{
    private ReadOnlySpan<byte> m_remaining;

    public WireReader(ReadOnlySpan<byte> source)
    {
        m_remaining = source;
    }

    public bool IsAtEnd => m_remaining.IsEmpty;

    public bool TryReadOpcode(MessageOpcode expected)
    {
        return TryReadUInt16(out ushort opcode) && opcode == (ushort)expected;
    }

    public bool TryReadByte(out byte value)
    {
        if (m_remaining.Length < sizeof(byte))
        {
            value = 0;
            return false;
        }

        value = m_remaining[0];
        m_remaining = m_remaining.Slice(sizeof(byte));
        return true;
    }

    public bool TryReadUInt16(out ushort value)
    {
        if (m_remaining.Length < sizeof(ushort))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt16LittleEndian(m_remaining);
        m_remaining = m_remaining.Slice(sizeof(ushort));
        return true;
    }

    public bool TryReadUInt32(out uint value)
    {
        if (m_remaining.Length < sizeof(uint))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadUInt32LittleEndian(m_remaining);
        m_remaining = m_remaining.Slice(sizeof(uint));
        return true;
    }

    public bool TryReadInt64(out long value)
    {
        if (m_remaining.Length < sizeof(long))
        {
            value = 0;
            return false;
        }

        value = BinaryPrimitives.ReadInt64LittleEndian(m_remaining);
        m_remaining = m_remaining.Slice(sizeof(long));
        return true;
    }

    /// <summary>
    ///     Fails on NaN and infinities: no message has a use for them and they poison arithmetic downstream.
    /// </summary>
    public bool TryReadSingle(out float value)
    {
        if (m_remaining.Length < sizeof(int))
        {
            value = 0f;
            return false;
        }

        // The target framework has no single-precision BinaryPrimitives overload.
        value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(m_remaining));
        m_remaining = m_remaining.Slice(sizeof(int));
        return !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    ///     Reads a 16-bit byte count followed by strict UTF-8. The count is checked against the limit and the
    ///     remaining input before anything is decoded.
    /// </summary>
    public bool TryReadString(int maxBytes, out string value)
    {
        value = string.Empty;
        if (!TryReadUInt16(out ushort length) || length > maxBytes || length > m_remaining.Length)
        {
            return false;
        }

        try
        {
            value = WireText.StrictUtf8.GetString(m_remaining.Slice(0, length));
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        m_remaining = m_remaining.Slice(length);
        return true;
    }

    public bool TryReadPosition(out WorldPosition position)
    {
        position = default;
        if (!TryReadSingle(out float x) || !TryReadSingle(out float y) || !TryReadSingle(out float z))
        {
            return false;
        }

        position = new WorldPosition(x, y, z);
        return true;
    }

    public bool TryReadDirection(out WorldDirection direction)
    {
        direction = default;
        if (!TryReadSingle(out float x) || !TryReadSingle(out float z))
        {
            return false;
        }

        direction = new WorldDirection(x, z);
        return true;
    }
}
}
