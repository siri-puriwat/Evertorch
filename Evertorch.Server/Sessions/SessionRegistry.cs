using System.Collections.Generic;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     Every live session, by connection and, once in the world, by character. Tick thread only.
/// </summary>
public sealed class SessionRegistry
{
    private readonly Dictionary<ConnectionId, ClientSession> m_byConnection = new();

    private readonly Dictionary<CharacterId, ClientSession> m_byCharacter = new();

    public IReadOnlyCollection<ClientSession> Sessions => m_byConnection.Values;

    public void Add(ClientSession session)
    {
        m_byConnection[session.Connection] = session;
    }

    public bool TryGet(ConnectionId connection, out ClientSession? session)
    {
        return m_byConnection.TryGetValue(connection, out session);
    }

    public bool TryGetByCharacter(CharacterId character, out ClientSession? session)
    {
        return m_byCharacter.TryGetValue(character, out session);
    }

    public void BindCharacter(ClientSession session, CharacterId character)
    {
        m_byCharacter[character] = session;
    }

    public void Remove(ClientSession session)
    {
        m_byConnection.Remove(session.Connection);
        if (session.Player != null
            && m_byCharacter.TryGetValue(session.Player.Character, out ClientSession? bound)
            && ReferenceEquals(bound, session))
        {
            m_byCharacter.Remove(session.Player.Character);
        }
    }
}
}
