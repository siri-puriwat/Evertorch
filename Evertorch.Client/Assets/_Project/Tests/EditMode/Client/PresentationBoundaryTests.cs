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
}
}
