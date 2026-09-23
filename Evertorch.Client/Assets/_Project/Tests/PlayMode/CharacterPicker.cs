using System;
using Evertorch.Protocol;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     Plays the character selection screen for a live test: once the first list arrives it enters the character
///     named <see cref="Name" />, creating it first when the account has none by that name.
/// </summary>
internal sealed class CharacterPicker
{
    private ClientConnection? m_connection;
    private bool m_hasList;
    private bool m_isCreating;

    public CharacterPicker(string name)
    {
        Name = name;
    }

    public string Name { get; }

    /// <summary>
    ///     Call every frame; it follows the connection it is given, so a reconnect with a new one starts over.
    /// </summary>
    public void Poll(ClientConnection? connection)
    {
        if (connection == null)
        {
            return;
        }

        if (!ReferenceEquals(connection, m_connection))
        {
            m_connection = connection;
            m_hasList = false;
            m_isCreating = false;
            connection.CharactersChanged += () => m_hasList = true;
        }

        if (connection.State != ClientConnectionState.SelectingCharacter || !m_hasList)
        {
            return;
        }

        foreach (CharacterListEntry entry in connection.Characters)
        {
            if (string.Equals(entry.Name, Name, StringComparison.OrdinalIgnoreCase))
            {
                connection.EnterWorld(entry.Character);
                return;
            }
        }

        if (!m_isCreating)
        {
            m_isCreating = true;
            connection.CreateCharacter(Name);
        }
    }
}
}
