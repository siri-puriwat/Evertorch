using Evertorch.Game;

namespace Evertorch.Protocol
{
/// <summary>
///     One job change an NPC offers (Network Protocol §6): the first job, the job it changes from, and the job level
///     the character needs, its base job's cap.
/// </summary>
public readonly struct NpcJobChangeOffer
{
    public NpcJobChangeOffer(JobDefinitionId job, JobDefinitionId fromJob, ushort level)
    {
        Job = job;
        FromJob = fromJob;
        Level = level;
    }

    public JobDefinitionId Job { get; }

    public JobDefinitionId FromJob { get; }

    public ushort Level { get; }
}
}
