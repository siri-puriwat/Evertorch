using System.Collections.Generic;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Limits on what one peer, one address, or one session may send (Network Protocol §11). Generous by default: an
///     honest client never meets them, and many phones may share one address behind carrier NAT.
/// </summary>
public sealed class AbuseOptions
{
    public const string SectionName = "Abuse";

    /// <summary>
    ///     Switches every limit off, for the tests of the guarantees that hold without them.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    ///     Messages each peer may send per tick, on average. At least 3, so an honest client stays inside it at any
    ///     tick rate.
    /// </summary>
    public int PeerMessagesPerTick { get; set; } = 5;

    /// <summary>
    ///     Messages a peer may send at once before the per-tick budget applies.
    /// </summary>
    public int PeerMessageBurst { get; set; } = 200;

    public int ConnectionRequestsPerSecond { get; set; } = 20;

    public int MaxConnectionsPerAddress { get; set; } = 64;

    /// <summary>
    ///     Bound of the table of remote addresses the connection limits remember.
    /// </summary>
    public int MaxTrackedAddresses { get; set; } = 10000;

    /// <summary>
    ///     <c>AttackEntity</c>, <c>TargetEntity</c>, and <c>Respawn</c> per second, per connection.
    /// </summary>
    public int CombatCommandsPerSecond { get; set; } = 20;

    public int CombatCommandBurst { get; set; } = 40;

    public int PickupCommandsPerSecond { get; set; } = 10;

    public int PickupCommandBurst { get; set; } = 20;

    /// <summary>
    ///     Commands of the item class per second, per connection: <c>EquipItem</c>, <c>UnequipItem</c>, <c>UseItem</c>,
    ///     <c>BuyItem</c>, <c>SellItem</c>, <c>AcceptQuest</c>, <c>CompleteQuest</c>, <c>AllocateStat</c>,
    ///     <c>LearnSkill</c>, <c>ResetBuild</c>, and <c>ChangeJob</c>.
    /// </summary>
    public int ItemCommandsPerSecond { get; set; } = 10;

    public int ItemCommandBurst { get; set; } = 20;

    /// <summary>
    ///     <c>CreateCharacter</c>, <c>EnterWorldRequest</c>, and <c>Logout</c> per second, per connection.
    /// </summary>
    public int SessionCommandsPerSecond { get; set; } = 2;

    public int SessionCommandBurst { get; set; } = 5;

    public int ResyncRequestsPerSecond { get; set; } = 1;

    public int ResyncRequestBurst { get; set; } = 3;

    /// <summary>
    ///     <c>ChatSend</c> per second, per connection: a typing pace (Network Protocol §11). The client keeps the same
    ///     bucket, so an honest player is never scored for it.
    /// </summary>
    public int ChatCommandsPerSecond { get; set; } = 1;

    public int ChatCommandBurst { get; set; } = 5;

    /// <summary>
    ///     The party's five commands per second, per connection (Network Protocol §11).
    /// </summary>
    public int PartyCommandsPerSecond { get; set; } = 2;

    public int PartyCommandBurst { get; set; } = 5;

    /// <summary>
    ///     A trade's requests and replies per second, per connection; its offers, lock, confirm, and cancel take the
    ///     item bucket (Network Protocol §11).
    /// </summary>
    public int TradeCommandsPerSecond { get; set; } = 2;

    public int TradeCommandBurst { get; set; } = 5;

    /// <summary>
    ///     Reads of an account's storage per second, per connection: a read costs the database a query, a commit does not
    ///     cost more (Network Protocol §11).
    /// </summary>
    public int ReadCommandsPerSecond { get; set; } = 1;

    public int ReadCommandBurst { get; set; } = 3;

    /// <summary>
    ///     Violation score at which a connection is closed; each violation adds <see cref="ViolationScore.Points" />.
    /// </summary>
    public int ViolationThreshold { get; set; } = 100;

    /// <summary>
    ///     Score forgiven per second, spread over the ticks.
    /// </summary>
    public int ViolationDecayPerSecond { get; set; } = 5;

    /// <summary>
    ///     How long an account, or an address that had not signed in, is refused after a disconnect for violations.
    /// </summary>
    public int KickCooldownMs { get; set; } = 60000;

    /// <summary>
    ///     Sign-ins per second from one remote address, at the gateway.
    /// </summary>
    public int SignInsPerSecond { get; set; } = 2;

    public int SignInBurst { get; set; } = 20;

    /// <summary>
    ///     Failed sign-ins forgiven per minute for one login, whether or not an account has it.
    /// </summary>
    public int SignInFailuresPerMinute { get; set; } = 2;

    /// <summary>
    ///     Failed sign-ins a login may have before it is answered 429.
    /// </summary>
    public int SignInFailureBurst { get; set; } = 5;

    /// <summary>
    ///     Bound of the table of logins the failure limit remembers.
    /// </summary>
    public int MaxTrackedLogins { get; set; } = 10000;
}

public sealed class AbuseOptionsValidator : IValidateOptions<AbuseOptions>
{
    public ValidateOptionsResult Validate(string? name, AbuseOptions options)
    {
        var failures = new List<string>();
        AddRangeFailure(failures, "PeerMessagesPerTick", options.PeerMessagesPerTick, 3, 1000);
        AddRangeFailure(failures, "PeerMessageBurst", options.PeerMessageBurst, 1, 100000);
        AddRangeFailure(failures, "ConnectionRequestsPerSecond", options.ConnectionRequestsPerSecond, 1, 10000);
        AddRangeFailure(failures, "MaxConnectionsPerAddress", options.MaxConnectionsPerAddress, 1, 10000);
        AddRangeFailure(failures, "MaxTrackedAddresses", options.MaxTrackedAddresses, 16, 1000000);
        AddRangeFailure(failures, "CombatCommandsPerSecond", options.CombatCommandsPerSecond, 1, 1000);
        AddRangeFailure(failures, "CombatCommandBurst", options.CombatCommandBurst, 1, 10000);
        AddRangeFailure(failures, "PickupCommandsPerSecond", options.PickupCommandsPerSecond, 1, 1000);
        AddRangeFailure(failures, "PickupCommandBurst", options.PickupCommandBurst, 1, 10000);
        AddRangeFailure(failures, "ItemCommandsPerSecond", options.ItemCommandsPerSecond, 1, 1000);
        AddRangeFailure(failures, "ItemCommandBurst", options.ItemCommandBurst, 1, 10000);
        AddRangeFailure(failures, "SessionCommandsPerSecond", options.SessionCommandsPerSecond, 1, 1000);
        AddRangeFailure(failures, "SessionCommandBurst", options.SessionCommandBurst, 1, 10000);
        AddRangeFailure(failures, "ResyncRequestsPerSecond", options.ResyncRequestsPerSecond, 1, 1000);
        AddRangeFailure(failures, "ResyncRequestBurst", options.ResyncRequestBurst, 1, 10000);
        AddRangeFailure(failures, "ChatCommandsPerSecond", options.ChatCommandsPerSecond, 1, 1000);
        AddRangeFailure(failures, "ChatCommandBurst", options.ChatCommandBurst, 1, 10000);
        AddRangeFailure(failures, "PartyCommandsPerSecond", options.PartyCommandsPerSecond, 1, 1000);
        AddRangeFailure(failures, "PartyCommandBurst", options.PartyCommandBurst, 1, 10000);
        AddRangeFailure(failures, "TradeCommandsPerSecond", options.TradeCommandsPerSecond, 1, 1000);
        AddRangeFailure(failures, "TradeCommandBurst", options.TradeCommandBurst, 1, 10000);
        AddRangeFailure(failures, "ReadCommandsPerSecond", options.ReadCommandsPerSecond, 1, 1000);
        AddRangeFailure(failures, "ReadCommandBurst", options.ReadCommandBurst, 1, 10000);
        AddRangeFailure(failures, "ViolationThreshold", options.ViolationThreshold, 1, 1000000);
        AddRangeFailure(failures, "ViolationDecayPerSecond", options.ViolationDecayPerSecond, 0, 1000000);
        AddRangeFailure(failures, "KickCooldownMs", options.KickCooldownMs, 0, 86400000);
        AddRangeFailure(failures, "SignInsPerSecond", options.SignInsPerSecond, 1, 1000);
        AddRangeFailure(failures, "SignInBurst", options.SignInBurst, 1, 10000);
        AddRangeFailure(failures, "SignInFailuresPerMinute", options.SignInFailuresPerMinute, 1, 1000);
        AddRangeFailure(failures, "SignInFailureBurst", options.SignInFailureBurst, 1, 1000);
        AddRangeFailure(failures, "MaxTrackedLogins", options.MaxTrackedLogins, 16, 1000000);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void AddRangeFailure(List<string> failures, string key, int value, int minimum, int maximum)
    {
        if (value < minimum || value > maximum)
        {
            failures.Add($"{AbuseOptions.SectionName}:{key} must be between {minimum} and {maximum}.");
        }
    }
}
}
