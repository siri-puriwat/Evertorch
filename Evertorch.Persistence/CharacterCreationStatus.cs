namespace Evertorch.Persistence
{
public enum CharacterCreationStatus
{
    Created = 0,

    /// <summary>
    ///     Another character, of any account, has the name in some letter case.
    /// </summary>
    NameTaken = 1,

    /// <summary>
    ///     The account already holds the most characters it may.
    /// </summary>
    LimitReached = 2
}
}
