using System;
using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     The answer to <see cref="CreateCharacter" />, followed by a new <see cref="CharacterList" />. The character ID
///     is set exactly when the outcome is <see cref="CreateCharacterOutcome.Created" />.
/// </summary>
public readonly struct CreateCharacterResult
{
    public const int EncodedLength = sizeof(ushort) + sizeof(byte) + sizeof(long);

    public CreateCharacterResult(CreateCharacterOutcome outcome, CharacterId character)
    {
        Outcome = outcome;
        Character = character;
    }

    public CreateCharacterOutcome Outcome { get; }

    public CharacterId Character { get; }

    public static bool TryRead(ReadOnlySpan<byte> source, out CreateCharacterResult message)
    {
        message = default;
        var reader = new WireReader(source);
        if (!reader.TryReadOpcode(MessageOpcode.CreateCharacterResult)
            || !reader.TryReadByte(out byte outcomeValue)
            || !reader.TryReadInt64(out long character)
            || !reader.IsAtEnd)
        {
            return false;
        }

        var outcome = (CreateCharacterOutcome)outcomeValue;
        bool isCreated = outcome == CreateCharacterOutcome.Created;
        if (!WireEnums.IsDefined(outcome) || (isCreated ? character <= 0 : character != 0))
        {
            return false;
        }

        message = new CreateCharacterResult(outcome, new CharacterId(character));
        return true;
    }

    public int Write(Span<byte> destination)
    {
        var writer = new WireWriter(destination);
        writer.WriteOpcode(MessageOpcode.CreateCharacterResult);
        writer.WriteByte((byte)Outcome);
        writer.WriteInt64(Character.Value);
        return writer.Position;
    }
}
}
