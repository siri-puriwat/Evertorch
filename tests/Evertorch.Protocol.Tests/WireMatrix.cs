using System;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     The rejection checks every message must pass, written once. A reader is a function from bytes to "accepted".
/// </summary>
internal static class WireMatrix
{
    public static void AssertRejectsEveryTruncation(byte[] golden, Func<byte[], bool> tryRead)
    {
        for (int length = 0; length < golden.Length; length++)
        {
            byte[] truncated = new byte[length];
            Array.Copy(golden, truncated, length);
            Assert.That(tryRead(truncated), Is.False, $"accepted a payload truncated to {length} bytes");
        }
    }

    public static void AssertRejectsTrailingData(byte[] golden, Func<byte[], bool> tryRead)
    {
        byte[] extended = new byte[golden.Length + 1];
        Array.Copy(golden, extended, golden.Length);

        Assert.That(tryRead(extended), Is.False);
    }

    public static void AssertRejectsOtherOpcodes(byte[] golden, Func<byte[], bool> tryRead)
    {
        foreach (MessageOpcode opcode in (MessageOpcode[])Enum.GetValues(typeof(MessageOpcode)))
        {
            byte low = (byte)((ushort)opcode & 0xFF);
            byte high = (byte)((ushort)opcode >> 8);
            if (low == golden[0] && high == golden[1])
            {
                continue;
            }

            byte[] altered = (byte[])golden.Clone();
            altered[0] = low;
            altered[1] = high;
            Assert.That(tryRead(altered), Is.False, $"accepted opcode {opcode}");
        }
    }

    public static byte[] With(byte[] golden, int index, params byte[] replacement)
    {
        byte[] altered = (byte[])golden.Clone();
        Array.Copy(replacement, 0, altered, index, replacement.Length);
        return altered;
    }
}
}
