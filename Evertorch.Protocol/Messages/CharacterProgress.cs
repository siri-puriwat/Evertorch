using System;

namespace Evertorch.Protocol
{
/// <summary>
///     The receiver's own character's base level and experience, sent to its owner alone after every award and
///     level-up (Network Protocol §9).
/// </summary>
public readonly struct CharacterProgress
{
    public const int EncodedLength = sizeof(ushort) + sizeof(ushort) + 2 * sizeof(ulong);

    public CharacterProgress(ushort level, ulong experience, ulong experienceToNextLevel)
    {
        Level = level;
        Experience = experience;
        ExperienceToNextLevel = experienceToNextLevel;
    }

    public ushort Level { get; }

    /// <summary>
    ///     Experience toward the next level.
    /// </summary>
    public ulong Experience { get; }

    /// <summary>
    ///     What the next level needs in all; 0 at the level cap.
    /// </summary>
    public ulong ExperienceToNextLevel { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out CharacterProgress message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.CharacterProgress)
            || !reader.TryReadUInt16(out ushort level)
            || !reader.TryReadUInt64(out ulong experience)
            || !reader.TryReadUInt64(out ulong experienceToNextLevel)
            || !reader.IsAtEnd
            || level == 0)
        {
            return false;
        }

        message = new CharacterProgress(level, experience, experienceToNextLevel);
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.CharacterProgress);
        writer.WriteUInt16(Level);
        writer.WriteUInt64(Experience);
        writer.WriteUInt64(ExperienceToNextLevel);
        return writer.Position;
    }
}
}
