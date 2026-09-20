using System;
using System.Collections.Generic;

namespace Evertorch.Server.Tests
{
internal sealed class RecordingPhase : ITickPhase
{
    private readonly string m_name;
    private readonly List<string> m_log;
    private readonly Action<TickContext>? m_onExecute;

    public RecordingPhase(TickPhase phase, string name, List<string> log, Action<TickContext>? onExecute = null)
    {
        Phase = phase;
        m_name = name;
        m_log = log;
        m_onExecute = onExecute;
    }

    public TickPhase Phase { get; }

    public void Execute(in TickContext context)
    {
        m_log.Add(m_name + "@" + context.Tick);
        m_onExecute?.Invoke(context);
    }
}
}
