using System;
using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Only the values sent on purpose travel (Content Pipeline §5, Network Protocol §9; Milestone 6 verification):
///     an actor strikes and kills a slime, picks up its drop, casts First Aid and Focus, equips a sword, drinks a
///     potion, has a command refused, and crosses to the field, while an observer beside it at full HP and SP does
///     nothing. The observer hears what anyone near sees, and nothing of the actor's own target, health, progress,
///     skills, status effects, inventory, refusals, or new map. Milestone 7 adds the town: the actor's purchases and
///     sales,
///     quest, and reward reach the observer as nothing more. Milestone 9 adds the build: the actor's raises, learned
///     levels, and reset reach the observer as nothing at all. Milestone 10 adds the first jobs: another's change
///     reaches the observer as its new body alone, and a Mend on the observer as the heal and its own health.
///     Milestone 12 adds the party: another's party, its health, and its lines reach an observer outside it as nothing.
///     Milestone 13 adds the dungeon: another's numbing and award reach an observer as nothing, a slam on another as its
///     result, and a boss's fall everyone on its map. Milestone 14 adds the trade and storage: another's trade, its
///     settled bags, and its deposits and withdrawals reach an observer beside it as nothing. Milestone 15 adds the
///     market board: another's reads, listings, purchases, sales, and collections reach an observer beside it as nothing.
///     Milestone 18 adds the elements: another's converters, the sheet's attack element and the status list they change,
///     reach an observer beside it and a member of its party as nothing. Milestone 20 adds the eight positions: another's
///     whole outfit, its swaps, its refused burst, and a job change that takes its weapon off reach an observer as its
///     appearance alone, and a newcomer, a returning observer, and the map it crosses to learn the whole outfit.
///     Milestone 21 adds the quarry: its boss's burst and fall reach an observer as what anyone near sees, the fall its
///     map alone, and the award its most valuable player alone; the Smith's fees reach only whoever newly sees it.
/// </summary>
[TestFixture]
public sealed class ObserverOutputTests
{
    private const long Actor = 1;
    private const long Observer = 2;
    private const string Strike = "skill.strike";
    private const string FirstAid = "skill.first_aid";
    private const string Focus = "skill.focus";
    private const string Sword = "item.weapon.training_sword";
    private const string Potion = "item.consumable.minor_health";
    private const string Gel = "item.material.slime_gel";
    private const string Dust = "item.material.gloom_dust";
    private const string Quartermaster = "npc.quartermaster";
    private const string GateWarden = "npc.gate_warden";
    private const string Hunt = "quest.crawler_hunt";
    private const string Guildmaster = "npc.guildmaster";
    private const string NumbingSpark = "skill.numbing_spark";
    private const string QuakeSlam = "skill.quake_slam";
    private const string Staff = "item.weapon.training_staff";
    private const string Cloth = "item.armor.cloth";
    private const string Leather = "item.armor.leather";
    private const string Cap = "item.headgear.trail_cap";
    private const string Buckler = "item.shield.wooden_buckler";
    private const string Cape = "item.garment.travel_cape";
    private const string Boots = "item.footwear.trail_boots";
    private const string TravelerCharm = "item.accessory.traveler_charm";
    private const string GrottoCharm = "item.accessory.grotto_charm";
    private const string FurnaceBurst = "skill.furnace_burst";
    private const string Smith = "npc.smith";

    // A piece for each of the eight positions, in position order.
    private static readonly string[] Outfit = { Staff, Cloth, Cap, Buckler, Cape, Boots, TravelerCharm, GrottoCharm };

    private static readonly MessageOpcode[] WhatAnyoneNearSees =
    {
        MessageOpcode.EntitySpawn, MessageOpcode.EntityDespawn, MessageOpcode.EntitySnapshot,
        MessageOpcode.AttackStarted, MessageOpcode.Damage, MessageOpcode.EntityDied, MessageOpcode.EntityRevived,
        MessageOpcode.SkillCastStarted, MessageOpcode.SkillResolved, MessageOpcode.ItemDropped,
        MessageOpcode.ItemPickedUp, MessageOpcode.EquipmentAppearance
    };

    private static MapInstance GroundOf(TestServer server)
    {
        server.World.TryGetMap(new MapDefinitionId("map.training_ground"), out MapInstance? ground);
        return ground!;
    }

    private static MapInstance GrottoOf(TestServer server)
    {
        server.World.TryGetMap(new MapDefinitionId("map.umbral_grotto"), out MapInstance? grotto);
        return grotto!;
    }

    private static MapInstance QuarryOf(TestServer server)
    {
        server.World.TryGetMap(new MapDefinitionId("map.ashen_quarry"), out MapInstance? quarry);
        return quarry!;
    }

    private static void StandBeside(TestServer server, ConnectionId player, MonsterEntity monster)
    {
        NavigationGrid grid = GroundOf(server).Definition.Navigation;
        for (int step = 0; step < 8; step++)
        {
            double angle = step * Math.PI / 4.0;
            float x = monster.Position.X + 1.2f * (float)Math.Cos(angle);
            float z = monster.Position.Z + 1.2f * (float)Math.Sin(angle);
            if (grid.CanOccupy(x, z)
                && grid.TrySampleHeight(x, z, out float height)
                && grid.HasLineOfSight(new WorldPosition(x, height, z), monster.Position))
            {
                server.PlayerOf(player).Position = new WorldPosition(x, height, z);
                return;
            }
        }

        throw new InvalidOperationException("No place to stand beside the monster.");
    }

    private static void TickUntil(TestServer server, Func<bool> condition, string what)
    {
        for (int tick = 0; tick < 200 && !condition(); tick++)
        {
            server.Tick();
        }

        Assert.That(condition(), Is.True, what);
    }

    private static void Cast(TestServer server, ConnectionId actor, string skill, EntityId target, uint sequence)
    {
        PlayerEntity player = server.PlayerOf(actor);
        player.CurrentSpirit = player.MaxSpirit;
        server.SendUseSkill(actor, skill, target, sequence);
        server.Tick();
        TickUntil(server, () => !player.Combat.IsCasting, $"{skill} resolved");
        server.Tick(20);
    }

    private static void Settle(TestServer server, ConnectionId actor)
    {
        server.Tick();
        TickUntil(server, () => server.SessionOf(actor).Character!.Operation == null, "the operation settled");
    }

    private static long RowOf(TestServer server, ConnectionId actor, string item)
    {
        return server.SessionOf(actor).Character!.Inventory.Rows.First(row => row.Item.Value == item).InventoryItem;
    }

    private static IEnumerable<T> Read<T>(TestServer server, ConnectionId connection, MessageOpcode opcode,
        Func<byte[], T?> read)
        where T : class
    {
        return server.Transport.SentTo(connection)
            .Where(message => message.Opcode == opcode)
            .Select(message => read(message.Payload)!);
    }

    // The character holds a row of each item, as earlier pickups would have left them, and enters the world.
    private static ConnectionId EnterHolding(TestServer server, long character, params string[] items)
    {
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, character);
        foreach (string item in items)
        {
            server.Store.GiveItems(character, item, 1, 1, 5);
        }

        server.SendEnterWorld(player, character);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        return player;
    }

    private static void WearOutfit(TestServer server, ConnectionId actor)
    {
        for (int index = 0; index < Outfit.Length; index++)
        {
            server.SendEquip(actor, RowOf(server, actor, Outfit[index]), (uint)(index + 1),
                EquipmentPositions.All[index]);
            Settle(server, actor);
        }
    }

    private static string[] WornBy(TestServer server, ConnectionId actor)
    {
        InventoryEntry[] rows = server.SessionOf(actor).Character!.Inventory.Rows.ToArray();
        return EquipmentPositions.All
            .Select(position => rows.FirstOrDefault(row => row.Slot == position).Item.Value)
            .ToArray();
    }

    private static EquipmentAppearance[] AppearancesOf(TestServer server, ConnectionId connection, EntityId entity)
    {
        return Read(server, connection, MessageOpcode.EquipmentAppearance, payload =>
                EquipmentAppearance.TryRead(payload, out EquipmentAppearance? read) ? read : null)
            .Where(appearance => appearance.Entity == entity)
            .ToArray();
    }

    private static string[] ItemsOf(EquipmentAppearance appearance)
    {
        return EquipmentPositions.All.Select(position => appearance.ItemIn(position).Value).ToArray();
    }

    private static ConnectionId EnterBesideTheBroker(
        TestServer server,
        long character,
        long coins,
        Action<InMemoryGameStore>? prepare)
    {
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, character);
        prepare?.Invoke(server.Store);
        server.Store.Edit(character, coins: coins);
        server.SendEnterWorld(player, character);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        NpcEntity broker = server.NpcOf("npc.broker");
        server.Place(player, broker.Position.X - 2f, broker.Position.Z + character - 2);
        server.Tick(2);
        return player;
    }

    // The Smith's fees (Milestone 21 line 5): whoever newly sees the Smith learns its seventeen fees in their two
    // parts; the actor, who already sees it, hears none of them again, nor the Smith's services or tables.
    [Test]
    public void ANewcomer_LearnsTheSmithsFeesInTheirParts_AndWhoeverSeesTheSmithAlreadyHearsNoneAgain()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId actor = server.EnterWorld(Actor);
        server.Tick(5);
        EntityId smith = server.NpcOf(Smith).Id;
        server.Transport.ClearSent();

        ConnectionId observer = server.EnterWorld(Observer);
        server.Tick(5);

        Assert.That(
            Read(server, observer, MessageOpcode.CardRemovalFees, payload =>
                    CardRemovalFees.TryRead(payload, out CardRemovalFees? read) ? read : null)
                .Select(part => (part.Npc, part.Part, part.PartCount, part.Entries.Count)),
            Is.EqualTo(new[] { (smith, (byte)0, (byte)2, 14), (smith, (byte)1, (byte)2, 3) }),
            "the newcomer learns the fees, once");
        Assert.That(
            server.Transport.SentTo(actor).Select(message => message.Opcode),
            Has.No.Member(MessageOpcode.CardRemovalFees)
                .And.No.Member(MessageOpcode.NpcServices)
                .And.No.Member(MessageOpcode.RefineTable),
            "the actor already knows them");
    }

    // A whole outfit (Milestone 20, session task T12) reaches a newcomer with the actor's spawn, an observer returning
    // after a disconnect, and a player on the field when the actor crosses to it; the ground, left behind, hears the
    // actor go (Network Protocol §6, §9).
    [Test]
    public void AWholeOutfit_ReachesANewcomer_AReturningObserver_AndTheMapItCrossesTo()
    {
        var server = new TestServer(withEveryMap: true);
        ConnectionId actor = EnterHolding(server, Actor, Outfit);
        WearOutfit(server, actor);
        EntityId entity = server.PlayerOf(actor).Id;

        ConnectionId newcomer = server.EnterWorld(Observer);
        server.Tick(2);
        string[] newcomerSaw = ItemsOf(AppearancesOf(server, newcomer, entity).Last());
        server.Disconnect(newcomer);
        server.Tick(2);
        ConnectionId returning = server.EnterWorld(Observer);
        server.Tick(2);
        string[] returningSaw = ItemsOf(AppearancesOf(server, returning, entity).Last());

        ConnectionId fielder = server.EnterWorld(3);
        MapInstance ground = GroundOf(server);
        MapPortal toTheField = ground.Definition.Portals.Single(portal =>
            portal.DestinationMap == new MapDefinitionId("map.training_field"));
        server.PlayerOf(fielder).Position = toTheField.Center;
        TickUntil(
            server,
            () => server.SessionOf(fielder).Character!.Map.Definition.Id != ground.Definition.Id,
            "the fielder crossed");
        server.Tick(20);
        server.Transport.ClearSent();
        server.PlayerOf(actor).Position = toTheField.Center;
        TickUntil(
            server,
            () => server.SessionOf(actor).Character!.Map.Definition.Id != ground.Definition.Id,
            "the actor crossed");
        server.Tick(2);

        Assert.That(newcomerSaw, Is.EqualTo(Outfit), "the newcomer");
        Assert.That(returningSaw, Is.EqualTo(Outfit), "the returning observer");
        Assert.That(
            server.Transport.SentTo(fielder)
                .Where(message => message.Opcode is MessageOpcode.EntitySpawn or MessageOpcode.EquipmentAppearance)
                .Select(message => message.Opcode)
                .Take(2),
            Is.EqualTo(new[] { MessageOpcode.EntitySpawn, MessageOpcode.EquipmentAppearance }),
            "the field hears the actor's spawn, then its appearance");
        Assert.That(ItemsOf(AppearancesOf(server, fielder, entity).Last()), Is.EqualTo(Outfit), "the field");
        Assert.That(
            server.Transport.SentTo(returning).Select(message => message.Opcode),
            Has.Member(MessageOpcode.EntityDespawn),
            "the ground saw the actor go");
    }

    // The build (Milestone 9 verification): the actor raises AGI and VIT, learns Strike, is refused a raise past its
    // points, and is reset at the Guildmaster, while the observer stands beside it. The actor hears its sheet, its skill
    // list, and its new maximums; the observer hears none of them, nor anything at all of the build.
    [Test]
    public void AnotherPlayersBuild_ReachesAnObserverAsNothing()
    {
        var server = new TestServer(withNpcs: true, withAdventurerBuild: false);
        ConnectionId actor = server.EnterWorld(Actor);
        ConnectionId observer = server.EnterWorld(Observer);
        server.PlayerOf(actor).Level = 5;
        server.PlayerOf(actor).JobLevel = 2;
        NpcEntity guildmaster = server.NpcOf(Guildmaster);
        server.Place(actor, guildmaster.Position.X + 2f, guildmaster.Position.Z);
        server.Place(observer, guildmaster.Position.X + 2.5f, guildmaster.Position.Z);
        server.Tick(2);
        server.Transport.ClearSent();

        server.SendAllocateStat(actor, PrimaryStat.Agi, 2, 1);
        server.SendAllocateStat(actor, PrimaryStat.Vit, 2, 2);
        server.SendLearnSkill(actor, Strike, 3);
        server.SendAllocateStat(actor, PrimaryStat.Dex, 50, 4);
        server.Tick();
        server.SendResetBuild(actor, guildmaster.Id, 5);
        server.Tick(2);

        MessageOpcode[] actorHeard = server.Transport.SentTo(actor).Select(message => message.Opcode).ToArray();
        MessageOpcode[] observerHeard = server.Transport.SentTo(observer).Select(message => message.Opcode).ToArray();
        Assert.That(
            actorHeard,
            Is.SupersetOf(
                new[]
                {
                    MessageOpcode.CharacterSheet, MessageOpcode.SkillList, MessageOpcode.CharacterHealth,
                    MessageOpcode.CommandRejected
                }),
            "the actor was told of its build");
        Assert.That(actorHeard.Count(opcode => opcode == MessageOpcode.CharacterSheet), Is.GreaterThanOrEqualTo(2));
        Assert.That(server.PlayerOf(actor).Skills, Is.Empty, "the reset went through");
        Assert.That(observerHeard.Distinct(), Is.SubsetOf(WhatAnyoneNearSees));
        Assert.That(
            observerHeard,
            Has.None.EqualTo(MessageOpcode.CharacterSheet).And.None.EqualTo(MessageOpcode.SkillList),
            "never another's sheet or skills");
    }

    // The elements (Milestone 18 verification): the actor, wearing a sword and partied with a third player, uses a Fire
    // Converter, then a Wind one, then takes the sword off, while the observer stands beside it outside the party. The
    // actor hears its sheet and its status list each time; the member and the observer hear neither: the observer what
    // anyone near sees, the member that and the party.
    [Test]
    public void AnotherPlayersConverters_ReachAnObserverAndAPartyMemberAsNothingNew()
    {
        const string fire = "item.consumable.fire_converter";
        const string wind = "item.consumable.wind_converter";
        var rig = new PartyRig();
        TestServer server = rig.Server;
        ConnectionId actor = server.Connect();
        server.SignInWithCharacter(actor, Actor);
        server.Store.GiveItems(Actor, fire, 1, 1, 5);
        server.Store.GiveItems(Actor, wind, 1, 1, 5);
        server.Store.GiveItems(Actor, Sword, 1, 1, 5);
        server.Store.Wear(Actor, server.Store.Stored(Actor).Items.Last().Id, "Weapon");
        server.SendEnterWorld(actor, Actor);
        server.TickUntil(() => server.SessionOf(actor).State == SessionState.InWorld);
        ConnectionId observer = rig.Enter(Observer);
        ConnectionId member = rig.Enter(3);
        rig.Join(actor, "Tester1", member, "Tester3");
        server.Place(observer, server.PlayerOf(actor).Position.X + 1f, server.PlayerOf(actor).Position.Z);
        server.Place(member, server.PlayerOf(actor).Position.X - 1f, server.PlayerOf(actor).Position.Z);
        server.Tick(2);
        server.Transport.ClearSent();

        server.SendUseItem(actor, RowOf(server, actor, fire), rig.Next(actor));
        Settle(server, actor);
        server.SendUseItem(actor, RowOf(server, actor, wind), rig.Next(actor));
        Settle(server, actor);
        server.SendUnequip(actor, EquipmentSlot.Weapon, rig.Next(actor));
        Settle(server, actor);

        MessageOpcode[] actorHeard = server.Transport.SentTo(actor).Select(message => message.Opcode).ToArray();
        MessageOpcode[] observerHeard = server.Transport.SentTo(observer).Select(message => message.Opcode).ToArray();
        MessageOpcode[] memberHeard = server.Transport.SentTo(member).Select(message => message.Opcode).ToArray();
        MessageOpcode[] party =
            { MessageOpcode.PartyEvent, MessageOpcode.PartyRoster, MessageOpcode.PartyMemberStatus };
        Assert.That(
            Read(server, actor, MessageOpcode.CharacterSheet, payload =>
                    CharacterSheet.TryRead(payload, out CharacterSheet? sheet) ? sheet : null)
                .Select(sheet => sheet.AttackElement),
            Is.EqualTo(new[] { Element.Fire, Element.Wind, Element.Neutral }),
            "the actor's sheet told each change");
        Assert.That(
            actorHeard.Count(opcode => opcode == MessageOpcode.StatusEffects),
            Is.EqualTo(3),
            "and its status list");
        Assert.That(observerHeard.Distinct(), Is.SubsetOf(WhatAnyoneNearSees));
        Assert.That(memberHeard.Distinct(), Is.SubsetOf(WhatAnyoneNearSees.Concat(party)));
        Assert.That(
            observerHeard.Concat(memberHeard),
            Has.None.EqualTo(MessageOpcode.CharacterSheet).And.None.EqualTo(MessageOpcode.StatusEffects),
            "never another's sheet or statuses");
    }

    // The party (Milestone 12 verification): the actor and a third player form a party, the actor is wounded, speaks to
    // the party, passes the lead, and leaves, while the observer stands beside it outside the party. The observer
    // hears none of the party's messages, no party line, and nothing of the actor's health.
    // The dungeon (Milestone 13 verification): a Gloom Wisp numbs the actor, the Slime Monarch slams it, and the boss
    // falls to the actor, its most valuable player with HP to spare, while the observer stands near the actor
    // throughout, a third player waits by the grotto's west gate, out of their sight, and a fourth stands in town. The
    // observer hears what anyone near sees, the slam's result on the actor among it, and the boss's fall, and nothing
    // of the actor's numbing or award; the player by the gate hears the fall and nothing of the slam; the one in town,
    // nothing of the boss.
    [Test]
    public void AnotherPlayersDungeon_ReachesAnObserverAsWhatAnyoneNearSees_AndItsMapAsTheBossesFall()
    {
        var server = new TestServer(
            withEveryMap: true,
            withGrotto: true,
            withMonsters: true,
            withMonsterAi: false,
            dropRandom: new ScriptedRandom(0));
        ConnectionId actor = server.EnterWorld(Actor);
        ConnectionId observer = server.EnterWorld(Observer);
        ConnectionId gate = server.EnterWorld(3);
        ConnectionId town = server.EnterWorld(4);
        foreach (ConnectionId player in new[] { actor, observer, gate })
        {
            server.CrossIntoTheGrotto(player);
        }

        MapInstance grotto = GrottoOf(server);
        PlayerEntity acting = server.PlayerOf(actor);
        MonsterEntity wisp = grotto.Monsters.First(monster => monster.Definition.Id.Value == "monster.gloom_wisp");
        MonsterEntity monarch = grotto.Monsters.Single(monster => monster.Definition.IsBoss);
        wisp.Position = new WorldPosition(16f, 0f, -19.5f);
        acting.Position = new WorldPosition(12f, 0f, -19.5f);
        server.PlayerOf(observer).Position = new WorldPosition(12f, 0f, -17.5f);
        server.Tick(2);
        server.Transport.ClearSent();

        server.Combat.BeginMonsterCast(grotto, wisp, new SkillDefinitionId(NumbingSpark), acting, server.CurrentTick);
        server.Tick(TestServer.TickRate);
        acting.Position = new WorldPosition(15.5f, 0f, 18f);
        acting.CurrentHealth = 1_000_000;
        server.PlayerOf(observer).Position = new WorldPosition(11f, 0f, 18f);
        server.Tick(2);
        server.Combat.BeginMonsterCast(grotto, monarch, new SkillDefinitionId(QuakeSlam), acting, server.CurrentTick);
        server.Tick(2 * TestServer.TickRate);
        monarch.LogMvpDealt(acting.Character, 500);
        server.Combat.Kill(grotto, monarch, acting, server.CurrentTick);
        TickUntil(server, () => !server.SessionOf(actor).Character!.HasInventoryWork, "the prize settled");

        MessageOpcode[] observerHeard = server.Transport.SentTo(observer).Select(message => message.Opcode).ToArray();
        MessageOpcode[] gateHeard = server.Transport.SentTo(gate).Select(message => message.Opcode).ToArray();
        SkillResolved[] slamsSeenByTheObserver = Read(server, observer, MessageOpcode.SkillResolved, payload =>
                SkillResolved.TryRead(payload, out SkillResolved? read) ? read : null)
            .Where(resolved => resolved.Skill.Value == QuakeSlam)
            .ToArray();
        Assert.That(
            server.Transport.SentTo(actor).Select(message => message.Opcode),
            Is.SupersetOf(
                new[]
                {
                    MessageOpcode.StatusEffects, MessageOpcode.BossAnnouncement, MessageOpcode.MvpAwarded,
                    MessageOpcode.InventoryChanged
                }),
            "the actor was told of its numbing, the fall, and its award");
        Assert.That(
            observerHeard.Distinct(),
            Is.SubsetOf(WhatAnyoneNearSees.Append(MessageOpcode.BossAnnouncement)));
        Assert.That(observerHeard, Does.Contain(MessageOpcode.BossAnnouncement), "the observer heard the fall");
        Assert.That(
            slamsSeenByTheObserver.Select(resolved => resolved.Target),
            Is.EqualTo(new[] { acting.Id }),
            "the slam's result on the actor, whom the observer knows");
        Assert.That(gateHeard, Does.Contain(MessageOpcode.BossAnnouncement), "the player by the gate heard the fall");
        Assert.That(gateHeard, Has.No.Member(MessageOpcode.SkillResolved), "but nothing of the slam far off");
        Assert.That(
            server.Transport.SentTo(town).Select(message => message.Opcode),
            Has.No.Member(MessageOpcode.BossAnnouncement).And.No.Member(MessageOpcode.SkillResolved),
            "nothing of the boss in town");
    }

    // The eight positions (Milestone 20, session task T12): the actor, an Adventurer ready to change job, wears a piece
    // in each position, swaps its armor once in a burst of four equips, the other three refused while the first is in
    // flight, then ten times more one after another, and becomes a Vanguard at the Guildmaster, which takes its staff
    // off. The observer beside it hears an appearance for each committed change, in rising revisions, and nothing of
    // the actor's rows, levels, cards, or refusals; the last names what the actor wears.
    [Test]
    public void AnotherPlayersEightPiecesSwapsAndJobChange_ReachAnObserverAsItsAppearancesAlone()
    {
        var server = new TestServer(withNpcs: true);
        server.Store.SeedOnCreate = BuildSeed.ReadyToChange;
        ConnectionId actor = EnterHolding(server, Actor, Outfit.Append(Leather).ToArray());
        ConnectionId observer = server.EnterWorld(Observer);
        NpcEntity guildmaster = server.NpcOf(Guildmaster);
        server.Place(actor, guildmaster.Position.X + 2f, guildmaster.Position.Z);
        server.Place(observer, guildmaster.Position.X + 3f, guildmaster.Position.Z);
        server.Tick(2);
        server.Transport.ClearSent();
        EntityId entity = server.PlayerOf(actor).Id;

        WearOutfit(server, actor);
        long cloth = RowOf(server, actor, Cloth);
        long leather = RowOf(server, actor, Leather);
        for (uint burst = 0; burst < 4; burst++)
        {
            server.SendEquip(actor, burst % 2 == 0 ? leather : cloth, 20 + burst, EquipmentSlot.Armor);
        }

        Settle(server, actor);
        for (int swap = 0; swap < 10; swap++)
        {
            server.SendEquip(actor, swap % 2 == 0 ? cloth : leather, (uint)(30 + swap), EquipmentSlot.Armor);
            Settle(server, actor);
        }

        server.SendChangeJob(actor, guildmaster.Id, "job.vanguard", 50);
        Settle(server, actor);
        server.Tick(2);

        MessageOpcode[] observerHeard = server.Transport.SentTo(observer).Select(message => message.Opcode).ToArray();
        EquipmentAppearance[] seen = AppearancesOf(server, observer, entity);
        CommandRejectionReason[] refused = server.Transport.SentTo(actor)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read)
                ? read.Reason
                : CommandRejectionReason.None)
            .ToArray();
        Assert.That(observerHeard.Distinct(), Is.SubsetOf(WhatAnyoneNearSees), "only what anyone near sees");
        Assert.That(
            refused,
            Is.EqualTo(Enumerable.Repeat(CommandRejectionReason.ItemActionInFlight, 3)),
            "the burst's three while the first was in flight");
        Assert.That(seen, Has.Length.EqualTo(8 + 1 + 10 + 1), "one for each committed change");
        Assert.That(seen.Select(appearance => appearance.Revision), Is.Ordered.Ascending, "in rising revisions");
        Assert.That(seen.Select(appearance => appearance.Revision), Is.Unique, "each revision once");
        Assert.That(
            ItemsOf(seen.Last()),
            Is.EqualTo(new[] { string.Empty, Leather, Cap, Buckler, Cape, Boots, TravelerCharm, GrottoCharm }),
            "the staff off with the job change, the leather on after the last swap");
        Assert.That(ItemsOf(seen.Last()), Is.EqualTo(WornBy(server, actor)), "what the actor wears");
    }

    // The first jobs (Milestone 10 verification): the actor, at the Adventurer's job level 10, becomes an Arcanist at
    // the Guildmaster, selects the observer, and Mends it. The actor hears its sheet with the job, its skill list, and
    // its selection; the observer hears the actor's new body as one replacement spawn and nothing of its sheet, its
    // skills, or its selection, and of the heal what anyone near sees and its own health.
    [Test]
    public void AnotherPlayersJobChangeAndMend_ReachAnObserverAsItsNewBodyAndTheHealAlone()
    {
        var server = new TestServer(withNpcs: true, withAdventurerBuild: false);
        ConnectionId actor = server.EnterWorld(Actor);
        ConnectionId observer = server.EnterWorld(Observer);
        server.PlayerOf(actor).JobLevel = 10;
        NpcEntity guildmaster = server.NpcOf(Guildmaster);
        server.Place(actor, guildmaster.Position.X + 2f, guildmaster.Position.Z);
        server.Place(observer, guildmaster.Position.X + 3f, guildmaster.Position.Z);
        server.Tick(2);
        server.Transport.ClearSent();

        server.SendChangeJob(actor, guildmaster.Id, "job.arcanist", 1);
        Settle(server, actor);
        server.Tick(2);
        PlayerEntity arcanist = server.PlayerOf(actor);
        PlayerEntity healed = server.PlayerOf(observer);
        arcanist.SetSkillLevel(new SkillDefinitionId("skill.mend"), 1);
        healed.CurrentHealth = 10;
        server.SendTarget(actor, healed.Id);
        server.Tick();
        Cast(server, actor, "skill.mend", healed.Id, 2);

        MessageOpcode[] actorHeard = server.Transport.SentTo(actor).Select(message => message.Opcode).ToArray();
        MessageOpcode[] observerHeard = server.Transport.SentTo(observer).Select(message => message.Opcode).ToArray();
        EntitySpawn[] spawns = Read(
                server,
                observer,
                MessageOpcode.EntitySpawn,
                payload => EntitySpawn.TryRead(payload, out EntitySpawn? spawn) ? spawn : null)
            .Where(spawn => spawn.Entity == arcanist.Id)
            .ToArray();
        CharacterSheet sheet = Read(
                server,
                actor,
                MessageOpcode.CharacterSheet,
                payload => CharacterSheet.TryRead(payload, out CharacterSheet? read) ? read : null)
            .Last();
        Assert.That(arcanist.Job.Value, Is.EqualTo("job.arcanist"), "the change went through");
        Assert.That(
            actorHeard,
            Is.SupersetOf(new[] { MessageOpcode.CharacterSheet, MessageOpcode.SkillList, MessageOpcode.TargetChanged }),
            "the actor was told of its change and its selection");
        Assert.That(sheet.Job.Value, Is.EqualTo("job.arcanist"));
        Assert.That(
            spawns.Select(spawn => (spawn.Kind, spawn.DefinitionId, spawn.StateFlags)),
            Is.EqualTo(new[] { (EntityKind.Player, "job.arcanist", EntityStateFlags.None) }),
            "one replacement spawn, the new job");
        Assert.That(
            observerHeard.Distinct(),
            Is.SubsetOf(WhatAnyoneNearSees.Append(MessageOpcode.CharacterHealth)),
            "what anyone near sees, and its own health");
        Assert.That(
            observerHeard,
            Has.None.EqualTo(MessageOpcode.CharacterSheet)
                .And.None.EqualTo(MessageOpcode.SkillList)
                .And.None.EqualTo(MessageOpcode.TargetChanged),
            "never another's sheet, skills, or selection");
        Assert.That(healed.CurrentHealth, Is.GreaterThanOrEqualTo(50), "Mend 1 on the observer");
    }

    // The market board (Milestone 15 verification): the actor reads the board, lists ten Gloom Dust, and collects its
    // listing after a third player bought four, while the observer stands beside both throughout. The actor hears its
    // board, its listings, its bag, and its notices, the buyer its purchase and its board; the observer hears what
    // anyone near sees, and nothing more.
    [Test]
    public void AnotherPlayersMarketCommands_ReachAnObserverOnlyAsWhatAnyoneNearSees()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId actor = EnterBesideTheBroker(server, Actor, 100, store => store.GiveItems(Actor, Dust, 1, 10, 1));
        ConnectionId observer = EnterBesideTheBroker(server, Observer, 0, null);
        ConnectionId buyer = EnterBesideTheBroker(server, 3, 500, null);
        EntityId broker = server.NpcOf("npc.broker").Id;
        server.Tick();
        server.Transport.ClearSent();

        server.SendMarketOpen(actor, broker, 1);
        server.Tick(3);
        server.SendMarketList(actor, broker, RowOf(server, actor, Dust), 10, 2);
        Settle(server, actor);
        server.Tick(3);
        server.SendMarketBuy(buyer, broker, Dust, 4, 28, 1);
        Settle(server, buyer);
        server.Tick(3);
        long listing = Read(
                server,
                actor,
                MessageOpcode.MarketEvent,
                payload => MarketEvent.TryRead(payload, out MarketEvent? read) ? read : null)
            .First()
            .Listing;
        server.SendMarketCollect(actor, broker, listing, 3);
        Settle(server, actor);
        server.Tick(3);

        MessageOpcode[] actorHeard = server.Transport.SentTo(actor).Select(message => message.Opcode).ToArray();
        MessageOpcode[] buyerHeard = server.Transport.SentTo(buyer).Select(message => message.Opcode).ToArray();
        MessageOpcode[] observerHeard = server.Transport.SentTo(observer).Select(message => message.Opcode).ToArray();
        Assert.That(
            actorHeard,
            Is.SupersetOf(
                new[]
                {
                    MessageOpcode.MarketBoard, MessageOpcode.MarketMine, MessageOpcode.MarketEvent,
                    MessageOpcode.InventoryChanged
                }),
            "the actor was told of its board, its listing, its sale, and its collection");
        Assert.That(
            buyerHeard,
            Is.SupersetOf(new[] { MessageOpcode.MarketBoard, MessageOpcode.MarketMine, MessageOpcode.InventoryChanged })
                .And.No.Member(MessageOpcode.MarketEvent),
            "the buyer heard its purchase, never the sale's notice");
        Assert.That(server.SessionOf(actor).Character!.Inventory.Coins, Is.EqualTo(200L), "the fee, then 102 net");
        Assert.That(observerHeard.Distinct(), Is.SubsetOf(WhatAnyoneNearSees));
    }

    [Test]
    public void AnotherPlayersParty_ReachesAnObserverOutsideItAsNothing()
    {
        var rig = new PartyRig();
        ConnectionId actor = rig.Enter(Actor);
        ConnectionId observer = rig.Enter(Observer);
        ConnectionId member = rig.Enter(3);
        rig.Server.Transport.ClearSent();

        rig.Join(actor, "Tester1", member, "Tester3");
        rig.Server.PlayerOf(actor).CurrentHealth /= 2;
        rig.Server.SendChat(actor, ChatChannel.Party, string.Empty, "members only", rig.Next(actor));
        rig.Server.Tick(TestServer.TickRate + 1);
        rig.Lead(actor, "Tester3");
        rig.Leave(actor);

        MessageOpcode[] observerHeard =
            rig.Server.Transport.SentTo(observer).Select(message => message.Opcode).ToArray();
        Assert.That(
            rig.Server.Transport.SentTo(member).Select(message => message.Opcode),
            Is.SupersetOf(
                new[] { MessageOpcode.PartyEvent, MessageOpcode.PartyRoster, MessageOpcode.PartyMemberStatus }),
            "the member heard the party");
        Assert.That(observerHeard.Distinct(), Is.SubsetOf(WhatAnyoneNearSees));
    }

    [Test]
    public void AnotherPlayersPlay_ReachesAnObserverOnlyAsWhatAnyoneNearSees()
    {
        var server = new TestServer(
            withMonsters: true,
            combatRandom: new SureHitRandom(),
            withMonsterAi: false,
            dropRandom: new ScriptedRandom(0, 0, 0, 999_999),
            withEveryMap: true);
        ConnectionId actor = server.Connect();
        server.SignInWithCharacter(actor, Actor);
        server.Store.GiveItems(Actor, Sword, 1, 1, 1);
        server.Store.GiveItems(Actor, Potion, 1, 2, 1);
        server.SendEnterWorld(actor, Actor);
        server.TickUntil(() => server.SessionOf(actor).State == SessionState.InWorld);
        ConnectionId observer = server.EnterWorld(Observer);
        MonsterEntity slime = server.MonstersNear(server.PlayerOf(observer).Position).First();
        StandBeside(server, actor, slime);
        server.Tick(2);
        server.Transport.ClearSent();

        Cast(server, actor, Strike, slime.Id, 1);
        slime.CurrentHealth = 1;
        server.SendAttack(actor, slime.Id, 2);
        TickUntil(server, () => slime.IsDead, "the slime died");
        ItemDropped gel = Read(server, actor, MessageOpcode.ItemDropped, payload =>
            ItemDropped.TryRead(payload, out ItemDropped? read) ? read : null).Single();
        server.SendPickup(actor, gel.Entity, 3);
        Settle(server, actor);
        Cast(server, actor, FirstAid, default, 4);
        Cast(server, actor, Focus, default, 5);
        server.SendEquip(actor, RowOf(server, actor, Sword), 6);
        Settle(server, actor);
        server.PlayerOf(actor).CurrentHealth = 10;
        server.SendUseItem(actor, RowOf(server, actor, Potion), 7);
        Settle(server, actor);
        server.SendUnequip(actor, EquipmentSlot.Armor, 8);
        server.Tick();
        server.PlayerOf(actor).Position = GroundOf(server).Definition.Portals.Single().Center;
        server.Tick(2);

        MessageOpcode[] actorHeard = server.Transport.SentTo(actor).Select(message => message.Opcode).ToArray();
        MessageOpcode[] observerHeard = server.Transport.SentTo(observer).Select(message => message.Opcode).ToArray();
        Assert.That(
            actorHeard,
            Is.SupersetOf(
                new[]
                {
                    MessageOpcode.CharacterProgress, MessageOpcode.InventoryChanged, MessageOpcode.CharacterHealth,
                    MessageOpcode.StatusEffects, MessageOpcode.CommandRejected, MessageOpcode.WorldEntered,
                    MessageOpcode.InventorySnapshot, MessageOpcode.SkillList
                }),
            "the actor was told of its own play");
        Assert.That(observerHeard.Distinct(), Is.SubsetOf(WhatAnyoneNearSees));
        Assert.That(
            observerHeard,
            Is.SupersetOf(
                new[]
                {
                    MessageOpcode.AttackStarted, MessageOpcode.Damage, MessageOpcode.EntityDied,
                    MessageOpcode.ItemDropped, MessageOpcode.ItemPickedUp, MessageOpcode.EntityDespawn
                }),
            "the observer watched the whole play");
        Assert.That(
            Read(server, observer, MessageOpcode.SkillCastStarted, payload =>
                    SkillCastStarted.TryRead(payload, out SkillCastStarted? read) ? read : null)
                .Select(cast => cast.Skill.Value),
            Is.EqualTo(new[] { Strike, FirstAid, Focus }));
        Assert.That(
            Read(server, observer, MessageOpcode.EquipmentAppearance, payload =>
                    EquipmentAppearance.TryRead(payload, out EquipmentAppearance? read) ? read : null)
                .Where(appearance => appearance.Entity == server.PlayerOf(actor).Id)
                .Select(appearance => appearance.ItemIn(EquipmentSlot.Weapon).Value)
                .Last(),
            Is.EqualTo(Sword),
            "the sword in the actor's hand, from its appearance");
    }

    // The quarry (Milestone 21 line 5): the actor fights the Furnace Heart in its arena while the observer stands near
    // it, a third player waits in the quarry's arrival yard, out of their sight, a fourth stands in the Umbral Grotto,
    // the other boss's map, and a fifth in town. The Furnace Heart's burst lands on the actor, and the boss falls to it,
    // its most valuable player. The observer hears what anyone near sees and the fall; the yard hears the fall and
    // nothing of the burst; the grotto and the town hear nothing of the quarry's boss; and only the actor hears the
    // award.
    [Test]
    public void AnotherPlayersQuarryBoss_ReachesAnObserverAsWhatAnyoneNearSees_AndOnlyItsMapAsTheBossFalls()
    {
        var server = new TestServer(
            withEveryMap: true,
            withGrotto: true,
            withQuarry: true,
            withMonsters: true,
            withMonsterAi: false,
            dropRandom: new ScriptedRandom(0));
        ConnectionId actor = server.EnterWorld(Actor);
        ConnectionId observer = server.EnterWorld(Observer);
        ConnectionId yard = server.EnterWorld(3);
        ConnectionId grotto = server.EnterWorld(4);
        ConnectionId town = server.EnterWorld(5);
        foreach (ConnectionId player in new[] { actor, observer, yard })
        {
            server.CrossIntoTheQuarry(player);
        }

        server.CrossIntoTheGrotto(grotto);
        MapInstance quarry = QuarryOf(server);
        MonsterEntity boss = quarry.Monsters.Single(monster => monster.Definition.IsBoss);
        PlayerEntity acting = server.PlayerOf(actor);
        acting.Position = new WorldPosition(boss.Position.X, 0f, boss.Position.Z - 2.5f);
        acting.CurrentHealth = 1_000_000;
        server.PlayerOf(observer).Position = new WorldPosition(boss.Position.X - 6f, 0f, boss.Position.Z - 6f);
        server.Tick(2);
        server.Transport.ClearSent();

        server.Combat.BeginMonsterCast(quarry, boss, new SkillDefinitionId(FurnaceBurst), acting, server.CurrentTick);
        server.Tick(2 * TestServer.TickRate + 1);
        boss.LogMvpDealt(acting.Character, 500);
        server.Combat.Kill(quarry, boss, acting, server.CurrentTick);
        TickUntil(server, () => !server.SessionOf(actor).Character!.HasInventoryWork, "the prize settled");

        MessageOpcode[] observerHeard = server.Transport.SentTo(observer).Select(message => message.Opcode).ToArray();
        MessageOpcode[] yardHeard = server.Transport.SentTo(yard).Select(message => message.Opcode).ToArray();
        Assert.That(
            Read(server, observer, MessageOpcode.SkillResolved, payload =>
                    SkillResolved.TryRead(payload, out SkillResolved? read) ? read : null)
                .Where(resolved => resolved.Skill.Value == FurnaceBurst)
                .Select(resolved => resolved.Target),
            Is.EqualTo(new[] { acting.Id }),
            "the burst's result on the actor, whom the observer knows");
        Assert.That(
            server.Transport.SentTo(actor).Select(message => message.Opcode),
            Is.SupersetOf(
                new[] { MessageOpcode.BossAnnouncement, MessageOpcode.MvpAwarded, MessageOpcode.InventoryChanged }),
            "the actor was told of the fall and its award");
        Assert.That(
            observerHeard.Distinct(),
            Is.SubsetOf(WhatAnyoneNearSees.Append(MessageOpcode.BossAnnouncement)));
        Assert.That(observerHeard, Does.Contain(MessageOpcode.BossAnnouncement), "the observer heard the fall");
        Assert.That(yardHeard, Does.Contain(MessageOpcode.BossAnnouncement), "the yard heard the fall");
        Assert.That(yardHeard, Has.No.Member(MessageOpcode.SkillResolved), "but nothing of the burst far off");
        foreach (ConnectionId elsewhere in new[] { grotto, town })
        {
            Assert.That(
                server.Transport.SentTo(elsewhere).Select(message => message.Opcode),
                Has.No.Member(MessageOpcode.BossAnnouncement)
                    .And.No.Member(MessageOpcode.SkillResolved)
                    .And.No.Member(MessageOpcode.MvpAwarded),
                "nothing of the quarry's boss off its map");
        }
    }

    // The town's play (Milestone 7 verification line V4): the actor buys from the Quartermaster, sells to it, and
    // accepts and turns in the Gate Warden's quest while the observer stands at the spawn point, from where both NPCs
    // are in view. The prices, the catalogue, and the reward went to both with each NPC's services when it came into
    // view; the actor's coins, rows, quest, and reward reach the observer as nothing.
    [Test]
    public void AnotherPlayersShoppingAndQuest_ReachAnObserverOnlyAsWhatAnyoneNearSees()
    {
        var server = new TestServer(withNpcs: true);
        ConnectionId actor = server.Connect();
        server.SignInWithCharacter(actor, Actor);
        server.Store.Edit(Actor, coins: 100);
        server.Store.GiveItems(Actor, Gel, 1, 3, 1);
        server.SendEnterWorld(actor, Actor);
        server.TickUntil(() => server.SessionOf(actor).State == SessionState.InWorld);
        ConnectionId observer = server.EnterWorld(Observer);
        NpcEntity quartermaster = server.NpcOf(Quartermaster);
        NpcEntity warden = server.NpcOf(GateWarden);
        server.Place(actor, quartermaster.Position.X + 2f, quartermaster.Position.Z);
        server.Tick(2);
        server.Transport.ClearSent();

        server.SendBuy(actor, quartermaster.Id, Potion, 1, 1);
        Settle(server, actor);
        server.SendSell(actor, quartermaster.Id, RowOf(server, actor, Gel), 3, 2);
        Settle(server, actor);
        server.Place(actor, warden.Position.X + 2f, warden.Position.Z);
        server.Tick(2);
        server.SendAcceptQuest(actor, warden.Id, Hunt, 3);
        server.Tick(2);
        server.SessionOf(actor).Character!.Quests.Entries.Single().Progress = 5;
        server.SendCompleteQuest(actor, warden.Id, Hunt, 4);
        Settle(server, actor);

        MessageOpcode[] actorHeard = server.Transport.SentTo(actor).Select(message => message.Opcode).ToArray();
        MessageOpcode[] observerHeard = server.Transport.SentTo(observer).Select(message => message.Opcode).ToArray();
        Assert.That(
            actorHeard,
            Is.SupersetOf(
                new[] { MessageOpcode.InventoryChanged, MessageOpcode.QuestLog, MessageOpcode.CharacterProgress }),
            "the actor was told of its purchases and sales, its quest, and its reward");
        Assert.That(server.SessionOf(actor).Character!.Inventory.Coins, Is.EqualTo(186L), "all three went through");
        Assert.That(observerHeard.Distinct(), Is.SubsetOf(WhatAnyoneNearSees));
        Assert.That(
            server.Transport.SnapshotsSentTo(observer).Last().Entities.Select(state => state.Entity),
            Does.Contain(server.PlayerOf(actor).Id),
            "the observer still saw the actor at the Gate Warden");
    }

    // The trade and storage (Milestone 14 verification): the actor trades gel to a partner, then stores and takes back
    // gel at the Storekeeper, while the observer stands beside it throughout. The traders and the depositor hear their
    // own; the observer hears what anyone near sees, and nothing more.
    [Test]
    public void AnotherPlayersTradeAndStorage_ReachAnObserverOnlyAsWhatAnyoneNearSees()
    {
        var rig = new TradeRig(new TestServer(withNpcs: true));
        ConnectionId actor = rig.Enter(Actor, store =>
        {
            store.GiveItems(Actor, Gel, 1, 6, 1);
            store.Edit(Actor, coins: 40);
        });
        ConnectionId observer = rig.Enter(Observer);
        ConnectionId partner = rig.Enter(3);
        rig.Server.Tick();
        rig.Server.Transport.ClearSent();

        rig.Open(actor, partner, "Tester1", "Tester3");
        rig.Offer(actor, RowOf(rig.Server, actor, Gel), 2);
        rig.Lock(actor);
        rig.Lock(partner);
        rig.Confirm(actor);
        rig.Confirm(partner);
        rig.Server.TickUntil(() => rig.Server.Trades.OpenTrades == 0);
        NpcEntity keeper = rig.Server.NpcOf("npc.storekeeper");
        rig.Server.Place(actor, keeper.Position.X + 2f, keeper.Position.Z);
        rig.Server.Place(observer, keeper.Position.X + 2f, keeper.Position.Z + 1f);
        rig.Server.Tick(2);
        rig.Server.SendStorageOpen(actor, keeper.Id, rig.Next(actor));
        rig.Server.Tick(2);
        rig.Server.SendStorageDeposit(actor, keeper.Id, RowOf(rig.Server, actor, Gel), 4, rig.Next(actor));
        Settle(rig.Server, actor);
        long stored = Read(
                rig.Server,
                actor,
                MessageOpcode.StorageChanged,
                payload => StorageChanged.TryRead(payload, out StorageChanged? read) ? read : null)
            .Single()
            .Row.StorageItem;
        rig.Server.SendStorageWithdraw(actor, keeper.Id, stored, 1, rig.Next(actor));
        Settle(rig.Server, actor);

        MessageOpcode[] actorHeard = rig.Server.Transport.SentTo(actor).Select(message => message.Opcode).ToArray();
        MessageOpcode[] observerHeard =
            rig.Server.Transport.SentTo(observer).Select(message => message.Opcode).ToArray();
        Assert.That(
            actorHeard,
            Is.SupersetOf(
                new[]
                {
                    MessageOpcode.TradeEvent, MessageOpcode.TradeSide, MessageOpcode.InventorySnapshot,
                    MessageOpcode.StorageSnapshot, MessageOpcode.StorageChanged, MessageOpcode.InventoryChanged
                }),
            "the actor was told of its trade and its storage");
        Assert.That(rig.Server.SessionOf(actor).Character!.Inventory.Coins, Is.EqualTo(20L), "the deposit's fee");
        Assert.That(observerHeard.Distinct(), Is.SubsetOf(WhatAnyoneNearSees));
    }
}
}
