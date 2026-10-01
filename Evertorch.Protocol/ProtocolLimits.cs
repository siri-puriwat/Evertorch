using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     Upper bounds a receiver enforces before it allocates or decodes anything.
/// </summary>
public static class ProtocolLimits
{
    public const int MaxBuildVersionBytes = 32;
    public const int MaxSessionTokenBytes = 512;
    public const int MaxDefinitionIdBytes = DefinitionIdLimits.MaxLength;
    public const int MaxNoticeMessageBytes = 128;

    /// <summary>
    ///     A character name: at most 23 characters, all ASCII under <see cref="CharacterNames" />.
    /// </summary>
    public const int MaxCharacterNameBytes = CharacterNames.MaxLength;

    /// <summary>
    ///     The largest payload a client may send: a <see cref="ClientHello" /> with both strings at their limits.
    /// </summary>
    public const int MaxClientPayloadBytes = 556;
}
}
