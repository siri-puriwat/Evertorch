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
        @"ClientConnection|ICombatCommandSink|IMoveIntentSink|ISkillCommandSink|IItemCommandSink|\bSend\w*\("
        + @"|AutoAttackState|PickupState|SkillState|InventoryActions\.Press\b|MovementController|LocalPlayerDriver"
        + @"|MovementPredictor|\.Predictor\b|\.IsLocked\b|RequestRespawn|UseSkillSlot|PressInventoryRow|\.ActionLock\b"
        + @"|\.On(Spawn|Despawn|Snapshot|TargetChanged|AttackStarted|Damage|EntityDied|EntityRevived|CommandRejected"
        + @"|ItemDropped|ItemPickedUp|CharacterHealth|CharacterProgress|SkillCastStarted|SkillResolved|SkillList"
        + @"|StatusEffects|LocalCancel|Changed)\("
        + @"|\.(Advance|CollectTargetCandidates|CollectDropCandidates)\("
        + @"|\.(HealthPermille|StateFlags|CurrentHealth|Target|LastRejection|LocalHealth|LocalMaximumHealth|IsDead"
        + @"|LocalSpirit|LocalMaximumSpirit|Level|Experience|ExperienceToNextLevel)\s*=(?![=>])");

    // UI may ask GameClient for anything a player can do, and read what it likes; it may not send, build a message,
    // drive movement or the client's ticks, or change what the client believes (Coding Standards §3). A method whose
    // name only one client type has is matched on any receiver, so a local copy of the controller or the predictor
    // cannot hide the call.
    private static readonly Regex UiForbidden = new(
        @"ICombatCommandSink|IMoveIntentSink|ISkillCommandSink|IItemCommandSink|\.Send\w*\(|AutoAttackState"
        + @"|LocalPlayerDriver|PickupState|SkillState|InventoryActions\.Press\b"
        + @"|\.ActionLock\b|MoveIntentProducer|\bnew\s+(MoveIntent|ClientHello|EnterWorldRequest|MoveInput|StopMovement"
        + @"|TargetEntity|AttackEntity|CancelAction|UseSkill|Respawn|Logout|PickupItem|CreateCharacter"
        + @"|InventoryResyncRequest|EquipItem|UnequipItem|UseItem)\s*\("
        + @"|\.Connection\??\.(Connect|Disconnect|EnterWorld|CreateCharacter|Poll)\("
        + @"|\.Controller\??\.(Cancel\w*|Tick)\("
        + @"|\.(SetManualDirection|TryMoveTo|Chase\w*|StopChase|CancelPath|NextTick|Reconcile|Teleport"
        + @"|ForgetPendingStops|OnTick|OnCorrected|Observe)\("
        + @"|[Pp]redictor\??\.Apply\(|\.Buffer\.(Add|Clear)\("
        + @"|\.On(Spawn|Despawn|Snapshot|TargetChanged|AttackStarted|Damage|EntityDied|EntityRevived|CommandRejected"
        + @"|ItemDropped|ItemPickedUp|CharacterHealth|CharacterProgress|SkillCastStarted|SkillResolved|SkillList"
        + @"|StatusEffects|LocalCancel|Changed)\("
        + @"|\.(Advance|CollectTargetCandidates|CollectDropCandidates)\("
        + @"|\.(Target|LastRejection|LocalHealth|LocalMaximumHealth|HealthPermille|StateFlags|CurrentHealth|IsLocked"
        + @"|IsDead|LocalSpirit|LocalMaximumSpirit|Level|Experience|ExperienceToNextLevel)\s*=(?![=>])");

    private static readonly Regex UsesPresentation = new(
        @"\b(CombatAnimation|CombatTimeline|CombatPresenter|HitMark|FloatingNumber|HealthBar|CastBar|EntityView)\b"
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
            "controller.IsLocked = true;", "new AutoAttackState(world, controller, sink, 0.05);",
            "var skill = new SkillState(world, controller, sink);", "m_world.OnStatusEffects(effects);",
            "m_client.UseSkillSlot(1);", "client.PressInventoryRow(row);",
            "InventoryActions.Press(sink, row, ItemType.Consumable);", "IItemCommandSink items = m_items;",
            "ISkillCommandSink skills = m_skills;", "m_world.OnCharacterProgress(progress);",
            "world.Inventory.OnChanged(change);", "m_world.OnItemPickedUp(pickedUp);",
            "m_world.OnCommandRejected(rejected);", "m_world.Advance(0.05f);",
            "var pickup = new PickupState(world, controller, sink);", "world.Level =", "m_world.Target = entity;"
        };
        string[] allowed =
        {
            "if (remote.HealthPermille == 0)", "m_world.SkillCastStartedReceived += OnSkillCastStarted;",
            "EntityView? to = flight.Target == m_world.LocalEntity", "double now = m_world.RemoteRenderTime;",
            "bool isLocal = resolved.Target == m_world.LocalEntity;"
        };

        Assert.That(probes.Where(probe => !Forbidden.IsMatch(probe)), Is.Empty);
        Assert.That(allowed.Where(line => Forbidden.IsMatch(line)), Is.Empty, "reading is allowed");
        Assert.That(UsesPresentation.IsMatch("m_presenter = new CombatPresenter(world);"), Is.True);
        Assert.That(UsesPresentation.IsMatch("CastBar bar = CastBar.Create(back, fill);"), Is.True);
    }

    [Test]
    public void TheUiScan_FindsWhatItLooksFor()
    {
        string[] probes =
        {
            "connection.SendTarget(target);", "client.Connection.EnterWorld(entry.Character);",
            "var hello = new ClientHello(1, build, 0, token);", "world.OnCommandRejected(rejected);",
            "world.Target = entity;", "client.Controller.TryMoveTo(from, point);", "controller.StopChase();",
            "controller.CancelPath();", "m_client.Clock?.NextTick();", "world.Inventory.OnChanged(change);",
            "predictor.Teleport(position, facing);", "smoother.OnCorrected(before, after);",
            "world.ServerTime.Observe(now);", "remote.Buffer.Clear();", "m_predictor.Apply(intent);",
            "controller.IsDead = true;", "world.Target =", "world.OnCharacterProgress(progress);",
            "world.Level = 3;", "world.LocalSpirit = 0;", "world.OnSkillResolved(resolved);",
            "m_world.OnLocalCancel();", "var use = new UseSkill(skill, target, 3);",
            "SkillState? skill = m_client.Skill;", "world.OnStatusEffects(effects);",
            "InventoryActions.Press(commands, row, item.Type);", "var equip = new EquipItem(row, 3);",
            "var off = new UnequipItem(EquipmentSlot.Weapon, 4);", "var drink = new UseItem(row, 5);",
            "IItemCommandSink items = client;", "ISkillCommandSink skills = client;"
        };
        string[] allowed =
        {
            "client.EnterWorld(entry.Character);", "client.RequestRespawn();", "if (world.Target == entity)",
            "int rtt = client.Connection.RoundTripMilliseconds;", "WorldPosition self = world.Predictor.Position;",
            "if (m_client.Clock != null && m_client.Clock.SkippedTicks > 0)", "bool isLocked = controller.IsLocked;",
            "if (target.IsDead == wasDead)", "world.Inventory.Changed += Refresh;", "m_lines.Clear();",
            "world.LeveledUp += OnLeveledUp;", "ShowCharacter(played.Value.Name, world.Level);",
            "m_client.UseSkillSlot(number);", "Show(slot, world.CooldownRemaining(slot.Skill));",
            "int seconds = (int)Math.Ceiling(world.StatusRemaining(effect.Status));",
            "int held = world != null ? InventoryActions.CountOf(world.Inventory.Rows, slot.Item) : 0;",
            "if (item != null && InventoryActions.HasAction(item.Type))",
            "GameObject button = Ui.CreateButton(text, m_rows!, () => client.PressInventoryRow(row));",
            "SkillSlots.TryGetItem(number, out ItemDefinitionId item);"
        };
        string[] lockProbes = { "world.ActionLock.LockForSwing(4);", "bool held = m_world.ActionLock.IsCastLocked;" };

        Assert.That(probes.Where(probe => !UiForbidden.IsMatch(probe)), Is.Empty);
        Assert.That(allowed.Where(line => UiForbidden.IsMatch(line)), Is.Empty);
        Assert.That(lockProbes.Where(probe => !UiForbidden.IsMatch(probe) || !Forbidden.IsMatch(probe)), Is.Empty);
    }

    [Test]
    public void Ui_RequestsThroughTheClientButNeverSendsOrChangesTheClientWorld()
    {
        Assert.That(Offenders("UI", UiForbidden), Is.Empty);
    }
}
}
