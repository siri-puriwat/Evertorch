using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Every opcode has a golden-byte mirror, so no message reaches the wire without Unity's compiler and runtime
///     agreeing with .NET on its bytes. A mirror is a static <c>byte[]</c> field whose name ends in <c>Bytes</c>,
///     declared by a <c>Shared*Tests</c> fixture of this assembly; its first two bytes are the opcode.
/// </summary>
[TestFixture]
public sealed class SharedMirrorCoverageTests
{
    private const BindingFlags StaticFields = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    private static IEnumerable<FieldInfo> MirrorFields()
    {
        return typeof(SharedMirrorCoverageTests).Assembly.GetTypes()
            .Where(type => type.Name.StartsWith("Shared", StringComparison.Ordinal)
                && type.Name.EndsWith("Tests", StringComparison.Ordinal))
            .SelectMany(type => type.GetFields(StaticFields))
            .Where(field => field.FieldType == typeof(byte[])
                && field.Name.EndsWith("Bytes", StringComparison.Ordinal));
    }

    [Test]
    public void Mirrors_AcrossTheSharedFixtures_NameEveryOpcode()
    {
        var mirrored = new HashSet<MessageOpcode>();
        foreach (FieldInfo field in MirrorFields())
        {
            byte[] golden = (byte[])field.GetValue(null)!;
            Assert.That(golden.Length, Is.GreaterThanOrEqualTo(sizeof(ushort)), field.Name);
            mirrored.Add((MessageOpcode)BinaryPrimitives.ReadUInt16LittleEndian(golden));
        }

        MessageOpcode[] opcodes = ((MessageOpcode[])Enum.GetValues(typeof(MessageOpcode)))
            .Where(opcode => opcode != MessageOpcode.None)
            .ToArray();

        Assert.That(opcodes.Except(mirrored), Is.Empty, "opcodes without a mirror");
        Assert.That(mirrored.Except(opcodes), Is.Empty, "mirrors of no opcode");
    }
}
}
