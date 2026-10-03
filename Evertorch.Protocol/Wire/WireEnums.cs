namespace Evertorch.Protocol
{
/// <summary>
///     Range checks for enums read off the wire. A value outside the defined set rejects the whole message.
/// </summary>
internal static class WireEnums
{
    private const EntityStateFlags KnownStateFlags = EntityStateFlags.Moving | EntityStateFlags.Dead;

    public static bool IsDefined(DisconnectReason value)
    {
        return value >= DisconnectReason.ProtocolMismatch && value <= DisconnectReason.InternalError;
    }

    public static bool IsDefined(EntityKind value)
    {
        return value >= EntityKind.Player && value <= EntityKind.Npc;
    }

    public static bool IsDefined(CombatResult value)
    {
        return value >= CombatResult.Hit && value <= CombatResult.Critical;
    }

    public static bool IsDefined(DespawnReason value)
    {
        return value == DespawnReason.OutOfRange || value == DespawnReason.Removed || value == DespawnReason.PickedUp;
    }

    public static bool IsDefined(CreateCharacterOutcome value)
    {
        return value >= CreateCharacterOutcome.Created && value <= CreateCharacterOutcome.ServiceUnavailable;
    }

    public static bool IsDefined(CommandRejectionReason value)
    {
        return value >= CommandRejectionReason.InvalidTarget && value <= CommandRejectionReason.RequirementNotMet;
    }

    public static bool IsDefined(SkillOutcome value)
    {
        return value >= SkillOutcome.Hit && value <= SkillOutcome.Applied;
    }

    public static bool IsDefined(QuestState value)
    {
        return value == QuestState.Active || value == QuestState.Completed;
    }

    public static bool IsDefined(PrimaryStat value)
    {
        return value >= PrimaryStat.Str && value <= PrimaryStat.Luk;
    }

    public static bool IsDefined(PartyEventKind value)
    {
        return value >= PartyEventKind.Invited && value <= PartyEventKind.Disbanded;
    }

    public static bool IsDefined(BossAnnouncementKind value)
    {
        return value == BossAnnouncementKind.Appeared || value == BossAnnouncementKind.Fell;
    }

    public static bool IsDefined(PrizePlacement value)
    {
        return value <= PrizePlacement.Feet;
    }

    public static bool IsDefined(ChatChannel value)
    {
        return value >= ChatChannel.Nearby && value <= ChatChannel.WhisperSent;
    }

    /// <summary>
    ///     A channel a client may send on; <see cref="ChatChannel.WhisperSent" /> is the server's alone.
    /// </summary>
    public static bool IsSendable(ChatChannel value)
    {
        return value >= ChatChannel.Nearby && value <= ChatChannel.Whisper;
    }

    public static bool IsDefined(EntityStateFlags value)
    {
        return (value & ~KnownStateFlags) == 0;
    }
}
}
