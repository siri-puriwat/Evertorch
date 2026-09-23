namespace Evertorch.Protocol
{
/// <summary>
///     The answer to <see cref="CreateCharacter" />. Zero is never sent.
/// </summary>
public enum CreateCharacterOutcome : byte
{
    None = 0,
    Created = 1,

    /// <summary>
    ///     The name breaks the naming policy: 4 to 23 characters of A–Z, a–z, and 0–9.
    /// </summary>
    NameInvalid = 2,

    /// <summary>
    ///     Another character has the name, in any letter case.
    /// </summary>
    NameTaken = 3,

    /// <summary>
    ///     The account already holds the most characters it may.
    /// </summary>
    LimitReached = 4,

    /// <summary>
    ///     The database is unavailable; trying again later may work.
    /// </summary>
    ServiceUnavailable = 5
}
}
