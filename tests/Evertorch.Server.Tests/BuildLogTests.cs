using System.Linq;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
/// <summary>
///     The build's log events (System Architecture §10): <c>StatRaised</c> (1012), <c>SkillLearned</c> (1013), and
///     <c>BuildReset</c> (1014) name the character, the connection, and what changed, at Information, and nothing of the
///     account, its sign-in, or its token.
/// </summary>
[TestFixture]
public sealed class BuildLogTests
{
    private const string Identity = "dev:secret-build-log";

    [Test]
    public void BuildEvents_NameTheCharacterTheConnectionAndTheChange_AndNothingElse()
    {
        var server = new TestServer(withNpcs: true, withAdventurerBuild: false);
        ConnectionId player = server.EnterWorldAs("secret-build-log", "BuildLog");
        PlayerEntity entity = server.PlayerOf(player);
        entity.Level = 3;
        entity.JobLevel = 2;
        NpcEntity guildmaster = server.NpcOf("npc.guildmaster");
        server.Place(player, guildmaster.Position.X + 2f, guildmaster.Position.Z);
        server.Tick(2);

        server.SendAllocateStat(player, PrimaryStat.Luk, 1, 1);
        server.SendLearnSkill(player, "skill.strike", 2);
        server.Tick();
        server.SendResetBuild(player, guildmaster.Id, 3);
        server.Tick();

        var entries =
            server.BuildsLog.Entries.ToList();
        Assert.That(
            entries.Select(entry => (entry.Level, entry.EventId.Id, entry.EventId.Name)),
            Is.EqualTo(
                new[]
                {
                    (LogLevel.Information, 1012, "StatRaised"), (LogLevel.Information, 1013, "SkillLearned"),
                    (LogLevel.Information, 1014, "BuildReset")
                }));
        Assert.That(
            entries.Select(entry => entry.Fields.Keys.Where(key => key != "{OriginalFormat}").OrderBy(key => key)),
            Is.EqualTo(
                new[]
                {
                    new[] { "Character", "Connection", "Previous", "Stat", "Value" },
                    new[] { "Character", "Connection", "Level", "Skill" },
                    new[] { "Character", "Connection", "Reason" }
                }));
        long account = server.SessionOf(player).Account!.Value.Value;
        Assert.That(
            entries.Select(entry => entry.Message),
            Has.None.Contains("secret").And.None.Contains(Identity).And.None.Contains($"account {account}"));
    }
}
}
