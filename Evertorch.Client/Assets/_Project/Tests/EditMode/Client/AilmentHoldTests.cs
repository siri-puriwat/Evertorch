using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The local hold (Prototype Content §2; Gameplay Systems §9.1): a stun, a freeze, or sleep keeps the character
///     still and ends its cast in gameplay code, a silence ends the cast too, and a refused press says why.
/// </summary>
[TestFixture]
public sealed class AilmentHoldTests
{
    private static readonly SkillDefinitionId FirstAid = new("skill.first_aid");

    private static void Flag(ClientWorld world, uint tick, EntityStateFlags flags)
    {
        world.OnSnapshot(
            ClientWorldFixture.Snapshot(
                tick,
                0,
                new EntityState(
                    ClientWorldFixture.LocalEntity,
                    world.Predictor.Position,
                    new WorldDirection(0f, 1f),
                    0f,
                    0f,
                    0f,
                    flags)));
    }

    [TestCase(HeldAct.Move, EntityStateFlags.Frozen, "You can't move while frozen.")]
    [TestCase(HeldAct.UseItems, EntityStateFlags.Stunned, "You can't use items while stunned.")]
    [TestCase(HeldAct.UseSkills, EntityStateFlags.Silenced, "You can't use skills while silenced.")]
    [TestCase(HeldAct.Attack, EntityStateFlags.Asleep, "You can't attack while asleep.")]
    [TestCase(HeldAct.PickUp, EntityStateFlags.Frozen, "You can't pick things up while frozen.")]
    [TestCase(HeldAct.Talk, EntityStateFlags.Stunned | EntityStateFlags.Frozen, "You can't talk while stunned.")]
    [TestCase(HeldAct.UseSkills, EntityStateFlags.Asleep | EntityStateFlags.Silenced,
        "You can't use skills while asleep.")]
    public void Refusal_NamesTheActAndTheAilment(HeldAct act, EntityStateFlags flags, string words)
    {
        Assert.That(AilmentMessages.Refusal(flags, act), Is.EqualTo(words));
    }

    [TestCase(HeldAct.Move)]
    [TestCase(HeldAct.UseItems)]
    [TestCase(HeldAct.Attack)]
    public void Refusal_OfASilence_LeavesAllButSkills(HeldAct act)
    {
        Assert.That(
            AilmentMessages.Refusal(EntityStateFlags.Silenced | EntityStateFlags.Poisoned | EntityStateFlags.Blinded,
                act),
            Is.Null);
    }

    private sealed class Sink : IMoveIntentSink
    {
        public List<MoveIntent> Intents { get; } = new();

        public void Send(MoveIntent intent)
        {
            Intents.Add(intent);
        }
    }

    [Test]
    public void AHeldPlayer_TakesNoStep_AndWalksAgainOnceFree()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), ClientTestGrids.Center(2, 8));
        var controller = new MovementController(world.Grid);
        var sink = new Sink();
        var driver = new LocalPlayerDriver(controller, new MoveIntentProducer(), world, sink);
        Flag(world, 1, EntityStateFlags.Stunned);
        WorldPosition start = world.Predictor.Position;

        controller.SetManualDirection(1f, 0f);
        for (uint tick = 1; tick <= 5; tick++)
        {
            driver.Tick(tick);
        }

        WorldPosition held = world.Predictor.Position;
        int sentHeld = sink.Intents.Count;
        Flag(world, 2, EntityStateFlags.None);
        driver.Tick(6);

        Assert.That(held, Is.EqualTo(start), "stunned, no step");
        Assert.That(sentHeld, Is.Zero, "and no intent");
        Assert.That(world.Predictor.Position, Is.Not.EqualTo(start), "free, it walks");
    }

    [Test]
    public void AHold_EndsTheLocalCast_AndASilenceToo_AndPoisonDoesNot()
    {
        ClientWorld world = ClientWorldFixture.Create(ClientTestGrids.CreateYard(), ClientTestGrids.Center(2, 8));
        int ended = 0;
        world.LocalCastEnded += () => ended++;

        world.OnSkillCastStarted(new SkillCastStarted(ClientWorldFixture.LocalEntity, FirstAid, default, 0, 1500));
        Flag(world, 1, EntityStateFlags.Poisoned);
        bool isLockedPoisoned = world.ActionLock.IsCastLocked;
        Flag(world, 2, EntityStateFlags.Poisoned | EntityStateFlags.Frozen);
        bool isLockedFrozen = world.ActionLock.IsCastLocked;
        bool isHeld = world.IsHeld;
        Flag(world, 3, EntityStateFlags.None);
        world.OnSkillCastStarted(new SkillCastStarted(ClientWorldFixture.LocalEntity, FirstAid, default, 3, 1500));
        Flag(world, 4, EntityStateFlags.Silenced);

        Assert.That(isLockedPoisoned, Is.True);
        Assert.That((isLockedFrozen, isHeld), Is.EqualTo((false, true)), "frozen");
        Assert.That(world.ActionLock.IsCastLocked, Is.False, "silenced");
        Assert.That((world.IsHeld, world.IsSilenced), Is.EqualTo((false, true)));
        Assert.That(ended, Is.EqualTo(2));
    }
}
}
