using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
public sealed class TickPipelineTests
{
    [Test]
    public void TickPhase_Values_FollowArchitectureOrder()
    {
        string[] expected =
        {
            "DrainCommands",
            "ApplyCommands",
            "Movement",
            "Combat",
            "MonsterAi",
            "FinalizeWorld",
            "BuildSnapshots",
            "SchedulePersistence",
        };

        Assert.That(Enum.GetNames(typeof(TickPhase)), Is.EqualTo(expected));
        Assert.That(Enum.GetValues(typeof(TickPhase)), Is.Ordered);
    }

    [Test]
    public void Execute_WithPhasesRegisteredOutOfOrder_RunsInArchitectureOrder()
    {
        List<string> log = new List<string>();
        TickPipeline pipeline = new TickPipeline(new ITickPhase[]
        {
            new RecordingPhase(TickPhase.BuildSnapshots, "snapshots", log),
            new RecordingPhase(TickPhase.Movement, "movement", log),
            new RecordingPhase(TickPhase.DrainCommands, "drain", log),
            new RecordingPhase(TickPhase.ApplyCommands, "apply", log),
        });

        pipeline.Execute(new TickContext(7, 0.05f));

        Assert.That(log, Is.EqualTo(new[] { "drain@7", "apply@7", "movement@7", "snapshots@7" }));
    }

    [Test]
    public void Execute_WithTwoSystemsInOnePhase_KeepsRegistrationOrder()
    {
        List<string> log = new List<string>();
        TickPipeline pipeline = new TickPipeline(new ITickPhase[]
        {
            new RecordingPhase(TickPhase.FinalizeWorld, "first", log),
            new RecordingPhase(TickPhase.DrainCommands, "drain", log),
            new RecordingPhase(TickPhase.FinalizeWorld, "second", log),
        });

        pipeline.Execute(new TickContext(1, 0.05f));

        Assert.That(log, Is.EqualTo(new[] { "drain@1", "first@1", "second@1" }));
    }

    [Test]
    public void Execute_WithNoPhases_DoesNothing()
    {
        TickPipeline pipeline = new TickPipeline(new ITickPhase[0]);
        Action execute = () => pipeline.Execute(new TickContext(1, 0.05f));

        Assert.That(execute, Throws.Nothing);
    }
}
}
