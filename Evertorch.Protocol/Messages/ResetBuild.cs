using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Asks the NPC that offers it for the reset of the character's build (Gameplay Systems §6.1): every stat and
///     skill point back, for free. An accepted one answers with <see cref="SkillList" /> and
///     <see cref="CharacterSheet" />.
/// </summary>
public readonly struct ResetBuild
{
    public const int EncodedLength = sizeof(ushort) + sizeof(long) + sizeof(uint);

    public ResetBuild(EntityId npc, uint commandSequence)
    {
        Npc = npc;
        CommandSequence = commandSequence;
    }

    public EntityId Npc { get; }

    public uint CommandSequence { get; }

    /// <summary>
    ///     False for a malformed message, including an NPC entity of 0 or below.
    /// </summary>
    public static bool TryRead(ReadOnlySpan<byte> source, out ResetBuild message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.ResetBuild)
            || !reader.TryReadInt64(out long npc)
            || !reader.TryReadUInt32(out uint sequence)
            || !reader.IsAtEnd
            || npc <= 0)
        {
            return false;
        }

        message = new ResetBuild(new EntityId(npc), sequence);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.ResetBuild);
        writer.WriteInt64(Npc.Value);
        writer.WriteUInt32(CommandSequence);
        return writer.Position;
    }
}
}
