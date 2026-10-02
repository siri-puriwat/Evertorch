using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Evertorch.Game;

namespace Evertorch.Server
{
/// <summary>
///     Operator commands that act on the world wait here for the tick thread, which runs them in its
///     <c>SchedulePersistence</c> phase (System Architecture §8): <c>save</c> and <c>boss respawn</c>.
/// </summary>
public sealed class AdminQueue : ITickPhase
{
    private readonly ConcurrentQueue<SaveRequest> m_saves = new();
    private readonly ConcurrentQueue<BossRespawnRequest> m_bossRespawns = new();
    private readonly CharacterLifetime m_characters;
    private readonly MonsterAiSystem m_monsters;
    private readonly AuditLog m_audit;

    public AdminQueue(CharacterLifetime characters, MonsterAiSystem monsters, AuditLog audit)
    {
        m_characters = characters;
        m_monsters = monsters;
        m_audit = audit;
    }

    public TickPhase Phase => TickPhase.SchedulePersistence;

    public void Execute(in TickContext context)
    {
        while (m_saves.TryDequeue(out SaveRequest? request))
        {
            int queued = m_characters.SaveAll();
            m_audit.OperatorSaved(request.Actor, queued);
            request.Result.TrySetResult(queued);
        }

        while (m_bossRespawns.TryDequeue(out BossRespawnRequest? request))
        {
            MonsterDefinitionId[] returned = m_monsters.RespawnBossesNow()
                .Select(boss => boss.Definition.Id)
                .ToArray();
            m_audit.OperatorBossRespawned(request.Actor, returned);
            request.Result.TrySetResult(returned);
        }
    }

    /// <summary>
    ///     Safe from any thread. The task ends with the number of checkpoints queued once a tick has run the save, and
    ///     never if the server stops first.
    /// </summary>
    public Task<int> RequestSave(AdminActor actor)
    {
        var request = new SaveRequest(actor);
        m_saves.Enqueue(request);
        return request.Result.Task;
    }

    /// <summary>
    ///     Safe from any thread. The task ends with the bosses brought back, none when every boss is alive, once a
    ///     tick has run the request, and never if the server stops first.
    /// </summary>
    public Task<IReadOnlyList<MonsterDefinitionId>> RequestBossRespawn(AdminActor actor)
    {
        var request = new BossRespawnRequest(actor);
        m_bossRespawns.Enqueue(request);
        return request.Result.Task;
    }

    private sealed class BossRespawnRequest
    {
        public BossRespawnRequest(AdminActor actor)
        {
            Actor = actor;
        }

        public AdminActor Actor { get; }

        public TaskCompletionSource<IReadOnlyList<MonsterDefinitionId>> Result { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class SaveRequest
    {
        public SaveRequest(AdminActor actor)
        {
            Actor = actor;
        }

        public AdminActor Actor { get; }

        // Whoever waits for the answer continues on its own thread, never on the tick thread.
        public TaskCompletionSource<int> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
}
