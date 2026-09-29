using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Evertorch.Game;
using NUnit.Framework;

namespace Evertorch.Protocol.Tests
{
/// <summary>
///     Only the values sent on purpose travel (Content Pipeline §5; Milestone 6 verification): every server-to-client
///     message, and every kind of value inside it, has exactly the fields reviewed here, so a new field fails until it is
///     reviewed. No field may name a damage ratio, an AI field, an experience reward, or another server-only value of
///     the content. Consumable effects rest on the review alone, because the status effect list rightly names its
///     effects.
/// </summary>
[TestFixture]
public sealed class ServerOutputTests
{
    // Each sent on purpose (Network Protocol §9), including the attack's timing, a skill's range, SP cost, cooldown,
    // and after-cast delay, its cast time, and the experience to the next level. A monster's health travels only as
    // a permille, and a heal as its nominal amount.
    private static readonly Dictionary<string, string[]> Reviewed = new()
    {
        ["AttackStarted"] = new[] { "Attacker", "StartTick", "Target", "Timing" },
        ["AttackTiming"] = new[] { "Impact", "Interval", "Recovery", "Windup" },
        ["CharacterHealth"] = new[] { "Current", "CurrentSpirit", "Maximum", "MaximumSpirit" },
        ["CharacterId"] = new[] { "Value" },
        ["CharacterList"] = new[] { "Characters" },
        ["CharacterListEntry"] = new[] { "BaseLevel", "Character", "Job", "Name" },
        ["CharacterProgress"] = new[] { "Experience", "ExperienceToNextLevel", "Level" },
        ["CharacterSheet"] = new[]
        {
            "Attack", "AttackSpeed", "Critical", "Defense", "Flee", "Hit", "Job", "JobExperience",
            "JobExperienceToNextLevel", "JobLevel", "MagicAttack", "MagicDefense", "SkillPoints", "StatPoints", "Stats"
        },
        ["CharacterSheetStat"] = new[] { "NextCost", "Value" },
        ["CommandRejected"] = new[] { "CommandSequence", "Reason" },
        ["CreateCharacterResult"] = new[] { "Character", "Outcome" },
        ["Damage"] = new[] { "Amount", "Result", "ServerTick", "Source", "Target", "TargetHealthPermille" },
        ["DisconnectNotice"] = new[] { "Message", "Reason" },
        ["EntityDespawn"] = new[] { "Entity", "Reason" },
        ["EntityDied"] = new[] { "Entity", "ServerTick", "Source" },
        ["EntityId"] = new[] { "Value" },
        ["EntityRevived"] = new[] { "Entity", "Facing", "Position", "ServerTick" },
        ["EntitySnapshot"] = new[] { "Entities", "LastProcessedInputSequence", "ServerTick" },
        ["EntitySpawn"] = new[]
            { "DefinitionId", "Entity", "Facing", "HealthPermille", "Kind", "Position", "StateFlags" },
        ["EntityState"] = new[] { "Entity", "Facing", "Position", "StateFlags", "VelocityX", "VelocityY", "VelocityZ" },
        ["InventoryChanged"] = new[] { "Changes", "Coins", "NewRevision", "PriorRevision" },
        ["InventoryEntry"] = new[] { "InventoryItem", "Item", "Quantity", "Slot" },
        ["InventorySnapshot"] = new[] { "Coins", "Entries", "IsLast", "Part", "PartCount", "Revision" },
        ["ItemDefinitionId"] = new[] { "Value" },
        ["ItemDropped"] = new[] { "Amount", "Entity", "ItemId", "Position" },
        ["ItemPickedUp"] = new[] { "Amount", "Drop", "Item", "Recipient" },
        ["JobDefinitionId"] = new[] { "Value" },
        ["LogoutComplete"] = Array.Empty<string>(),
        ["MapDefinitionId"] = new[] { "Value" },
        ["MonsterDefinitionId"] = new[] { "Value" },
        ["NpcJobChangeOffer"] = new[] { "FromJob", "Job", "Level" },
        ["NpcQuestOffer"] = new[] { "BaseExperience", "Coins", "Count", "JobExperience", "Monster", "Quest" },
        ["NpcServiceEntry"] = new[] { "BuyPrice", "Item", "SellPrice" },
        ["NpcServices"] = new[] { "Entries", "JobChanges", "Npc", "Offers", "OffersReset" },
        ["QuestDefinitionId"] = new[] { "Value" },
        ["QuestLog"] = new[] { "Entries" },
        ["QuestLogEntry"] = new[] { "Count", "Progress", "Quest", "State" },
        ["ServerHello"] = new[]
        {
            "ProtocolVersion", "RequiredClientContentVersion", "ServerBuildVersion", "ServerTickRate",
            "ServerTimeUnixMilliseconds"
        },
        ["SkillCastStarted"] = new[] { "CastMs", "Caster", "Skill", "StartTick", "Target" },
        ["SkillDefinitionId"] = new[] { "Value" },
        ["SkillList"] = new[] { "Skills" },
        ["SkillListEntry"] = new[]
        {
            "AfterCastDelayMs", "CooldownMs", "IsLearned", "Level", "MaxLevel", "PrerequisiteIndex",
            "PrerequisiteLevel", "Range", "RemainingCooldownMs", "Skill", "SpCost"
        },
        ["SkillResolved"] = new[]
        {
            "Amount", "Caster", "Outcome", "ServerTick", "Skill", "Target", "TargetHealthPermille"
        },
        ["StatusDefinitionId"] = new[] { "Value" },
        ["StatusEffectEntry"] = new[] { "RemainingMs", "Status" },
        ["StatusEffects"] = new[] { "Effects" },
        ["TargetChanged"] = new[] { "Actor", "Target" },
        ["WorldDirection"] = new[] { "X", "Z" },
        ["WorldEntered"] = new[]
        {
            "AttackRange", "Character", "CurrentHealth", "CurrentSpirit", "Experience", "ExperienceToNextLevel",
            "Facing", "Job", "LastCommandSequence", "Level", "LocalEntity", "Map", "MapEpoch", "MapInstance",
            "MaximumHealth", "MaximumSpirit", "MovementSpeed", "Position", "ServerTick"
        },
        ["WorldPosition"] = new[] { "X", "Y", "Z" }
    };

    // An NPC's prices and a quest's terms travel on purpose, because the player must see them (Content Pipeline §5),
    // and so do the derived statistics of the owner's Stats window (Network Protocol §9): exactly these fields may
    // carry a server-only word.
    private static readonly string[] SentOnPurpose =
    {
        "NpcServiceEntry.BuyPrice", "NpcServiceEntry.SellPrice", "NpcQuestOffer.BaseExperience",
        "CharacterSheet.Defense", "CharacterSheet.Flee", "CharacterSheet.MagicAttack", "CharacterSheet.MagicDefense"
    };

    // Words of the content's server-only fields (Content Pipeline §5).
    private static readonly string[] ServerOnlyWords =
    {
        "Hp", "PhysicalAttack", "Defense", "Flee", "MagicAttack", "Behavior", "Perception", "Leash", "Roam", "Idle",
        "Scan", "KeepDistance", "BaseExperience", "Drops", "Weight", "Price", "Ratio", "Percent", "Penalty", "Bonus",
        "Chance", "Seed", "Threshold", "Burst"
    };

    private static readonly Assembly[] WireAssemblies = { typeof(MessageOpcode).Assembly, typeof(EntityId).Assembly };

    private static Type MessageType(MessageOpcode opcode)
    {
        Type? type = typeof(MessageOpcode).Assembly.GetType($"Evertorch.Protocol.{opcode}");
        Assert.That(type, Is.Not.Null, $"no message type named after {opcode}");
        return type!;
    }

    private static PropertyInfo[] FieldsOf(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
    }

    private static Type Carried(Type type)
    {
        return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>)
            ? type.GetGenericArguments()[0]
            : type;
    }

    private static bool IsComposite(Type type)
    {
        return !type.IsPrimitive && !type.IsEnum && type != typeof(string) && WireAssemblies.Contains(type.Assembly);
    }

    private static bool IsServerOnlyName(string name)
    {
        return ServerOnlyWords.Any(word => name.Contains(word, StringComparison.Ordinal));
    }

    // Each server-to-client message (0x8000 and up) and every kind of value inside it, down to numbers, enums, and
    // strings, with the names of its fields.
    private static Dictionary<string, string[]> Sent()
    {
        var sent = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var pending = new Stack<Type>(
            ((MessageOpcode[])Enum.GetValues(typeof(MessageOpcode)))
            .Where(opcode => (ushort)opcode >= 0x8000)
            .Select(MessageType));
        while (pending.Count > 0)
        {
            Type type = pending.Pop();
            if (sent.ContainsKey(type.Name))
            {
                continue;
            }

            PropertyInfo[] fields = FieldsOf(type);
            sent.Add(type.Name,
                fields.Select(field => field.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
            foreach (Type carried in fields.Select(field => Carried(field.PropertyType)).Where(IsComposite))
            {
                pending.Push(carried);
            }
        }

        return sent;
    }

    [Test]
    public void ServerMessages_CarryExactlyTheReviewedFields()
    {
        Dictionary<string, string[]> sent = Sent();

        Assert.That(
            sent.Keys.OrderBy(name => name, StringComparer.Ordinal),
            Is.EqualTo(Reviewed.Keys.OrderBy(name => name, StringComparer.Ordinal)),
            "a new message or kind of value on the wire must be reviewed and listed");
        string[] unreviewed = sent
            .Where(type => !type.Value.SequenceEqual(Reviewed[type.Key]))
            .Select(type => $"{type.Key}: {string.Join(", ", type.Value)}")
            .ToArray();
        Assert.That(unreviewed, Is.Empty, "each field on the wire must be reviewed and listed");
    }

    [Test]
    public void ServerMessages_NameNoServerOnlyValueOfTheContent()
    {
        string[] named = Sent()
            .SelectMany(type => type.Value.Where(IsServerOnlyName).Select(field => $"{type.Key}.{field}"))
            .ToArray();

        Assert.That(named.Except(SentOnPurpose), Is.Empty);
        Assert.That(named, Is.SupersetOf(SentOnPurpose), "each field allowed on purpose is still sent");
    }

    [Test]
    public void TheNameCheck_WouldCatchTheServerOnlyFieldsOfTheContent()
    {
        string[] caught = new[]
            {
                typeof(MonsterDefinition), typeof(MonsterSkill), typeof(ItemDefinition), typeof(ItemEquipment),
                typeof(SkillEffect)
            }
            .SelectMany(FieldsOf)
            .Select(field => field.Name)
            .Where(IsServerOnlyName)
            .ToArray();

        Assert.That(
            caught,
            Is.SupersetOf(
                new[]
                {
                    "Hp", "PhysicalAttack", "PhysicalDefense", "Flee", "MagicAttack", "Behavior", "PerceptionRadius",
                    "LeashRadius", "RoamRadius", "IdlePauseMinMs", "IdlePauseMaxMs", "ScanIntervalMs", "KeepDistance",
                    "BaseExperience", "Drops", "Chance", "Weight", "SellPrice", "AttackSpeedPenalty", "Defense",
                    "Bonus", "DamageRatioPercent", "HealHp"
                }));
    }
}
}
