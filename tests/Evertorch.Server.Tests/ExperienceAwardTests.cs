using System.Linq;
using Evertorch.Protocol;
using Evertorch.Rules;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A dead monster's experience, shared by damage among the characters that may still receive it (Gameplay Systems
///     §2.1 and the vectors of the experience research note). The training slime gives 10.
/// </summary>
[TestFixture]
public sealed class ExperienceAwardTests
{
    private const long SlimeExperience = 10;

    private static MonsterEntity SlimeOf(TestServer server)
    {
        MonsterEntity slime = server.MonstersNear(server.World.Maps.Single().Definition.SpawnPosition).First();
        Assert.That(slime.Definition.BaseExperience, Is.EqualTo(SlimeExperience));
        return slime;
    }

    private static void Kill(TestServer server, WorldEntity entity)
    {
        server.Combat.Kill(server.World.Maps.Single(), entity, null, server.CurrentTick);
    }

    private static CharacterSession CharacterOf(TestServer server, ConnectionId connection)
    {
        bool isInWorld = server.Sessions.TryGetCharacter(
            server.PlayerOf(connection).Character,
            out CharacterSession? character);
        Assert.That(isInWorld, Is.True);
        return character!;
    }

    [TestCase(30, 20, 6, 4)]
    [TestCase(1, 99, 1, 9)]
    [TestCase(45, 20, 6, 3)]
    [TestCase(1, 1, 5, 5)]
    public void Kill_SharesTheExperienceByDamage(int firstDamage, int secondDamage, long firstShare, long secondShare)
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false);
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);
        MonsterEntity slime = SlimeOf(server);
        slime.LogDamage(server.PlayerOf(first).Character, firstDamage);
        slime.LogDamage(server.PlayerOf(second).Character, secondDamage);

        Kill(server, slime);

        Assert.That(server.PlayerOf(first).Experience, Is.EqualTo(firstShare));
        Assert.That(server.PlayerOf(second).Experience, Is.EqualTo(secondShare));
    }

    /// <summary>
    ///     Draws the highest number every time: never dodged, never a critical, and always a miss.
    /// </summary>
    private sealed class HighestRandom : IRandomSource
    {
        public int Next(int exclusiveMax)
        {
            return exclusiveMax - 1;
        }
    }

    private static CharacterProgress[] ProgressSentTo(TestServer server, ConnectionId connection)
    {
        return server.Transport.ControlSentTo(connection)
            .Where(message => message.Opcode == MessageOpcode.CharacterProgress)
            .Select(message => CharacterProgress.TryRead(message.Payload, out CharacterProgress read) ? read : default)
            .ToArray();
    }

    [Test]
    public void Attack_LogsTheWholeRoll_OverkillIncluded()
    {
        var rig = new CombatRig(combatRandom: new SureHitRandom());
        rig.Slime.CurrentHealth = 1;

        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(1);
        rig.Server.TickUntil(() => rig.Slime.IsDead);

        uint rolled = rig.Damages().Single().Amount;
        Assert.That(rolled, Is.GreaterThan(1u), "the blow rolled more than the slime had left");
        Assert.That(rig.Slime.DamageLog.Single().Character, Is.EqualTo(rig.Entity.Character));
        Assert.That(rig.Slime.DamageLog.Single().Damage, Is.EqualTo(rolled));
        Assert.That(rig.Entity.Experience, Is.EqualTo(SlimeExperience), "alone, the whole reward");
    }

    [Test]
    public void Attack_ThatMisses_LogsNothing()
    {
        var rig = new CombatRig(combatRandom: new HighestRandom());

        rig.Attack(rig.Slime.Id);
        rig.TickUntilStarts(2);
        rig.Server.Tick(40);

        Assert.That(rig.Damages().Select(damage => damage.Result), Has.All.EqualTo(CombatResult.Miss));
        Assert.That(rig.Slime.DamageLog, Is.Empty);
    }

    [Test]
    public void Kill_LogsNoLevelUpAndSendsNothing_WhenTheShareStaysBelowTheNextLevel()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false);
        ConnectionId first = server.EnterWorld(1);
        MonsterEntity slime = SlimeOf(server);
        slime.LogDamage(server.PlayerOf(first).Character, 50);
        server.Transport.ClearSent();

        Kill(server, slime);

        Assert.That(server.PlayerOf(first).Level, Is.EqualTo(1));
        Assert.That(server.PlayerOf(first).Experience, Is.EqualTo(SlimeExperience));
        Assert.That(
            server.Transport.ControlOpcodesSentTo(first),
            Has.None.EqualTo(MessageOpcode.CharacterHealth));
        Assert.That(server.ProgressionLog.Entries, Is.Empty);
    }

    [Test]
    public void Kill_SendsEachOwnerItsOwnProgressAndNoOneElses()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false);
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);
        MonsterEntity slime = SlimeOf(server);
        slime.LogDamage(server.PlayerOf(first).Character, 30);
        slime.LogDamage(server.PlayerOf(second).Character, 20);
        server.Transport.ClearSent();

        Kill(server, slime);

        CharacterProgress mine = ProgressSentTo(server, first).Single();
        CharacterProgress theirs = ProgressSentTo(server, second).Single();
        Assert.That((mine.Level, mine.Experience, mine.ExperienceToNextLevel), Is.EqualTo((1, 6UL, 30UL)));
        Assert.That((theirs.Level, theirs.Experience, theirs.ExperienceToNextLevel), Is.EqualTo((1, 4UL, 30UL)));
    }

    [Test]
    public void Kill_WhenACharacterHasLeftTheWorld_GivesTheOthersOnlyTheirShare()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false);
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);
        MonsterEntity slime = SlimeOf(server);
        PlayerEntity gone = server.PlayerOf(first);
        slime.LogDamage(gone.Character, 30);
        slime.LogDamage(server.PlayerOf(second).Character, 20);
        server.Disconnect(first);
        server.Tick();
        Assert.That(server.Sessions.TryGetCharacter(gone.Character, out _), Is.False);

        Kill(server, slime);

        Assert.That(gone.Experience, Is.Zero);
        Assert.That(server.PlayerOf(second).Experience, Is.EqualTo(4));
    }

    [Test]
    public void Kill_WhenACharacterIsDead_GivesItNothingAndTheOthersOnlyTheirShare()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false);
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);
        MonsterEntity slime = SlimeOf(server);
        slime.LogDamage(server.PlayerOf(first).Character, 30);
        slime.LogDamage(server.PlayerOf(second).Character, 20);
        Kill(server, server.PlayerOf(first));

        Kill(server, slime);

        Assert.That(server.PlayerOf(first).Experience, Is.Zero);
        Assert.That(server.PlayerOf(second).Experience, Is.EqualTo(4));
    }

    [Test]
    public void Kill_WhenACharacterIsExpelled_GivesItNothing()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false);
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);
        MonsterEntity slime = SlimeOf(server);
        slime.LogDamage(server.PlayerOf(first).Character, 30);
        slime.LogDamage(server.PlayerOf(second).Character, 20);
        CharacterOf(server, first).IsExpelled = true;

        Kill(server, slime);

        Assert.That(server.PlayerOf(first).Experience, Is.Zero);
        Assert.That(server.PlayerOf(second).Experience, Is.EqualTo(4));
    }

    [Test]
    public void Kill_WhenACharacterIsInItsGracePeriod_StillShares()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false, reconnectGraceMs: 60_000);
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);
        MonsterEntity slime = SlimeOf(server);
        PlayerEntity retained = server.PlayerOf(first);
        slime.LogDamage(retained.Character, 30);
        slime.LogDamage(server.PlayerOf(second).Character, 20);
        server.Disconnect(first);
        server.Tick();
        Assert.That(retained.Owner, Is.EqualTo(default(ConnectionId)), "retained, with no connection");

        server.Transport.ClearSent();

        Kill(server, slime);

        Assert.That(retained.Experience, Is.EqualTo(6));
        Assert.That(server.PlayerOf(second).Experience, Is.EqualTo(4));
        Assert.That(server.Transport.ControlSentTo(first), Is.Empty, "no connection to tell");
    }

    [Test]
    public void Kill_WhenACharacterIsLoggingOut_GivesItNothing()
    {
        var server = new TestServer(withMonsters: true, withMonsterAi: false);
        ConnectionId first = server.EnterWorld(1);
        ConnectionId second = server.EnterWorld(2);
        MonsterEntity slime = SlimeOf(server);
        slime.LogDamage(server.PlayerOf(first).Character, 30);
        slime.LogDamage(server.PlayerOf(second).Character, 20);
        PlayerEntity leaving = server.PlayerOf(first);
        server.RunsPersistence = false;
        server.SendLogout(first, 1);
        server.Tick();
        Assert.That(CharacterOf(server, first).IsLoggingOut, Is.True);

        Kill(server, slime);

        Assert.That(leaving.Experience, Is.Zero);
        Assert.That(server.PlayerOf(second).Experience, Is.EqualTo(4));
    }
}
}
