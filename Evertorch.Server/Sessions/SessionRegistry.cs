using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     Every live connection, and every character in the world with the connection that controls it, if any. Tick
///     thread only.
/// </summary>
public sealed class SessionRegistry
{
    private readonly Dictionary<ConnectionId, ClientSession> m_byConnection = new();

    private readonly Dictionary<CharacterId, CharacterSession> m_characters = new();

    public IReadOnlyCollection<ClientSession> Sessions => m_byConnection.Values;

    public IReadOnlyCollection<CharacterSession> Characters => m_characters.Values;

    public void Add(ClientSession session)
    {
        m_byConnection[session.Connection] = session;
    }

    public bool TryGet(ConnectionId connection, out ClientSession? session)
    {
        return m_byConnection.TryGetValue(connection, out session);
    }

    public bool TryGetCharacter(CharacterId character, out CharacterSession? session)
    {
        return m_characters.TryGetValue(character, out session);
    }

    /// <summary>
    ///     The connection controlling <paramref name="character" />, when one does.
    /// </summary>
    public bool TryGetByCharacter(CharacterId character, out ClientSession? session)
    {
        session = m_characters.TryGetValue(character, out CharacterSession? owned) ? owned?.Connection : null;
        return session != null;
    }

    public void AddCharacter(CharacterSession character)
    {
        m_characters.Add(character.Character, character);
    }

    public void RemoveCharacter(CharacterSession character)
    {
        if (m_characters.TryGetValue(character.Character, out CharacterSession? registered)
            && ReferenceEquals(registered, character))
        {
            m_characters.Remove(character.Character);
        }
    }

    public void Remove(ClientSession session)
    {
        m_byConnection.Remove(session.Connection);
    }
}
}
