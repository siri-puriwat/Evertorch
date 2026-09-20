using System.Collections.Generic;
using System.Linq;

namespace Evertorch.Server
{
public sealed class TickPipeline
{
    private readonly ITickPhase[] m_phases;

    public TickPipeline(IEnumerable<ITickPhase> phases)
    {
        // OrderBy is stable, so systems sharing a phase keep their registration order.
        m_phases = phases.OrderBy(phase => phase.Phase).ToArray();
    }

    public void Execute(in TickContext context)
    {
        for (int index = 0; index < m_phases.Length; index++)
        {
            m_phases[index].Execute(context);
        }
    }
}
}
