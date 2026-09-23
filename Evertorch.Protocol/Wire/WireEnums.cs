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
        return value >= EntityKind.Player && value <= EntityKind.ItemDrop;
    }

    public static bool IsDefined(CombatResult value)
    {
        return value >= CombatResult.Hit && value <= CombatResult.Critical;
    }

    public static bool IsDefined(DespawnReason value)
    {
        return value == DespawnReason.OutOfRange || value == DespawnReason.Removed;
    }

    public static bool IsDefined(CreateCharacterOutcome value)
    {
        return value >= CreateCharacterOutcome.Created && value <= CreateCharacterOutcome.ServiceUnavailable;
    }

    public static bool IsDefined(CommandRejectionReason value)
    {
        return value >= CommandRejectionReason.InvalidTarget && value <= CommandRejectionReason.Busy;
    }

    public static bool IsDefined(EntityStateFlags value)
    {
        return (value & ~KnownStateFlags) == 0;
    }
}
}
