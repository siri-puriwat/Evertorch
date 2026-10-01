using System;
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

    // Names are unique by their lower-case form (Persistence §4), so this is keyed the same way.
    private readonly Dictionary<string, CharacterSession> m_byName = new(StringComparer.OrdinalIgnoreCase);

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

    /// <summary>
    ///     The character named <paramref name="name" />, whatever its case, when it is reachable: in the world with a
    ///     connection, so one in its reconnect grace is not (Gameplay Systems §15).
    /// </summary>
    public bool TryGetReachable(string name, out CharacterSession? character)
    {
        character = m_byName.TryGetValue(name, out CharacterSession? named) && named.Connection != null ? named : null;
        return character != null;
    }

    public void AddCharacter(CharacterSession character)
    {
        m_characters.Add(character.Character, character);
        m_byName.Add(character.Player.Name, character);
    }

    public void RemoveCharacter(CharacterSession character)
    {
        if (m_characters.TryGetValue(character.Character, out CharacterSession? registered)
            && ReferenceEquals(registered, character))
        {
            m_characters.Remove(character.Character);
            m_byName.Remove(character.Player.Name);
        }
    }

    public void Remove(ClientSession session)
    {
        m_byConnection.Remove(session.Connection);
    }
}
}
