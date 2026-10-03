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
///     result, and a boss's fall everyone on its map.
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
    private const string Quartermaster = "npc.quartermaster";
    private const string GateWarden = "npc.gate_warden";
    private const string Hunt = "quest.crawler_hunt";
    private const string Guildmaster = "npc.guildmaster";
    private const string NumbingSpark = "skill.numbing_spark";
    private const string QuakeSlam = "skill.quake_slam";

    private static readonly MessageOpcode[] WhatAnyoneNearSees =
    {
        MessageOpcode.EntitySpawn, MessageOpcode.EntityDespawn, MessageOpcode.EntitySnapshot,
        MessageOpcode.AttackStarted, MessageOpcode.Damage, MessageOpcode.EntityDied, MessageOpcode.EntityRevived,
        MessageOpcode.SkillCastStarted, MessageOpcode.SkillResolved, MessageOpcode.ItemDropped,
        MessageOpcode.ItemPickedUp, MessageOpcode.WornWeaponChanged
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
            dropRandom: new ScriptedRandom(0),
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
            Read(server, observer, MessageOpcode.WornWeaponChanged, payload =>
                    WornWeaponChanged.TryRead(payload, out WornWeaponChanged? read) ? read : null)
                .Select(changed => (changed.Entity, changed.WornWeapon)),
            Is.EqualTo(new[] { (server.PlayerOf(actor).Id, Sword) }),
            "the sword in the actor's hand, told once; the armor taken off, never");
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
}
}
