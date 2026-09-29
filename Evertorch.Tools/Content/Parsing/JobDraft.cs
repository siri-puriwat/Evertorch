using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Evertorch.Game;

namespace Evertorch.Tools
{
/// <summary>
///     A job as read from its file: a base job complete already, or a first job waiting for its base job, whose values
///     it takes in the second pass (Content Pipeline §4).
/// </summary>
internal sealed class JobDraft
{
    private readonly DefinitionSource? m_source;
    private readonly string m_prefab = string.Empty;
    private readonly JobDefinitionId m_id;
    private readonly string m_displayName = string.Empty;
    private readonly int m_healthBase;
    private readonly int m_healthPerLevel;
    private readonly int m_spiritBase;
    private readonly int m_spiritPerLevel;
    private readonly ExperienceDefinitionId m_jobExperienceTable;
    private readonly IReadOnlyList<SkillDefinitionId> m_skills = Array.Empty<SkillDefinitionId>();
    private readonly IReadOnlyList<WeaponType> m_weapons = Array.Empty<WeaponType>();

    public JobDraft(AuthoredJob baseJob)
    {
        Resolved = baseJob;
    }

    public JobDraft(
        DefinitionSource source,
        string prefab,
        JobDefinitionId id,
        string displayName,
        JobDefinitionId baseJob,
        int healthBase,
        int healthPerLevel,
        int spiritBase,
        int spiritPerLevel,
        ExperienceDefinitionId jobExperienceTable,
        IReadOnlyList<SkillDefinitionId> skills,
        IReadOnlyList<WeaponType> weapons)
    {
        m_source = source;
        m_prefab = prefab;
        m_id = id;
        m_displayName = displayName;
        BaseJob = baseJob;
        m_healthBase = healthBase;
        m_healthPerLevel = healthPerLevel;
        m_spiritBase = spiritBase;
        m_spiritPerLevel = spiritPerLevel;
        m_jobExperienceTable = jobExperienceTable;
        m_skills = skills;
        m_weapons = weapons;
    }

    /// <summary>
    ///     A base job as read; null for a first job.
    /// </summary>
    public AuthoredJob? Resolved { get; }

    /// <summary>
    ///     The job a first job names; null for a base job.
    /// </summary>
    public JobDefinitionId? BaseJob { get; }

    /// <summary>
    ///     The second pass: every base job as read, and each first job with the values of its base job, whose job table
    ///     caps its carried points. A first job whose base job is unknown or has a base job of its own, or whose own
    ///     skills repeat one of its base tree, is reported and left out; one whose base job or table was rejected for
    ///     another error is left out quietly (§7).
    /// </summary>
    public static List<AuthoredJob> ResolveAll(
        IReadOnlyList<JobDraft> drafts,
        IReadOnlyList<AuthoredExperienceTable> experienceTables,
        IReadOnlyCollection<string> declaredIds,
        List<ContentDiagnostic> diagnostics)
    {
        var baseJobs = new Dictionary<string, JobDefinition>(StringComparer.Ordinal);
        var firstJobs = new HashSet<string>(StringComparer.Ordinal);
        foreach (JobDraft draft in drafts)
        {
            if (draft.Resolved != null)
            {
                baseJobs[draft.Resolved.Definition.Id.Value] = draft.Resolved.Definition;
            }
            else
            {
                firstJobs.Add(draft.m_id.Value);
            }
        }

        var tables = new Dictionary<string, ExperienceTableDefinition>(StringComparer.Ordinal);
        foreach (AuthoredExperienceTable table in experienceTables)
        {
            tables[table.Definition.Id.Value] = table.Definition;
        }

        var jobs = new List<AuthoredJob>();
        foreach (JobDraft draft in drafts)
        {
            AuthoredJob? job = draft.Resolved ?? draft.Resolve(baseJobs, firstJobs, tables, declaredIds, diagnostics);
            if (job != null)
            {
                jobs.Add(job);
            }
        }

        return jobs;
    }

    private AuthoredJob? Resolve(
        Dictionary<string, JobDefinition> baseJobs,
        HashSet<string> firstJobs,
        Dictionary<string, ExperienceTableDefinition> tables,
        IReadOnlyCollection<string> declaredIds,
        List<ContentDiagnostic> diagnostics)
    {
        DefinitionSource source = m_source!;
        string baseId = BaseJob.GetValueOrDefault().Value;
        if (!baseJobs.TryGetValue(baseId, out JobDefinition? baseJob))
        {
            if (firstJobs.Contains(baseId))
            {
                Report(source, "server.baseJob", $"names job '{baseId}', which has a base job of its own", diagnostics);
            }
            else if (!declaredIds.Contains(baseId))
            {
                Report(source, "server.baseJob", $"references unknown job '{baseId}'", diagnostics);
            }

            return null;
        }

        bool isRepeated = false;
        for (int index = 0; index < m_skills.Count; index++)
        {
            if (baseJob.Tree.Contains(m_skills[index]))
            {
                Report(
                    source,
                    string.Format(CultureInfo.InvariantCulture, "server.skills[{0}]", index),
                    $"repeats '{m_skills[index].Value}' of its base job's tree",
                    diagnostics);
                isRepeated = true;
            }
        }

        // An unknown base table is reported on the base job itself.
        if (isRepeated || !tables.TryGetValue(baseJob.JobExperienceTable.Value, out ExperienceTableDefinition? table))
        {
            return null;
        }

        var definition = JobDefinition.FirstJob(
            m_id,
            m_displayName,
            baseJob,
            table,
            m_healthBase,
            m_healthPerLevel,
            m_spiritBase,
            m_spiritPerLevel,
            m_jobExperienceTable,
            m_skills,
            m_weapons);
        return new AuthoredJob(source, definition, m_prefab);
    }

    private static void Report(
        DefinitionSource source,
        string fieldPath,
        string message,
        List<ContentDiagnostic> diagnostics)
    {
        diagnostics.Add(new ContentDiagnostic(source.File, fieldPath, source.LineOf(fieldPath), message));
    }
}
}
