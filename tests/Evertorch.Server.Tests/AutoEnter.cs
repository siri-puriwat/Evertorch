using System;
using System.Linq;
using Evertorch.Client;
using Evertorch.Protocol;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Plays the character selection screen for a test client: once the first list arrives it enters the character
///     named <see cref="Name" />, creating it first when the account has none by that name.
/// </summary>
internal sealed class AutoEnter
{
    private readonly ClientConnection m_connection;
    private readonly Action? m_beforeCreate;
    private bool m_hasList;
    private bool m_isCreating;

    public AutoEnter(ClientConnection connection, string name, Action? beforeCreate = null)
    {
        m_connection = connection;
        m_beforeCreate = beforeCreate;
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
