using System;
using System.Linq;
using System.Reflection;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     A client asks; it never reports an outcome. No client-to-server message can carry damage, HP, a hit result,
///     or attack timing, so client animation has no way to decide them (Milestone 3 verification).
/// </summary>
[TestFixture]
public sealed class ClientAuthorityTests
{
    private static readonly string[] ForbiddenWords =
    {
        "Damage", "Amount", "Health", "Hp", "Result", "Critical", "Miss", "Timing", "Interval", "Windup", "Impact",
        "Recovery", "Dead", "Died", "Revived"
    };

    private static readonly Type[] ForbiddenTypes = { typeof(CombatResult), typeof(AttackTiming) };

    private static MessageOpcode[] ClientOpcodes()
    {
        return ((MessageOpcode[])Enum.GetValues(typeof(MessageOpcode)))
            .Where(MessageRouting.IsClientToServer)
            .ToArray();
    }

    private static Type MessageType(MessageOpcode opcode)
    {
        Type? type = typeof(MessageOpcode).Assembly.GetType($"Evertorch.Protocol.{opcode}");
        Assert.That(type, Is.Not.Null, $"no message type named after {opcode}");
        return type!;
    }

    [Test]
    public void ClientMessages_AreTheOnesReviewedForAuthority()
    {
        Assert.That(
            ClientOpcodes().Select(opcode => opcode.ToString()),
            Is.EqualTo(
                new[]
                {
                    "ClientHello", "EnterWorldRequest", "MoveInput", "StopMovement", "TargetEntity",
                    "AttackEntity", "CancelAction", "UseSkill", "PickupItem", "Respawn", "CreateCharacter", "Logout",
                    "InventoryResyncRequest", "EquipItem", "UnequipItem", "UseItem", "BuyItem", "SellItem",
                    "AcceptQuest", "CompleteQuest", "AllocateStat"
                }),
            "a new client message must be checked against the rules below before it joins this list");
    }

    [Test]
    public void ClientMessages_CarryNoDamageHealthResultOrTiming()
    {
        string[] offenders = ClientOpcodes()
            .Select(MessageType)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => ForbiddenTypes.Contains(property.PropertyType)
                || ForbiddenWords.Any(word => property.Name.Contains(word, StringComparison.Ordinal)))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
            .ToArray();

        Assert.That(offenders, Is.Empty);
    }

    [Test]
    public void TheCheck_WouldCatchAnOutcomeOnAServerMessage()
    {
        string[] found = typeof(Damage).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => ForbiddenTypes.Contains(property.PropertyType)
                || ForbiddenWords.Any(word => property.Name.Contains(word, StringComparison.Ordinal)))
            .Select(property => property.Name)
            .ToArray();

        Assert.That(found, Is.SupersetOf(new[] { "Result", "Amount", "TargetHealthPermille" }));
    }
}
}
