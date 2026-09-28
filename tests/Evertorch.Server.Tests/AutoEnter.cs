using System;
using System.Linq;
using Evertorch.Client;
using Evertorch.Protocol;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Plays the character selection screen for a test client: once the first list arrives it enters the character
///     named <see cref="Name" />, creating it first when the account has none by that name. For a character it created,
///     it runs <c>afterCreate</c> once before entering, so a test can store what a new character has not earned.
/// </summary>
internal sealed class AutoEnter
{
    private readonly ClientConnection m_connection;
    private readonly Action? m_beforeCreate;
    private readonly Action? m_afterCreate;
    private bool m_hasList;
    private bool m_isCreating;
    private bool m_hasEntered;

    public AutoEnter(ClientConnection connection, string name, Action? beforeCreate = null, Action? afterCreate = null)
    {
        m_connection = connection;
        m_beforeCreate = beforeCreate;
        m_afterCreate = afterCreate;
        Name = name;
        connection.CharactersChanged += () => m_hasList = true;
    }

    public string Name { get; }

    public void Poll()
    {
        if (m_connection.State != ClientConnectionState.SelectingCharacter || !m_hasList)
        {
            return;
        }

        foreach (CharacterListEntry entry in m_connection.Characters.Where(entry =>
                     string.Equals(entry.Name, Name, StringComparison.OrdinalIgnoreCase)))
        {
            if (m_isCreating && !m_hasEntered)
            {
                m_afterCreate?.Invoke();
            }

            m_hasEntered = true;
            m_connection.EnterWorld(entry.Character);
            return;
        }

        if (!m_isCreating)
        {
            m_isCreating = true;
            m_beforeCreate?.Invoke();
            m_connection.CreateCharacter(Name);
        }
    }
}
}
