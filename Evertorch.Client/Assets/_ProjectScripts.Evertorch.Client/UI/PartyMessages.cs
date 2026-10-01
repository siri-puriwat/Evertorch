using System;
using Evertorch.Game;
using Evertorch.Protocol;

namespace Evertorch.Client
{
/// <summary>
///     The words of the party (Prototype Content §2): what happened to it, as a grey line in the chat log, the invite's
///     question, and how a member away is shown.
/// </summary>
public static class PartyMessages
{
    public const string Offline = "Offline";

    /// <summary>
    ///     The line for <paramref name="kind" />, which names <paramref name="name" />, told to the player
    ///     <paramref name="ownName" />.
    /// </summary>
    public static string Describe(PartyEventKind kind, string name, string? ownName)
    {
        bool isSelf = string.Equals(name, ownName, StringComparison.OrdinalIgnoreCase);
        return kind switch
        {
            PartyEventKind.Invited => Invitation(name),
            PartyEventKind.Declined => $"{name} declined your invite.",
            PartyEventKind.Expired => $"{name} did not answer your invite.",
            PartyEventKind.Joined => isSelf ? "You joined the party." : $"{name} joined the party.",
            PartyEventKind.Left => isSelf ? "You left the party." : $"{name} left the party.",
            PartyEventKind.Kicked =>
                isSelf ? "You were removed from the party." : $"{name} was removed from the party.",
            PartyEventKind.LeaderChanged => isSelf ? "You now lead the party." : $"{name} now leads the party.",
            _ => "The party disbanded."
        };
    }

    /// <summary>
    ///     "Ann invites you to a party."
    /// </summary>
    public static string Invitation(string inviter)
    {
        return $"{inviter} invites you to a party.";
    }

    /// <summary>
    ///     A row of the party list: "Ann (L)  Lv 12 Vanguard".
    /// </summary>
    public static string Row(PartyMember member, ClientContent? content)
    {
        string leader = member.IsLeader ? " (L)" : string.Empty;
        return $"{member.Name}{leader}  Lv {member.BaseLevel} {BuildMessages.JobName(content, member.Job)}";
    }

    /// <summary>
    ///     Where a member is: "Offline", the map's name when it is elsewhere, or nothing beside the player.
    /// </summary>
    public static string Whereabouts(PartyMember member, MapDefinitionId? ownMap, ClientContent? content)
    {
        if (member.Map is not MapDefinitionId map)
        {
            return Offline;
        }

        if (map == ownMap)
        {
            return string.Empty;
        }

        return content != null && content.TryGetMap(map, out ClientMap? found) && found != null
            ? found.DisplayName
            : map.Value;
    }
}
}
