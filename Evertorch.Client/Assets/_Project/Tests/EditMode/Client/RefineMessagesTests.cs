using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The Smith in the client (Prototype Content §2; Network Protocol §9): its tables kept until it despawns, a row of
///     its section, what a failure does, the outcome's words, and the refusals'.
/// </summary>
[TestFixture]
public sealed class RefineMessagesTests
{
    private static readonly EntityId Smith = new(500);
    private static readonly ItemDefinitionId Sword = new("item.weapon.iron_sword");
    private static readonly ItemDefinitionId Ember = new("item.material.emberstone");

    private static ClientContent Content()
    {
        var items = new Dictionary<ItemDefinitionId, ClientItem>
        {
            [Sword] = new(Sword, "Iron Sword", ItemType.Weapon, "pickup_iron_sword", "sword"),
            [Ember] = new(Ember, "Emberstone", ItemType.Material, "pickup_slime_gel", "gel")
        };
        return new ClientContent(
            "0000000000000000",
            new Dictionary<MapDefinitionId, ClientMap>(),
            new Dictionary<JobDefinitionId, ClientJob>(),
            new Dictionary<MonsterDefinitionId, ClientMonster>(),
            items,
            new Dictionary<SkillDefinitionId, ClientSkill>(),
            new Dictionary<StatusDefinitionId, ClientStatusEffect>());
    }

    private static RefineTable Table(RefineFailure failure = RefineFailure.Destroy, byte lowered = 0)
    {
        var steps = new List<RefineTableStep>();
        for (int level = 1; level <= 10; level++)
        {
            steps.Add(new RefineTableStep(level <= 6 ? (byte)100 : (byte)(100 - (level - 6) * 20), 100));
        }

        return new RefineTable(Smith, 1, Ember, 1, failure, lowered, steps, new[] { Sword });
    }

    private static EntitySpawn SmithSpawn()
    {
        return new EntitySpawn(
            Smith,
            EntityKind.Npc,
            "npc.smith",
            new WorldPosition(2.5f, 0f, 2.5f),
            new WorldDirection(0f, 1f),
            EntityStateFlags.None,
            0);
    }

    [Test]
    public void TheRefusals_SayWhatIsMissing()
    {
        ClientContent content = Content();
        var request = new RefineRequest(Smith, 5, Sword, 6);
        var top = new RefineRequest(Smith, 5, Sword, 10);

        Assert.That(
            new[]
            {
                RefineMessages.DescribeRefusal(request, Table(), CommandRejectionReason.RequirementNotMet, content),
                RefineMessages.DescribeRefusal(request, Table(), CommandRejectionReason.NotEnoughCoins, content),
                RefineMessages.DescribeRefusal(request, Table(), CommandRejectionReason.NotAllowedNow, content),
                RefineMessages.DescribeRefusal(top, Table(), CommandRejectionReason.NotAllowedNow, content)
            },
            Is.EqualTo(
                new[]
                {
                    "You need 1 Emberstone.", "Not enough coins.", "That can't be refined.", "It is already +10."
                }));
    }

    [Test]
    public void TheWords_NameTheRow_TheRisk_AndTheOutcome()
    {
        ClientContent content = Content();
        var row = new InventoryEntry(5, Sword, 1, EquipmentSlot.None, 6);

        Assert.That(
            RefineMessages.Row(content, row, Table(), 3),
            Is.EqualTo("+6 Iron Sword → +7 · 80 % · 100 coins · Emberstone 1 (3 held)"));
        Assert.That(
            new[]
            {
                RefineMessages.OnFailure(Table()),
                RefineMessages.OnFailure(Table(RefineFailure.Downgrade, 1)),
                RefineMessages.OnFailure(Table(RefineFailure.Downgrade, 2)),
                RefineMessages.OnFailure(Table(RefineFailure.Keep))
            },
            Is.EqualTo(
                new[]
                {
                    "On failure it is destroyed.", "On failure it falls 1 level.", "On failure it falls 2 levels.",
                    "On failure nothing changes."
                }));
        Assert.That(RefineMessages.Prompt(Table()), Is.EqualTo("Press again to refine. On failure it is destroyed."));
        Assert.That(
            RefineMessages.Prompt(Table(), true),
            Is.EqualTo("Press again. On failure it is destroyed; its cards come back to your bag."),
            "a carded row's cards come back");
        Assert.That(
            new[]
            {
                RefineMessages.Describe(new RefineOutcome(5, Sword, 6, 7, RefineOutcomeKind.Refined), content),
                RefineMessages.Describe(new RefineOutcome(5, Sword, 6, 0, RefineOutcomeKind.Destroyed), content),
                RefineMessages.Describe(new RefineOutcome(5, Sword, 6, 5, RefineOutcomeKind.Lowered), content),
                RefineMessages.Describe(new RefineOutcome(5, Sword, 6, 6, RefineOutcomeKind.Unchanged), content)
            },
            Is.EqualTo(
                new[]
                {
                    "Refined Iron Sword to +7.", "The refine failed: +6 Iron Sword was destroyed.",
                    "The refine failed: +6 Iron Sword fell to +5.", "The refine failed: +6 Iron Sword is unchanged."
                }));
        Assert.That(
            RefineMessages.Describe(new RefineOutcome(5, Sword, 6, 0, RefineOutcomeKind.Destroyed), content, true),
            Is.EqualTo("The refine failed: +6 Iron Sword was destroyed. Its cards are back in your bag."),
            "a carded row's cards came back");
    }

    [Test]
    public void TheWorld_KeepsASeenSmithsCardFees_UntilItDespawns_AndIgnoresAnUnknownOnes()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), ClientTestGrids.Center(1, 1));
        var fees = new CardRemovalFees(
            Smith,
            new[] { new CardRemovalFee(new ItemDefinitionId("item.card.forest_crawler"), 200) });
        world.OnCardRemovalFees(fees);
        int unknown = world.UnknownEntityEvents;
        world.OnSpawn(SmithSpawn());
        int version = world.RefineTablesVersion;
        world.OnCardRemovalFees(fees);
        uint kept = world.CardRemovalFeesOf(Smith)[0].Fee;
        bool isNewVersion = world.RefineTablesVersion > version;
        world.OnDespawn(new EntityDespawn(Smith, DespawnReason.OutOfRange));

        Assert.That(unknown, Is.EqualTo(1), "fees of an entity not seen");
        Assert.That((kept, isNewVersion), Is.EqualTo((200u, true)));
        Assert.That(world.CardRemovalFeesOf(Smith), Is.Empty, "gone with the Smith");
    }

    [Test]
    public void TheWorld_KeepsASeenSmithsTables_UntilItDespawns_AndIgnoresAnUnknownOnes()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), ClientTestGrids.Center(1, 1));
        world.OnRefineTable(Table());
        int unknown = world.UnknownEntityEvents;
        world.OnSpawn(SmithSpawn());
        world.OnRefineTable(Table());
        world.OnRefineTable(Table());
        RefineTable? kept = world.RefineTableFor(Smith, Sword);
        world.OnDespawn(new EntityDespawn(Smith, DespawnReason.OutOfRange));

        Assert.That(unknown, Is.EqualTo(1), "a table of an entity not seen");
        Assert.That(kept, Is.Not.Null);
        Assert.That(kept!.Index, Is.EqualTo((byte)1));
        Assert.That(world.RefineTablesOf(Smith), Is.Empty, "gone with the Smith");
    }
}
}
