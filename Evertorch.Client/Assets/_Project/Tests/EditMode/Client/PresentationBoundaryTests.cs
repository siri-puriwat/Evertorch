using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Client animation never decides damage or attack readiness (Evertorch Spec §3.3; Milestone 3 verification). The
///     presentation code may read the client world and draw; it may not send, move, lock, or change what the client
///     believes. The gameplay code, in turn, never reads the presentation.
/// </summary>
[TestFixture]
public sealed class PresentationBoundaryTests
{
    private static readonly Regex Forbidden = new(
        @"ClientConnection|ICombatCommandSink|IMoveIntentSink|\bSend\w*\(|AutoAttackState|MovementController"
        + @"|LocalPlayerDriver|MovementPredictor|\.Predictor\b|\.IsLocked\b|RequestRespawn"
        + @"|\.On(Spawn|Despawn|Snapshot|TargetChanged|AttackStarted|Damage|EntityDied|EntityRevived|ItemDropped"
        + @"|CharacterHealth)\("
        + @"|\.(HealthPermille|StateFlags|CurrentHealth)\s*=[^=]");

    // UI may ask GameClient for anything a player can do, and read what it likes; it may not send, build a message,
    // drive movement, or change what the client believes (Coding Standards §3).
    private static readonly Regex UiForbidden = new(
        @"ICombatCommandSink|IMoveIntentSink|\.Send\w*\(|AutoAttackState|LocalPlayerDriver|PickupState"
        + @"|MoveIntentProducer|\bnew\s+(MoveIntent|ClientHello|EnterWorldRequest|MoveInput|StopMovement|TargetEntity"
        + @"|AttackEntity|CancelAction|Respawn|Logout|PickupItem|CreateCharacter|InventoryResyncRequest)\s*\("
        + @"|\.Connection\??\.(Connect|Disconnect|EnterWorld|CreateCharacter|Poll)\("
        + @"|\.Controller\??\.(SetManualDirection|TryMoveTo|Chase\w*|Cancel\w*|Tick)\("
        + @"|\.On(Spawn|Despawn|Snapshot|TargetChanged|AttackStarted|Damage|EntityDied|EntityRevived|CommandRejected"
        + @"|ItemDropped|ItemPickedUp|CharacterHealth)\("
        + @"|\.(Advance|CollectTargetCandidates|CollectDropCandidates)\("
        + @"|\.(Target|LastRejection|LocalHealth|LocalMaximumHealth|HealthPermille|StateFlags|CurrentHealth)\s*=[^=]");

    private static readonly Regex UsesPresentation = new(
        @"\b(CombatAnimation|CombatTimeline|CombatPresenter|HitMark|FloatingNumber|HealthBar|EntityView)\b"
        + @"|\bAnimator\b|AnimationEvent");

    private static string ScriptsFolder(string folder)
    {
        return Path.Combine(Application.dataPath, "_ProjectScripts.Evertorch.Client", folder);
    }

    private static string[] Offenders(string folder, Regex pattern)
    {
        return Directory.GetFiles(ScriptsFolder(folder), "*.cs", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadAllLines(file)
                .Select((line, index) => (file, line, index))
                .Where(entry => !entry.line.TrimStart().StartsWith("//") && pattern.IsMatch(entry.line))
                .Select(entry => $"{Path.GetFileName(entry.file)}:{entry.index + 1}: {entry.line.Trim()}"))
            .ToArray();
    }

    [Test]
    public void Gameplay_NeverReadsThePresentation()
    {
        Assert.That(Offenders("Gameplay", UsesPresentation), Is.Empty);
        Assert.That(Offenders("Networking", UsesPresentation), Is.Empty);
    }

    [Test]
    public void Presentation_NeverSendsMovesLocksOrChangesTheClientWorld()
    {
        Assert.That(Offenders("Presentation", Forbidden), Is.Empty);
    }

    [Test]
    public void TheScan_FindsWhatItLooksFor()
    {
        string[] probes =
        {
            "connection.SendAttack(target);", "m_world.OnDamage(damage);", "remote.HealthPermille = 0;",
            "controller.IsLocked = true;", "new AutoAttackState(world, controller, sink, 0.05);"
        };

        Assert.That(probes.Where(probe => !Forbidden.IsMatch(probe)), Is.Empty);
        Assert.That(Forbidden.IsMatch("if (remote.HealthPermille == 0)"), Is.False, "reading is allowed");
        Assert.That(UsesPresentation.IsMatch("m_presenter = new CombatPresenter(world);"), Is.True);
    }

    [Test]
    public void TheUiScan_FindsWhatItLooksFor()
    {
        string[] probes =
        {
            "connection.SendTarget(target);", "client.Connection.EnterWorld(entry.Character);",
            "var hello = new ClientHello(1, build, 0, token);", "world.OnCommandRejected(rejected);",
            "world.Target = entity;", "client.Controller.TryMoveTo(from, point);"
        };
        string[] allowed =
        {
            "client.EnterWorld(entry.Character);", "client.RequestRespawn();", "if (world.Target == entity)",
            "int rtt = client.Connection.RoundTripMilliseconds;"
        };

        Assert.That(probes.Where(probe => !UiForbidden.IsMatch(probe)), Is.Empty);
        Assert.That(allowed.Where(line => UiForbidden.IsMatch(line)), Is.Empty);
    }

    [Test]
    public void Ui_RequestsThroughTheClientButNeverSendsOrChangesTheClientWorld()
    {
        Assert.That(Offenders("UI", UiForbidden), Is.Empty);
    }
}
}
