using System;
using Evertorch.Game;
using Evertorch.Persistence;
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

    private static readonly Action<ILogger, long, Exception?> LogCheckpointFailed =
        LoggerMessage.Define<long>(
            LogLevel.Warning,
            new EventId(4006, "CheckpointFailed"),
            "The checkpoint of character {Character} could not be written; it is retried once the database answers.");

    private readonly WorldSimulation m_world;
    private readonly SessionRegistry m_sessions;
    private readonly PersistenceWorker m_persistence;
    private readonly TimeProvider m_time;
    private readonly ILogger<CharacterLifetime> m_logger;
    private readonly uint m_checkpointIntervalTicks;

    public CharacterLifetime(
        WorldSimulation world,
        SessionRegistry sessions,
        PersistenceWorker persistence,
        TimeProvider time,
        IOptions<PersistenceOptions> persistenceOptions,
        IOptions<SimulationOptions> simulation,
        ILogger<CharacterLifetime> logger)
    {
        m_world = world;
        m_sessions = sessions;
        m_persistence = persistence;
        m_time = time;
        m_logger = logger;
        long ticks = (long)persistenceOptions.Value.CheckpointIntervalMs * simulation.Value.TickRate /
            MillisecondsPerSecond;
        m_checkpointIntervalTicks = (uint)Math.Max(1L, ticks);
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

        var character = new CharacterSession(new CharacterId(stored.Id), stored.Account, player!, map!)
        {
            Connection = owner,
            NextCheckpointTick = tick + m_checkpointIntervalTicks
        };
        m_sessions.AddCharacter(character);
        return character;
    }

    /// <summary>
    ///     Hands the character's current map, position, and HP to the writer, replacing any checkpoint of it still
    ///     waiting. <paramref name="onComplete" /> runs on the tick thread when it is written or found unwritable.
    /// </summary>
    public PersistenceJob QueueCheckpoint(CharacterSession character, Action<PersistenceOutcome>? onComplete = null)
    {
        PlayerEntity player = character.Player;
        long id = character.Character.Value;
        var checkpoint = new CharacterCheckpoint(
            id,
            character.Map.Definition.Id,
            player.Position,
            player.IsDead ? 0 : player.CurrentHealth,
            m_time.GetUtcNow().UtcDateTime);
        var job = new PersistenceJob<bool>(
            "checkpoint",
            character.Connection?.Connection ?? default,
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
                    LogCheckpointFailed(m_logger, id, null);
                }

                onComplete?.Invoke(outcome);
            });
        m_persistence.QueueCheckpoint(job);
        return job;
    }

    /// <summary>
    ///     Takes the character out of the world after queueing its final checkpoint.
    /// </summary>
    public void CheckpointAndRemove(CharacterSession character)
    {
        QueueCheckpoint(character);
        Remove(character);
    }

    /// <summary>
    ///     Takes the character out of the world without a checkpoint; the caller has written one.
    /// </summary>
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
    ///     Controlled shutdown (System Architecture §13): a checkpoint for every character still in the world.
    /// </summary>
    public void CheckpointAll()
    {
        foreach (CharacterSession character in m_sessions.Characters)
        {
            QueueCheckpoint(character);
        }
    }
}
}
