using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Evertorch.Server
{
/// <summary>
///     Operator commands that act on the world wait here for the tick thread, which runs them in its
///     <c>SchedulePersistence</c> phase (System Architecture §8). Only <c>save</c> uses it so far.
/// </summary>
public sealed class AdminQueue : ITickPhase
{
    private readonly ConcurrentQueue<SaveRequest> m_saves = new();
    private readonly CharacterLifetime m_characters;
    private readonly AuditLog m_audit;

    public AdminQueue(CharacterLifetime characters, AuditLog audit)
    {
        m_characters = characters;
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
