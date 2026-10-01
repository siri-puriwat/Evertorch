namespace Evertorch.Protocol
{
/// <summary>
///     Where a chat line goes (Gameplay Systems §15). A client sends <see cref="Nearby" />, <see cref="Party" />, or
///     <see cref="Whisper" />; the server also tells a speaker its own whisper as <see cref="WhisperSent" />. Zero is
///     never sent.
/// </summary>
public enum ChatChannel : byte
{
    None = 0,
    Nearby = 1,
    Party = 2,
    Whisper = 3,
    WhisperSent = 4
}
}
