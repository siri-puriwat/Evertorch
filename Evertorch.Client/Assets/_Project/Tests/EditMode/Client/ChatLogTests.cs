using System.Linq;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The chat log (Prototype Content §2): its words for each channel, its 50 lines, and who a reply answers.
/// </summary>
[TestFixture]
public sealed class ChatLogTests
{
    [Test]
    public void Add_PastFiftyLines_DropsTheOldest_AndSaysSoEachTime()
    {
        var log = new ChatLog();
        int changes = 0;
        log.Changed += () => changes++;

        for (int index = 1; index <= ChatLog.Capacity + 3; index++)
        {
            log.AddSystem($"line {index}");
        }

        Assert.That(ChatLog.Capacity, Is.EqualTo(50));
        Assert.That(log.Lines, Has.Count.EqualTo(ChatLog.Capacity));
        Assert.That(log.Lines[0].Text, Is.EqualTo("line 4"));
        Assert.That(log.Lines[log.Lines.Count - 1].Text, Is.EqualTo("line 53"));
        Assert.That(changes, Is.EqualTo(ChatLog.Capacity + 3));
    }

    [Test]
    public void Add_WordsEachChannel_AsTheLayoutSays()
    {
        var log = new ChatLog();

        log.Add(new ChatReceived(ChatChannel.Nearby, new EntityId(7), "Anna", "hi"));
        log.Add(new ChatReceived(ChatChannel.Party, default, "Anna", "hi"));
        log.Add(new ChatReceived(ChatChannel.Whisper, default, "Anna", "hi"));
        log.Add(new ChatReceived(ChatChannel.WhisperSent, default, "Bobby", "hi"));
        log.AddSystem("Bobby is not online.");

        Assert.That(
            log.Lines.Select(line => (line.Kind, line.Text)),
            Is.EqualTo(new[]
            {
                (ChatLineKind.Nearby, "Anna: hi"), (ChatLineKind.Party, "[Party] Anna: hi"),
                (ChatLineKind.WhisperReceived, "From Anna: hi"), (ChatLineKind.WhisperSent, "To Bobby: hi"),
                (ChatLineKind.System, "Bobby is not online.")
            }));
    }

    [Test]
    public void LastWhisperer_IsWhoeverLastWhisperedToThisClient()
    {
        var log = new ChatLog();

        string? before = log.LastWhisperer;
        log.Add(new ChatReceived(ChatChannel.Whisper, default, "Anna", "psst"));
        log.Add(new ChatReceived(ChatChannel.WhisperSent, default, "Bobby", "and you?"));
        log.Add(new ChatReceived(ChatChannel.Nearby, new EntityId(9), "Cora", "hello"));

        Assert.That(before, Is.Null);
        Assert.That(log.LastWhisperer, Is.EqualTo("Anna"), "a whisper sent or a line nearby changes nothing");
    }
}
}
