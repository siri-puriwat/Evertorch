using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The Ashen Quarry's balance against the pinned builds (Milestone 21 owner decisions 24 and 25): characters stored at
///     base level 25 and job level 10 with the baseline's statistics, skills, and complete +0 dungeon set load through
///     the real path and fight the quarry's monsters on the server with real commands, under abuse control: they walk
///     the grid's paths, keep their buffs up, strike with their skills by SP, drink and cure as a player would, and step
///     out of the boss's telegraphed casts. No command is refused or throttled and nobody dies; the means meet decision
///     24's targets, as means over the seeds (the session plan's statistic; the output gives the worst fight too), and
///     every boss fight ends within three to five minutes. A fight is timed and costed from its first strike to its last
///     kill, since the buffs cast before it last a minute: the HP lost is every hit and skill that struck the fighter,
///     over its maximum HP at the first strike, and the SP spent a kill is the cost of every cast it finished, over its
///     maximum SP then and the kills. Each case writes its figures to the test output, the loot at the shops' prices and
///     at the board's, the MVP's prize aside.
/// </summary>
[TestFixture]
public sealed class QuarryBalanceTests
{
    private const int SoloSeeds = 20;
    private const int BossSeeds = 10;
    private const int OrdinaryLimitTicks = 90 * TestServer.TickRate;
    private const int BossLimitTicks = 8 * 60 * TestServer.TickRate;
    private const int MaxExpandedNodes = 8192;
    private const string Cinder = "monster.cinder_crawler";
    private const string Basalt = "monster.basalt_crawler";
    private const string Wisp = "monster.ash_wisp";
    private const string Heart = "monster.furnace_heart";
    private const string HealthPotion = "item.consumable.minor_health";
    private const string ManaPotion = "item.consumable.minor_mana";
    private const string Remedy = "item.consumable.remedy";
    private const string GrandRemedy = "item.consumable.grand_remedy";
    private const string WaterConverter = "item.consumable.water_converter";

    private static readonly MapDefinitionId Quarry = new("map.ashen_quarry");

    private static readonly Build Vanguard = new(
        "job.vanguard",
        new PrimaryStats(25, 8, 15, 5, 20, 5),
        new Dictionary<string, int>
        {
            ["skill.strike"] = 3, ["skill.first_aid"] = 1, ["skill.focus"] = 3, ["skill.heavy_blow"] = 5,
            ["skill.war_cry"] = 3, ["skill.iron_guard"] = 3
        },
        "item.weapon.iron_sword");

    private static readonly Build Arcanist = new(
        "job.arcanist",
        new PrimaryStats(5, 5, 15, 30, 15, 5),
        new Dictionary<string, int>
        {
            ["skill.arcane_bolt"] = 5, ["skill.clarity"] = 3, ["skill.mend"] = 3, ["skill.frost_lance"] = 5,
            ["skill.strike"] = 1, ["skill.focus"] = 1
        },
        "item.weapon.ash_staff");

    // Decision 24's complete +0 dungeon set, beside each build's weapon.
    private static readonly (string Item, EquipmentSlot Slot)[] DungeonSet =
    {
        ("item.armor.leather", EquipmentSlot.Armor), ("item.headgear.grotto_circlet", EquipmentSlot.Headgear),
        ("item.shield.carapace", EquipmentSlot.Shield), ("item.garment.gloom_cape", EquipmentSlot.Garment),
        ("item.footwear.grotto_boots", EquipmentSlot.Footwear),
        ("item.accessory.grotto_charm", EquipmentSlot.Accessory1),
        ("item.accessory.grotto_charm", EquipmentSlot.Accessory2)
    };

    private static MapInstance QuarryOf(TestServer server)
    {
        server.World.TryGetMap(Quarry, out MapInstance? map);
        return map!;
    }

    private static float Distance(WorldPosition a, WorldPosition b)
    {
        float dx = a.X - b.X;
        float dz = a.Z - b.Z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static long NowMs(TestServer server)
    {
        return (long)(server.CurrentTick - 1) * 1000 / TestServer.TickRate;
    }

    // A floor cell the given distance from a place, in sight of it, the first of sixteen bearings past the skipped.
    private static WorldPosition Around(MapInstance map, WorldPosition at, float distance, int skip = 0)
    {
        NavigationGrid grid = map.Definition.Navigation;
        int found = 0;
        for (int step = 0; step < 16; step++)
        {
            double angle = step * Math.PI / 8.0;
            float x = at.X + distance * (float)Math.Cos(angle);
            float z = at.Z + distance * (float)Math.Sin(angle);
            if (grid.CanOccupy(x, z)
                && grid.TrySampleHeight(x, z, out float height)
                && grid.HasLineOfSight(new WorldPosition(x, height, z), at)
                && found++ >= skip)
            {
                return new WorldPosition(x, height, z);
            }
        }

        throw new InvalidOperationException($"No floor {distance} m from ({at.X}, {at.Z}).");
    }

    // The party, stored in the quarry the given distance from the prey's first monster, enters the world: each character
    // loads through the real path with its build, its set worn, and what decision 24 has it bring.
    private static Battle Enter(ulong seed, string monster, float distance, params Build[] builds)
    {
        var server = new TestServer(withEveryMap: true, withQuarry: true, withMonsters: true, randomSeed: seed);
        MapInstance quarry = QuarryOf(server);
        MonsterEntity first = quarry.Monsters.Where(candidate => candidate.Definition.Id.Value == monster)
            .OrderBy(candidate => candidate.Id.Value)
            .First();
        var party = new List<Fighter>();
        for (int index = 0; index < builds.Length; index++)
        {
            Build build = builds[index];
            long id = index + 1;
            ConnectionId connection = server.Connect();
            server.SignInWithCharacter(connection, id);
            server.Store.Edit(
                id,
                build.Job,
                Quarry.Value,
                Around(quarry, first.Position, distance, index),
                1_000_000,
                spirit: 1_000_000,
                level: 25,
                jobLevel: 10,
                stats: build.Stats,
                skills: build.Skills);
            Give(server, id, build.Weapon, 1, EquipmentSlot.Weapon);
            foreach ((string item, EquipmentSlot slot) in DungeonSet)
            {
                Give(server, id, item, 1, slot);
            }

            Give(server, id, HealthPotion, 10, EquipmentSlot.None);
            Give(server, id, ManaPotion, 10, EquipmentSlot.None);
            Give(server, id, Remedy, 3, EquipmentSlot.None);
            Give(server, id, GrandRemedy, 3, EquipmentSlot.None);
            if (monster == Heart && build == Vanguard)
            {
                Give(server, id, WaterConverter, 1, EquipmentSlot.None);
            }

            server.SendEnterWorld(connection, id);
            server.TickUntil(() => server.SessionOf(connection).State == SessionState.InWorld);
            party.Add(new Fighter(server, connection, build, new GridPathfinder(quarry.Definition.Navigation)));
        }

        Assert.That(server.BuildsLog.Entries, Is.Empty, "every build loaded as stored, none reset");
        foreach (Fighter fighter in party)
        {
            PlayerEntity player = fighter.Self;
            Assert.That(
                (server.Builds.StatPointsLeft(player), server.Builds.SkillPointsLeft(player)),
                Is.EqualTo((fighter.Build == Arcanist ? 1 : 0, 0)),
                "every point spent, but the Arcanist's last, too few for a raise");
            Assert.That(
                server.SessionOf(fighter.Connection).Character!.Inventory.Rows.Count(row =>
                    row.Slot != EquipmentSlot.None),
                Is.EqualTo(8),
                "all eight positions worn");
            Assert.That(
                (player.CurrentHealth, player.CurrentSpirit),
                Is.EqualTo((player.MaxHealth, player.MaxSpirit)),
                "from full HP and SP");
        }

        MonsterEntity[] prey = monster == Basalt
            ? quarry.Monsters.Where(candidate => candidate.Definition.Id.Value == Basalt
                    && Distance(candidate.Position, first.Position) < 6f)
                .ToArray()
            : new[] { first };
        return new Battle(server, party.ToArray(), prey);
    }

    private static void Give(TestServer server, long id, string item, int quantity, EquipmentSlot slot)
    {
        server.Store.GiveItems(id, item, 1, quantity, 1);
        if (slot != EquipmentSlot.None)
        {
            server.Store.Wear(id, server.Store.Stored(id).Items.Max(stored => stored.Id), slot.ToString());
        }
    }

    // The fight, a tick at a time: each fighter decides, the server ticks, and the costs are counted, until the prey
    // falls, a fighter dies, or the time runs out.
    private static Outcome Fight(Battle battle, int limitTicks)
    {
        TestServer server = battle.Server;
        uint started = 0;
        uint ended = 0;
        var drops = new HashSet<long>(QuarryOf(server).ItemDrops.Select(drop => drop.Id.Value));
        for (int tick = 0; tick < limitTicks; tick++)
        {
            foreach (Fighter fighter in battle.Party)
            {
                fighter.Act(battle);
            }

            server.Tick();
            if (started == 0 && battle.Party.Any(fighter => fighter.HasStruck))
            {
                started = server.CurrentTick;
                foreach (Fighter fighter in battle.Party)
                {
                    fighter.BeginCounting();
                }
            }

            if (battle.Prey.All(monster => monster.IsDead))
            {
                ended = server.CurrentTick;
                break;
            }

            if (battle.Party.Any(fighter => fighter.Self.IsDead))
            {
                break;
            }
        }

        MapInstance quarry = QuarryOf(server);
        ItemDropEntity[] loot = quarry.ItemDrops.Where(drop => !drops.Contains(drop.Id.Value)).ToArray();
        return new Outcome(
            ended != 0,
            ended == 0 ? double.NaN : (double)(ended - started) / TestServer.TickRate,
            battle.Party.Count(fighter => fighter.Self.IsDead),
            battle.Party.Sum(fighter => fighter.Refusals),
            battle.Party.Sum(fighter => fighter.Throttled),
            battle.Party.Select(fighter => fighter.Summary()).ToArray(),
            battle.Prey.Length,
            loot.Sum(drop => drop.Amount * server.Content.Items[drop.Item].SellPrice),
            loot.Sum(drop => drop.Amount * (server.Content.Items[drop.Item].Market?.BasePrice ?? 0)));
    }

    private static string Describe(string name, IReadOnlyList<Outcome> outcomes)
    {
        FighterSummary[] fighters = outcomes.SelectMany(outcome => outcome.Fighters).ToArray();
        double[] seconds = outcomes.Where(outcome => outcome.IsWon).Select(outcome => outcome.Seconds).ToArray();
        return string.Format(
            CultureInfo.InvariantCulture,
            "{0}: {1} fights, {2} won, {3} deaths, {4} refusals, {5} throttled; kill {6:F1} s mean, {7:F1} to {8:F1}; "
            + "HP lost {9:P0} mean, {10:P0} most; SP spent {11:P0} a kill; potions {12:F2} and cures {13:F2} a fight; "
            + "loot {14:F0} coins a fight at shop prices, {15:F0} at board prices",
            name,
            outcomes.Count,
            outcomes.Count(outcome => outcome.IsWon),
            outcomes.Sum(outcome => outcome.Deaths),
            outcomes.Sum(outcome => outcome.Refusals),
            outcomes.Sum(outcome => outcome.Throttled),
            seconds.DefaultIfEmpty(double.NaN).Average(),
            seconds.DefaultIfEmpty(double.NaN).Min(),
            seconds.DefaultIfEmpty(double.NaN).Max(),
            fighters.Average(fighter => fighter.HealthLost),
            fighters.Max(fighter => fighter.HealthLost),
            outcomes.SelectMany(outcome => outcome.SpiritSpentAKill).Average(),
            fighters.Sum(fighter => fighter.Potions) / (double)outcomes.Count,
            fighters.Sum(fighter => fighter.Cures) / (double)outcomes.Count,
            outcomes.Average(outcome => outcome.ShopValue),
            outcomes.Average(outcome => outcome.BoardValue));
    }

    private static List<Outcome> Solo(string monster, Build build, int seeds)
    {
        var outcomes = new List<Outcome>();
        for (ulong seed = 1; seed <= (ulong)seeds; seed++)
        {
            outcomes.Add(Fight(Enter(seed, monster, 8f, build), OrdinaryLimitTicks));
        }

        return outcomes;
    }

    [TestCase(Cinder, "Vanguard", 15d, 0.35)]
    [TestCase(Cinder, "Arcanist", 8d, 0.35)]
    [TestCase(Basalt, "Vanguard", 25d, 0.45)]
    [TestCase(Basalt, "Arcanist", 12d, 0.45)]
    [TestCase(Wisp, "Vanguard", 15d, 0.35)]
    [TestCase(Wisp, "Arcanist", 8d, 0.35)]
    public void AnOrdinaryFight_AloneWithEitherBuild_MeetsDecision24sTargets(
        string monster,
        string job,
        double seconds,
        double healthLost)
    {
        Build build = job == "Arcanist" ? Arcanist : Vanguard;
        List<Outcome> outcomes = Solo(monster, build, SoloSeeds);
        TestContext.WriteLine(Describe($"{monster} by the {job}", outcomes));

        Assert.That(
            outcomes.Select(outcome => (outcome.IsWon, outcome.Deaths, outcome.Refusals, outcome.Throttled)),
            Is.All.EqualTo((true, 0, 0, 0)),
            "every fight won, nobody dead, no command refused");
        Assert.That(outcomes.Average(outcome => outcome.Seconds), Is.LessThanOrEqualTo(seconds), "the mean kill time");
        Assert.That(
            outcomes.SelectMany(outcome => outcome.Fighters).Average(fighter => fighter.HealthLost),
            Is.LessThanOrEqualTo(healthLost),
            "the mean HP lost");
        if (build == Arcanist)
        {
            Assert.That(
                outcomes.SelectMany(outcome => outcome.SpiritSpentAKill).Average(),
                Is.LessThanOrEqualTo(0.25),
                "the mean SP spent a kill");
        }
    }

    [TestCase("Vanguard,Vanguard,Arcanist")]
    [TestCase("Vanguard,Arcanist,Arcanist")]
    public void TheFurnaceHeart_FallsToEitherPartyShape_InThreeToFiveMinutes(string shape)
    {
        Build[] builds = shape.Split(',').Select(job => job == "Arcanist" ? Arcanist : Vanguard).ToArray();
        var outcomes = new List<Outcome>();
        for (ulong seed = 1; seed <= BossSeeds; seed++)
        {
            outcomes.Add(Fight(Enter(seed, Heart, 10f, builds), BossLimitTicks));
        }

        TestContext.WriteLine(Describe($"The Furnace Heart by {shape}", outcomes));
        Assert.That(
            outcomes.Select(outcome => (outcome.IsWon, outcome.Deaths, outcome.Refusals, outcome.Throttled)),
            Is.All.EqualTo((true, 0, 0, 0)),
            "every fight won, nobody dead, no command refused");
        Assert.That(outcomes.Select(outcome => outcome.Seconds), Is.All.InRange(180d, 300d), "three to five minutes");
    }

    private sealed class Build
    {
        public Build(string job, PrimaryStats stats, Dictionary<string, int> skills, string weapon)
        {
            Job = job;
            Stats = stats;
            Skills = skills;
            Weapon = weapon;
        }

        public string Job { get; }

        public PrimaryStats Stats { get; }

        public Dictionary<string, int> Skills { get; }

        public string Weapon { get; }
    }

    private sealed class Battle
    {
        public Battle(TestServer server, Fighter[] party, MonsterEntity[] prey)
        {
            Server = server;
            Party = party;
            Prey = prey;
        }

        public TestServer Server { get; }

        public Fighter[] Party { get; }

        public MonsterEntity[] Prey { get; }
    }

    private sealed class FighterSummary
    {
        public FighterSummary(double healthLost, double spiritSpent, int potions, int cures)
        {
            HealthLost = healthLost;
            SpiritSpent = spiritSpent;
            Potions = potions;
            Cures = cures;
        }

        public double HealthLost { get; }

        public double SpiritSpent { get; }

        public int Potions { get; }

        public int Cures { get; }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:R}/{1:R}/{2}/{3}",
                HealthLost,
                SpiritSpent,
                Potions,
                Cures);
        }
    }

    private sealed class Outcome
    {
        public Outcome(
            bool isWon,
            double seconds,
            int deaths,
            int refusals,
            int throttled,
            FighterSummary[] fighters,
            int kills,
            long shopValue,
            long boardValue)
        {
            IsWon = isWon;
            Seconds = seconds;
            Deaths = deaths;
            Refusals = refusals;
            Throttled = throttled;
            Fighters = fighters;
            Kills = kills;
            ShopValue = shopValue;
            BoardValue = boardValue;
        }

        public bool IsWon { get; }

        public double Seconds { get; }

        public int Deaths { get; }

        public int Refusals { get; }

        public int Throttled { get; }

        public FighterSummary[] Fighters { get; }

        public int Kills { get; }

        public IEnumerable<double> SpiritSpentAKill => Fighters.Select(fighter => fighter.SpiritSpent / Kills);

        public long ShopValue { get; }

        public long BoardValue { get; }

        public override string ToString()
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1:R} {2} {3} {4} {5} {6} [{7}]",
                IsWon,
                Seconds,
                Deaths,
                Refusals,
                Throttled,
                ShopValue,
                BoardValue,
                string.Join(" ", Fighters.Select(fighter => fighter.ToString())));
        }
    }

    // A player at the keyboard: one command a tick at most, and a step along its path toward where it means to stand.
    private sealed class Fighter
    {
        private readonly TestServer m_server;
        private readonly GridPathfinder m_pathfinder;
        private readonly List<WorldPosition> m_path = new();
        private WorldPosition? m_pathGoal;
        private uint m_commands;
        private uint m_moves;
        private bool m_isMoving;
        private EntityId m_attacking;
        private int m_firstCountedMessage = -1;
        private int m_maxHealthAtStart;
        private int m_maxSpiritAtStart;
        private int m_potions;
        private int m_cures;

        public Fighter(TestServer server, ConnectionId connection, Build build, GridPathfinder pathfinder)
        {
            m_server = server;
            Connection = connection;
            Build = build;
            m_pathfinder = pathfinder;
        }

        public ConnectionId Connection { get; }

        public Build Build { get; }

        public PlayerEntity Self => m_server.PlayerOf(Connection);

        public bool HasStruck { get; private set; }

        public int Refusals => m_server.Transport.ControlOpcodesSentTo(Connection)
            .Count(opcode => opcode == MessageOpcode.CommandRejected);

        public int Throttled => (int)m_server.SessionOf(Connection).ThrottledCommands;

        private CharacterSession Character => m_server.SessionOf(Connection).Character!;

        // What the fight costs, from its first strike: the buffs before it last a minute, several fights.
        public void BeginCounting()
        {
            m_firstCountedMessage = m_server.Transport.SentTo(Connection).Count;
            m_maxHealthAtStart = Self.MaxHealth;
            m_maxSpiritAtStart = Self.MaxSpirit;
        }

        // Every hit and skill that struck the fighter, and the cost of every cast it finished, as its client was told.
        public FighterSummary Summary()
        {
            PlayerEntity self = Self;
            long struck = 0;
            long paid = 0;
            IReadOnlyList<InMemoryServerTransport.SentMessage> sent = m_server.Transport.SentTo(Connection);
            for (int index = Math.Max(0, m_firstCountedMessage);
                 m_firstCountedMessage >= 0 && index < sent.Count;
                 index++)
            {
                InMemoryServerTransport.SentMessage message = sent[index];
                if (message.Opcode == MessageOpcode.Damage
                    && Damage.TryRead(message.Payload, out Damage damage)
                    && damage.Target == self.Id)
                {
                    struck += damage.Amount;
                }
                else if (message.Opcode == MessageOpcode.SkillResolved
                         && SkillResolved.TryRead(message.Payload, out SkillResolved? resolved))
                {
                    if (resolved!.Target == self.Id
                        && resolved.Caster != self.Id
                        && (resolved.Outcome == SkillOutcome.Hit || resolved.Outcome == SkillOutcome.Critical))
                    {
                        struck += resolved.Amount;
                    }
                    else if (resolved.Caster == self.Id && self.Skills.TryGetValue(resolved.Skill, out int level))
                    {
                        paid += m_server.Content.Skills[resolved.Skill].ValuesAt(level).SpCost;
                    }
                }
            }

            return new FighterSummary(
                m_maxHealthAtStart == 0 ? 0 : (double)struck / m_maxHealthAtStart,
                m_maxSpiritAtStart == 0 ? 0 : (double)paid / m_maxSpiritAtStart,
                m_potions,
                m_cures);
        }

        // Survive, keep out of a telegraphed area, keep the buffs, heal, then strike; and walk where that needs.
        public void Act(Battle battle)
        {
            PlayerEntity self = Self;
            if (self.IsDead || self.IsHeld)
            {
                return;
            }

            MonsterEntity? target = battle.Prey.FirstOrDefault(monster => !monster.IsDead);
            if (target == null)
            {
                Stop();
                return;
            }

            // The telegraph, as the client draws it: the cast's area around a monster that has begun it.
            MonsterEntity? caster = battle.Prey.FirstOrDefault(monster => !monster.IsDead
                && monster.Combat.IsCasting
                && m_server.Content.Skills[monster.Combat.CastSkill].HasArea);
            if (caster != null)
            {
                double radius = m_server.Content.Skills[caster.Combat.CastSkill].AreaRadius;
                if (Distance(self.Position, caster.Position) <= radius + 0.5)
                {
                    TryDrink(self);
                    WalkTo(EscapeFrom(caster, (float)radius + 1.2f), 0.3f);
                    return;
                }
            }

            if (TryDrink(self) || TryCure(self))
            {
                Hold(target);
                return;
            }

            if (Build == Arcanist)
            {
                ActAsArcanist(battle, self, target);
            }
            else
            {
                ActAsVanguard(self, target);
            }
        }

        private void ActAsVanguard(PlayerEntity self, MonsterEntity target)
        {
            if (TryBuff(self, "skill.war_cry", "status.war_cry")
                || TryBuff(self, "skill.iron_guard", "status.iron_guard")
                || TryBuff(self, "skill.focus", "status.focus")
                || (target.Definition.IsBoss && !HasConverted() && TryUse(WaterConverter, false)))
            {
                Stop();
                return;
            }

            float reach = self.AttackRange - 0.2f;
            if (!Sees(self, target))
            {
                WalkTo(target.Position, 0.5f);
                return;
            }

            if (Distance(self.Position, target.Position) > reach)
            {
                WalkTo(target.Position, reach);
                return;
            }

            Stop();
            // A stun, a freeze, or a sleep ends the auto-attack (Gameplay Systems §9.1), so it is asked for again.
            if (m_attacking != target.Id || !self.Combat.IsAutoAttacking)
            {
                m_server.SendAttack(Connection, target.Id, ++m_commands);
                m_attacking = target.Id;
                HasStruck = true;
            }
            else if (IsReady(self, "skill.heavy_blow"))
            {
                m_server.SendUseSkill(Connection, "skill.heavy_blow", target.Id, ++m_commands);
            }
        }

        private void ActAsArcanist(Battle battle, PlayerEntity self, MonsterEntity target)
        {
            if (TryBuff(self, "skill.clarity", "status.clarity"))
            {
                Stop();
                return;
            }

            // Mend whoever is hurt most, once its heal would not go to waste; a second Arcanist only when the first
            // falls behind.
            int heal = m_server.Content.Skills[new SkillDefinitionId("skill.mend")].ValuesAt(3).Effect.HealHp;
            double threshold = battle.Party.First(member => member.Build == Arcanist) == this ? 0.8 : 0.45;
            PlayerEntity? hurt = battle.Party.Select(member => member.Self)
                .Where(member => !member.IsDead
                    && member.MaxHealth - member.CurrentHealth >= heal
                    && (double)member.CurrentHealth / member.MaxHealth < threshold)
                .OrderBy(member => (double)member.CurrentHealth / member.MaxHealth)
                .FirstOrDefault();
            if (hurt != null && IsReady(self, "skill.mend"))
            {
                if (Distance(self.Position, hurt.Position) <= 5.8f && Sees(self, hurt))
                {
                    Stop();
                    m_server.SendUseSkill(Connection, "skill.mend", hurt.Id, ++m_commands);

                    return;
                }

                WalkTo(hurt.Position, 4f);
                return;
            }

            // Against the boss, from outside its ash unless it has come for the Arcanist; against the rest, from where
            // it stands once within the bolts' reach.
            float distance = Distance(self.Position, target.Position);
            bool sees = Sees(self, target);
            if (target.Definition.IsBoss && target.Target != self.Id && (distance < 6.3f || distance > 7.8f || !sees))
            {
                WalkTo(ArcanistSpot(battle, self, target), 0.4f);
                return;
            }

            if (!sees || distance > 7.8f)
            {
                WalkTo(target.Position, sees ? 7.6f : 0.5f);
                return;
            }

            Stop();
            if (!IsReady(self, "skill.arcane_bolt"))
            {
                return;
            }

            // Arcane Bolt strikes hardest for its time; Frost Lance freezes a second monster at the Arcanist's side,
            // which no boss allows.
            MonsterEntity? other = battle.Prey.FirstOrDefault(monster => monster != target
                && !monster.IsDead
                && !monster.Definition.IsBoss
                && !monster.HasAilment(Ailment.Freeze)
                && monster.Target == self.Id
                && Distance(self.Position, monster.Position) <= 7.8f
                && Sees(self, monster));
            if (other != null && IsReady(self, "skill.frost_lance"))
            {
                m_server.SendUseSkill(Connection, "skill.frost_lance", other.Id, ++m_commands);
            }
            else
            {
                m_server.SendUseSkill(Connection, "skill.arcane_bolt", target.Id, ++m_commands);
            }

            HasStruck = true;
        }

        private bool Sees(PlayerEntity self, WorldEntity other)
        {
            return QuarryOf(m_server).Definition.Navigation.HasLineOfSight(self.Position, other.Position);
        }

        // Within the bolts' 8 m and outside the boss's 6 m of ash, on the side of the party's Vanguard, within 6 m of
        // it for Mend.
        private WorldPosition ArcanistSpot(Battle battle, PlayerEntity self, MonsterEntity target)
        {
            PlayerEntity? tank = battle.Party.Where(member => member.Build == Vanguard && !member.Self.IsDead)
                .Select(member => member.Self)
                .FirstOrDefault();
            return Outside(target.Position, tank?.Position ?? self.Position, 6.9f);
        }

        // Out of the area by the shortest way that also keeps toward the caster's home, as a party keeps a boss in its
        // arena: stepping straight away each time would walk it down the road to its leash.
        private WorldPosition EscapeFrom(MonsterEntity caster, float distance)
        {
            NavigationGrid grid = QuarryOf(m_server).Definition.Navigation;
            WorldPosition self = Self.Position;
            WorldPosition best = Outside(caster.Position, self, distance);
            double bestCost = double.MaxValue;
            for (int step = 0; step < 16; step++)
            {
                double angle = step * Math.PI / 8.0;
                float x = caster.Position.X + distance * (float)Math.Cos(angle);
                float z = caster.Position.Z + distance * (float)Math.Sin(angle);
                if (!grid.CanOccupy(x, z) || !grid.TrySampleHeight(x, z, out float height))
                {
                    continue;
                }

                var point = new WorldPosition(x, height, z);
                double cost = Distance(self, point) + Distance(caster.Home, point);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = point;
                }
            }

            return best;
        }

        // The floor cell the given distance from the centre on the bearing to the given place, or the nearest bearing
        // with floor.
        private WorldPosition Outside(WorldPosition centre, WorldPosition toward, float distance)
        {
            NavigationGrid grid = QuarryOf(m_server).Definition.Navigation;
            double bearing = Math.Atan2(toward.Z - centre.Z, toward.X - centre.X);
            for (int turn = 0; turn < 16; turn++)
            {
                double angle = bearing + (turn % 2 == 0 ? 1 : -1) * ((turn + 1) / 2) * Math.PI / 8.0;
                float x = centre.X + distance * (float)Math.Cos(angle);
                float z = centre.Z + distance * (float)Math.Sin(angle);
                if (grid.CanOccupy(x, z) && grid.TrySampleHeight(x, z, out float height))
                {
                    return new WorldPosition(x, height, z);
                }
            }

            return toward;
        }

        private void Hold(MonsterEntity target)
        {
            if (Build == Vanguard && Distance(Self.Position, target.Position) > Self.AttackRange - 0.2f)
            {
                WalkTo(target.Position, Self.AttackRange - 0.2f);
            }
            else
            {
                Stop();
            }
        }

        private bool TryDrink(PlayerEntity self)
        {
            if ((double)self.CurrentHealth / self.MaxHealth < 0.4 && TryUse(HealthPotion, true))
            {
                return true;
            }

            return (double)self.CurrentSpirit / self.MaxSpirit < 0.15 && TryUse(ManaPotion, true);
        }

        private bool TryCure(PlayerEntity self)
        {
            if (!self.HasAilment(Ailment.Silence) && !self.HasAilment(Ailment.Blind))
            {
                return false;
            }

            if (TryUse(Remedy, false) || TryUse(GrandRemedy, false))
            {
                m_cures++;
                return true;
            }

            return false;
        }

        private bool TryUse(string item, bool isPotion)
        {
            CharacterSession character = Character;
            if (character.Operation != null || Self.IsHeld)
            {
                return false;
            }

            long row = character.Inventory.Rows
                .Where(entry => entry.Item.Value == item && entry.Slot == EquipmentSlot.None && entry.Quantity > 0)
                .Select(entry => entry.InventoryItem)
                .FirstOrDefault();
            if (row == 0 || (item == WaterConverter && HasConverted()))
            {
                return false;
            }

            m_server.SendUseItem(Connection, row, ++m_commands);
            if (isPotion)
            {
                m_potions++;
            }

            return true;
        }

        private bool HasConverted()
        {
            return Self.StatusEffects.Any(effect => effect.Status.Value == "status.water_weapon");
        }

        private bool TryBuff(PlayerEntity self, string skill, string status)
        {
            if (self.StatusEffects.Any(effect => effect.Status.Value == status) || !IsReady(self, skill))
            {
                return false;
            }

            m_server.SendUseSkill(Connection, skill, self.Id, ++m_commands);
            return true;
        }

        private bool IsReady(PlayerEntity self, string skill)
        {
            var id = new SkillDefinitionId(skill);
            if (!self.Skills.TryGetValue(id, out int level))
            {
                return false;
            }

            long now = NowMs(m_server);
            return !self.Combat.IsCasting
                && !self.Combat.IsSwinging
                && now >= self.Combat.DelayEndsMs
                && now >= self.Combat.CooldownEndMs(id)
                && self.CurrentSpirit >= m_server.Content.Skills[id].ValuesAt(level).SpCost
                && !self.IsHeld
                && !self.HasAilment(Ailment.Silence);
        }

        // A step toward the goal along the grid's path, found again when the goal moves.
        private void WalkTo(WorldPosition goal, float tolerance)
        {
            PlayerEntity self = Self;
            if (Distance(self.Position, goal) <= tolerance || self.IsHeld)
            {
                Stop();
                return;
            }

            if (m_pathGoal == null || Distance(m_pathGoal.Value, goal) > 0.5f || m_path.Count == 0)
            {
                if (!m_pathfinder.TryFindPath(self.Position, goal, MaxExpandedNodes, m_path))
                {
                    m_path.Clear();
                    m_path.Add(goal);
                }

                m_pathGoal = goal;
            }

            while (m_path.Count > 1 && Distance(self.Position, m_path[0]) < 0.3f)
            {
                m_path.RemoveAt(0);
            }

            WorldPosition next = m_path[0];
            float dx = next.X - self.Position.X;
            float dz = next.Z - self.Position.Z;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.05f)
            {
                return;
            }

            m_server.SendMove(Connection, ++m_moves, dx / length, dz / length);
            m_isMoving = true;
        }

        private void Stop()
        {
            if (m_isMoving)
            {
                m_server.SendStop(Connection, ++m_moves);
                m_isMoving = false;
            }

            m_path.Clear();
            m_pathGoal = null;
        }
    }

    // The fixture's own determinism: a seed fights the same fight again, to the tick.
    [Test]
    public void AFight_ReplaysWithItsSeed()
    {
        Outcome first = Fight(Enter(7, Basalt, 8f, Vanguard), OrdinaryLimitTicks);
        Outcome second = Fight(Enter(7, Basalt, 8f, Vanguard), OrdinaryLimitTicks);

        Assert.That(second.ToString(), Is.EqualTo(first.ToString()));
    }
}
}
