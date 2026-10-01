namespace Evertorch.Game
{
/// <summary>
///     The chat text rule (Gameplay Systems §15; Network Protocol §6): 1 to 150 characters of printable ASCII, 0x20 to
///     0x7E, and not only spaces. Other scripts wait for a font that draws them.
/// </summary>
public static class ChatText
{
    public const int MaxLength = 150;

    public static bool IsValid(string? text)
    {
        if (text == null || text.Length == 0 || text.Length > MaxLength)
        {
            return false;
        }

        bool hasVisible = false;
        foreach (char character in text)
        {
            if (character < ' ' || character > '~')
            {
                return false;
            }

            hasVisible |= character != ' ';
        }

        return hasVisible;
    }
}
}
