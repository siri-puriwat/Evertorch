using System.Collections.Generic;
using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Learned skills and their levels in play (Gameplay Systems §9, §9.1): a skill is used only once learned, each cast
///     uses the values of the level it began at, and the owner's skill list names the learned skills alone.
/// </summary>
[TestFixture]
public sealed class SkillLevelTests
{
    private const string Strike = "skill.strike";
    private const string FirstAid = "skill.first_aid";
    private const string Focus = "skill.focus";

    private static CommandRejectionReason[] Rejections(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player)
            .Where(message => message.Opcode == MessageOpcode.CommandRejected)
            .Select(message => CommandRejected.TryRead(message.Payload, out CommandRejected read) ? read.Reason : 0)
            .ToArray();
    }

    private static SkillList LastSkillList(TestServer server, ConnectionId player)
    {
        SkillList? list = null;
        foreach (InMemoryServerTransport.SentMessage message in server.Transport.ControlSentTo(player))
        {
            if (message.Opcode == MessageOpcode.SkillList && SkillList.TryRead(message.Payload, out SkillList? read))
            {
                list = read;
            }
        }

        return list ?? throw new AssertionException("no skill list was sent");
    }

    private static uint StrikeOnce(CombatRig rig)
    {
        rig.Server.SendUseSkill(rig.Player, Strike, rig.Slime.Id, 1);
        rig.Server.Tick(3);
        foreach (InMemoryServerTransport.SentMessage message in rig.Server.Transport.ControlSentTo(rig.Player))
        {
            if (message.Opcode == MessageOpcode.SkillResolved
                && SkillResolved.TryRead(message.Payload, out SkillResolved? resolved)
                && resolved!.Skill.Value == Strike)
            {
                return resolved.Amount;
            }
        }

        throw new AssertionException("Strike did not resolve");
    }

    private static SkillList? LastSkillListOrNull(TestServer server, ConnectionId player)
    {
        return server.Transport.ControlSentTo(player).Any(message => message.Opcode == MessageOpcode.SkillList)
            ? LastSkillList(server, player)
            : null;
    }

    // The level a cast began at decides its values, whatever is learned before it resolves (Gameplay Systems §9).
    [Test]
    public void Cast_LearnedHigherBeforeItResolves_ResolvesAtTheLevelItBeganAt()
    {
        var rig = new CombatRig();
        PlayerEntity player = rig.Entity;
        player.SetSkillLevel(new SkillDefinitionId(Focus), 1);

        CastRefusal refusal = rig.Server.Combat.TryBeginCast(
            rig.Map,
            player,
            new SkillDefinitionId(Focus),
            default,
            rig.Server.CurrentTick);
        player.SetSkillLevel(new SkillDefinitionId(Focus), 3);
        rig.Server.Tick();

        Assert.That(refusal, Is.EqualTo(CastRefusal.None));
        Assert.That(player.StatusEffects.Single().StatPercent, Is.EqualTo(new StatPercentages(0, 40, 0, 0, 40, 0)));
    }

    [Test]
    public void SkillList_NamesTheLearnedSkillsAlone_InTheTreesOrder_AtTheirLearnedLevelsValues()
    {
        var server = new TestServer(withAdventurerBuild: false);
        ConnectionId player = server.Connect();
        server.SignInWithCharacter(player, 1);
        server.Store.Edit(1, jobLevel: 5, skills: new Dictionary<string, int> { [Focus] = 1, [Strike] = 3 });
        server.SendEnterWorld(player, 1);
        server.TickUntil(() => server.SessionOf(player).State == SessionState.InWorld);
        server.Tick(2);

        SkillList list = LastSkillList(server, player);

        Assert.That(
            list.Skills.Select(entry => (entry.Skill.Value, entry.SpCost, entry.CooldownMs)),
            Is.EqualTo(new[] { (Strike, 10u, 2000u), (Focus, 15u, 0u) }),
            "Strike 3 costs 10 SP; First Aid, not learned, is not listed");
    }

    [Test]
    public void Strike_AtAHigherLevel_HitsHarderByItsRatio()
    {
        var levelOne = new CombatRig(combatRandom: new SureHitRandom());
        var levelThree = new CombatRig(combatRandom: new SureHitRandom());
        levelThree.Entity.SetSkillLevel(new SkillDefinitionId(Strike), 3);

        uint first = StrikeOnce(levelOne);
        uint third = StrikeOnce(levelThree);

        Assert.That(third, Is.GreaterThan(first), $"160 % against 130 %: {third} against {first}");
    }

    [Test]
    public void UseSkill_NotLearned_IsRefusedAsNotAllowedNow_AndStartsNothing()
    {
        var server = new TestServer(withAdventurerBuild: false);
        ConnectionId player = server.EnterWorld(1);
        server.Transport.ClearSent();

        server.SendUseSkill(player, FirstAid, default, 1);
        server.Tick(3);

        Assert.That(Rejections(server, player), Is.EqualTo(new[] { CommandRejectionReason.NotAllowedNow }));
        Assert.That(server.PlayerOf(player).Combat.IsCasting, Is.False);
        Assert.That(LastSkillListOrNull(server, player), Is.Null, "nothing learned, nothing resent");
    }
}
}
