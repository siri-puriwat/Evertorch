using System;
using System.Collections.Generic;
using Evertorch.Game;
using Evertorch.Persistence;
using Evertorch.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     Brings characters into the world from storage and takes them out again with a checkpoint (Persistence §6,
///     §7). Tick thread only; <see cref="SessionManager" /> decides when.
/// </summary>
public sealed class CharacterLifetime
{
    private const int MillisecondsPerSecond = 1000;

    private static readonly Action<ILogger, string, long, long, Exception?> LogCheckpointFailed =
        LoggerMessage.Define<string, long, long>(
            LogLevel.Warning,
            new EventId(4006, "CheckpointFailed"),
            "Checkpoint {OperationId} of character {Character} on connection {Connection} could not be written; it "
            + "is retried once the database answers.");

    private static readonly Action<ILogger, long, long, JobDefinitionId, ItemDefinitionId, Exception?>
        LogWeaponNotWieldable = LoggerMessage.Define<long, long, JobDefinitionId, ItemDefinitionId>(
            LogLevel.Warning,
            new EventId(1016, "WeaponNotWieldable"),
            "Character {Character} on connection {Connection} entered as {Job} wearing {Item}, a weapon its job cannot "
            + "wield; it stays worn.");

    private readonly WorldSimulation m_world;
    private readonly CharacterBuilds m_builds;
    private readonly SessionRegistry m_sessions;
    private readonly PersistenceWorker m_persistence;
    private readonly TimeProvider m_time;
    private readonly ILogger<CharacterLifetime> m_logger;
    private readonly uint m_checkpointIntervalTicks;
    private readonly uint m_graceTicks;
    private readonly List<CharacterSession> m_expired = new();

    public CharacterLifetime(
        WorldSimulation world,
        CharacterBuilds builds,
        SessionRegistry sessions,
        PersistenceWorker persistence,
        TimeProvider time,
        IOptions<PersistenceOptions> persistenceOptions,
        IOptions<SessionOptions> sessionOptions,
        IOptions<SimulationOptions> simulation,
        ILogger<CharacterLifetime> logger)
    {
        m_world = world;
        m_builds = builds;
        m_sessions = sessions;
        m_persistence = persistence;
        m_time = time;
        m_logger = logger;
        long ticks = (long)persistenceOptions.Value.CheckpointIntervalMs * simulation.Value.TickRate /
            MillisecondsPerSecond;
        m_checkpointIntervalTicks = (uint)Math.Max(1L, ticks);
        m_graceTicks = (uint)((long)sessionOptions.Value.ReconnectGraceMs * simulation.Value.TickRate /
            MillisecondsPerSecond);
    }

    public uint CheckpointIntervalTicks => m_checkpointIntervalTicks;

    /// <summary>
    ///     Places <paramref name="stored" /> in the world under <paramref name="owner" />. Null, placing nothing, when
    ///     the content lacks one of its definitions; <paramref name="problem" /> names which.
    /// </summary>
    public CharacterSession? Spawn(StoredCharacter stored, ClientSession owner, uint tick, out string problem)
    {
        if (!m_world.TrySpawnPlayer(stored, owner.Connection, out PlayerEntity? player, out MapInstance? map,
                out problem))
        {
            return null;
        }

        // A build that spends more than its levels grant, from a hand-edited row or shortened content, starts again
        // from the job's start; the next checkpoint stores that (Persistence §6).
        m_builds.ResetIfOverspent(player!, owner.Connection);
        var character = new CharacterSession(
            new CharacterId(stored.Id),
            stored.Account,
            player!,
            map!,
            CharacterInventory.FromStored(stored),
            CharacterQuests.FromStored(stored.Quests))
        {
            Connection = owner,
            NextCheckpointTick = tick + m_checkpointIntervalTicks
        };
        LogUnwieldableWeapon(character);
        m_sessions.AddCharacter(character);
        return character;
    }

    // Only changed content or a hand-edited row leaves a weapon the job cannot wield; taking it off here would be an
    // inventory commit nobody asked for, so it stays worn and counts, and only a new equip is refused (Gameplay Systems
    // §11.1).
    private void LogUnwieldableWeapon(CharacterSession character)
    {
        PlayerEntity player = character.Player;
        long worn = character.Inventory.WornIn(EquipmentSlot.Weapon);
        if (worn == 0
            || !character.Inventory.TryGetRow(worn, out InventoryEntry row)
            || m_builds.CanWield(player, row.Item))
        {
            return;
        }

        LogWeaponNotWieldable(
            m_logger,
            character.Character.Value,
            character.Connection?.Connection.Value ?? 0,
            player.Job,
            row.Item,
            null);
    }

    /// <summary>
    ///     Hands the character's current job, map, position, HP, SP, level and experience, job level and job experience,
    ///     primary statistics, learned skills, and active quests to the writer, replacing any checkpoint of it still
    ///     waiting. <paramref name="onComplete" /> runs on the tick thread when it is
    ///     written or found unwritable.
    /// </summary>
    public PersistenceJob QueueCheckpoint(CharacterSession character, Action<PersistenceOutcome>? onComplete = null)
    {
        PlayerEntity player = character.Player;
        long id = character.Character.Value;
        var checkpoint = new CharacterCheckpoint(
            id,
            player.Job.Value,
            character.Map.Definition.Id,
            player.Position,
            player.IsDead ? 0 : player.CurrentHealth,
            player.CurrentSpirit,
            player.Level,
            player.Experience,
            m_time.GetUtcNow().UtcDateTime,
            character.Quests.ToCheckpoint(),
            character.Operation?.Kind == InventoryOperationKind.QuestReward,
            player.JobLevel,
            player.JobExperience,
            player.Primary,
            StoredSkillsOf(player));
        ConnectionId connection = character.Connection?.Connection ?? default;
        PersistenceJob<bool>? job = null;
        job = new PersistenceJob<bool>(
            "checkpoint",
            connection,
            id,
            async (store, cancellation) =>
            {
                await store.SaveCheckpointAsync(checkpoint, cancellation).ConfigureAwait(false);
                return true;
            },
            (outcome, _) =>
            {
                if (outcome != PersistenceOutcome.Succeeded)
                {
                    LogCheckpointFailed(m_logger, job!.OperationId, id, connection.Value, null);
                }

                onComplete?.Invoke(outcome);
            });
        m_persistence.QueueCheckpoint(job);
        return job;
    }

    private static List<StoredSkill> StoredSkillsOf(PlayerEntity player)
    {
        var skills = new List<StoredSkill>(player.Skills.Count);
        foreach (KeyValuePair<SkillDefinitionId, int> skill in player.Skills)
        {
            skills.Add(new StoredSkill(skill.Key.Value, skill.Value));
        }

        return skills;
    }

    /// <summary>
    ///     Takes the character out of the world after queueing its final checkpoint.
    /// </summary>
    public void CheckpointAndRemove(CharacterSession character)
    {
        // An inventory operation in flight still needs its character when its result comes back (Persistence §7).
        if (character.Operation != null)
        {
            Release(character);
            character.GraceEndsTick = null;
            character.IsRemovalDeferred = true;
            return;
        }

        QueueCheckpoint(character);
        Remove(character);
    }

    /// <summary>
    ///     The character's inventory operation settled; a removal that waited for it happens now.
    /// </summary>
    public void OnOperationSettled(CharacterSession character)
    {
        if (character.IsRemovalDeferred)
        {
            character.IsRemovalDeferred = false;
            CheckpointAndRemove(character);
        }
    }

    /// <summary>
    ///     Takes the character out of the world without a checkpoint; the caller has written one.
    /// </summary>
    // No connection controls the character any more: it stops, drops its target and auto-attack (no client can
    // be told of them), and nothing is sent to it until a connection attaches.
    private static void Release(CharacterSession character)
    {
        if (character.Connection != null)
        {
            character.Connection.Character = null;
            character.Connection.Input = null;
            character.Connection = null;
        }

        PlayerEntity player = character.Player;
        player.Owner = default;
        player.Target = default;
        player.Combat.IsAutoAttacking = false;
        player.VelocityX = 0f;
        player.VelocityY = 0f;
        player.VelocityZ = 0f;
        player.StateFlags &= ~EntityStateFlags.Moving;
    }

    public void Remove(CharacterSession character)
    {
        m_world.RemovePlayer(character.Map, character.Player);
        m_sessions.RemoveCharacter(character);
        if (character.Connection != null)
        {
            character.Connection.Character = null;
            character.Connection = null;
        }
    }

    /// <summary>
    ///     The connection controlling <paramref name="character" /> is gone (Network Protocol §3). The character stays
    ///     in the world, standing still and still attackable, for the reconnect grace period; with no grace period it
    ///     is checkpointed and removed at once.
    /// </summary>
    public void Detach(CharacterSession character, uint tick)
    {
        Release(character);
        if (m_graceTicks == 0)
        {
            CheckpointAndRemove(character);
            return;
        }

        character.GraceEndsTick = tick + m_graceTicks;
    }

    /// <summary>
    ///     Gives <paramref name="character" />, retained or taken from another connection, to
    ///     <paramref name="session" />: the same entity, with no despawn.
    /// </summary>
    public void Attach(CharacterSession character, ClientSession session)
    {
        Release(character);
        character.Connection = session;
        character.GraceEndsTick = null;
        character.IsRemovalDeferred = false;
        character.Player.Owner = session.Connection;
        session.Character = character;
    }

    /// <summary>
    ///     Checkpoints and removes every character whose reconnect grace period is over.
    /// </summary>
    public void ExpireGracePeriods(uint tick)
    {
        m_expired.Clear();
        foreach (CharacterSession character in m_sessions.Characters)
        {
            if (character.GraceEndsTick is uint ends && unchecked((int)(tick - ends)) >= 0)
            {
                m_expired.Add(character);
            }
        }

        foreach (CharacterSession character in m_expired)
        {
            CheckpointAndRemove(character);
        }
    }

    /// <summary>
    ///     Controlled shutdown (System Architecture §13): a checkpoint for every character still in the world.
    /// </summary>
    public void CheckpointAll()
    {
        foreach (CharacterSession character in m_sessions.Characters)
        {
            QueueCheckpoint(character);
        }
    }

    /// <summary>
    ///     The console's <c>save</c> (Persistence §6): a checkpoint for every character in the world or in its grace
    ///     period. A character logging out is left alone: its final checkpoint is already on its way, and replacing it in
    ///     its slot would drop the completion that finishes the logout. Returns the number queued.
    /// </summary>
    public int SaveAll()
    {
        int queued = 0;
        foreach (CharacterSession character in m_sessions.Characters)
        {
            if (!character.IsLoggingOut)
            {
                QueueCheckpoint(character);
                queued++;
            }
        }

        return queued;
    }
}
}
