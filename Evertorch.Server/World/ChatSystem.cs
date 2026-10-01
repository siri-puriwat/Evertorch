using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     Delivers chat (Gameplay Systems §15; Network Protocol §9): a line said nearby reaches the speaker and every
///     session on its map that knows the speaker; a line to the party reaches every reachable member, on any map; a
///     whisper reaches its recipient, and the speaker hears it back as sent. Nothing is stored and the text is never
///     logged; only a count by channel is kept. Tick thread only.
/// </summary>
public sealed class ChatSystem
{
    private readonly SessionRegistry m_sessions;
    private readonly MessageSender m_sender;
    private readonly ServerInstruments m_instruments;
    private readonly PartyRegistry m_parties;

    public ChatSystem(
        SessionRegistry sessions,
        MessageSender sender,
        ServerInstruments instruments,
        PartyRegistry parties)
    {
        m_sessions = sessions;
        m_parties = parties;
        m_sender = sender;
        m_instruments = instruments;
    }

    /// <summary>
    ///     Delivers <paramref name="text" /> from <paramref name="speaker" />, which is in the world and not logging
    ///     out. Refused with <see cref="CommandRejectionReason.InvalidTarget" /> for a whisper that finds no one, and
    ///     with <see cref="CommandRejectionReason.NotAllowedNow" /> for party chat without a party.
    /// </summary>
    public CommandRejectionReason TrySend(ClientSession speaker, ChatChannel channel, string recipient, string text)
    {
        switch (channel)
        {
            case ChatChannel.Nearby:
                SayNearby(speaker, text);
                return CommandRejectionReason.None;
            case ChatChannel.Whisper:
                return Whisper(speaker, recipient, text);
            case ChatChannel.Party:
                return SayToParty(speaker, text);
            default:
                return CommandRejectionReason.NotAllowedNow;
        }
    }

    private void SayNearby(ClientSession speaker, string text)
    {
        PlayerEntity player = speaker.Player!;
        MapInstance map = speaker.Map!;
        var line = new ChatReceived(ChatChannel.Nearby, player.Id, player.Name, text);
        foreach (ClientSession listener in m_sessions.Sessions)
        {
            if (listener.State == SessionState.InWorld
                && ReferenceEquals(listener.Map, map)
                && listener.Knows(player.Id))
            {
                m_sender.Send(listener.Connection, line);
            }
        }

        m_instruments.RecordChat(ChatChannel.Nearby);
    }

    // A member in its reconnect grace is not reachable and misses the line.
    private CommandRejectionReason SayToParty(ClientSession speaker, string text)
    {
        CharacterSession self = speaker.Character!;
        if (!m_parties.TryGetParty(self.Character, out ServerParty? party))
        {
            return CommandRejectionReason.NotAllowedNow;
        }

        var line = new ChatReceived(ChatChannel.Party, default, self.Player.Name, text);
        foreach (StoredPartyMember member in party!.Members)
        {
            if (m_sessions.TryGetCharacter(new CharacterId(member.CharacterId), out CharacterSession? listener)
                && listener!.Connection?.State == SessionState.InWorld)
            {
                m_sender.Send(listener.Connection.Connection, line);
            }
        }

        m_instruments.RecordChat(ChatChannel.Party);
        return CommandRejectionReason.None;
    }

    private CommandRejectionReason Whisper(ClientSession speaker, string recipient, string text)
    {
        CharacterSession self = speaker.Character!;
        if (!m_sessions.TryGetReachable(recipient, out CharacterSession? found)
            || ReferenceEquals(found, self))
        {
            return CommandRejectionReason.InvalidTarget;
        }

        m_sender.Send(
            found!.Connection!.Connection,
            new ChatReceived(ChatChannel.Whisper, default, self.Player.Name, text));
        m_sender.Send(speaker.Connection, new ChatReceived(ChatChannel.WhisperSent, default, found.Player.Name, text));
        m_instruments.RecordChat(ChatChannel.Whisper);
        return CommandRejectionReason.None;
    }
}
}
