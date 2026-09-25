using System.Collections.Generic;

namespace Evertorch.Game
{
/// <summary>
///     The experience each level needs to reach the next (Content Pipeline §4). <see cref="Levels" />[i] takes level
///     i + 1 to level i + 2, so a table of n entries caps the jobs that name it at level n + 1.
/// </summary>
public sealed class ExperienceTableDefinition
{
    public ExperienceTableDefinition(ExperienceDefinitionId id, IReadOnlyList<int> levels)
    {
        Id = id;
        Levels = levels;
    }

    public ExperienceDefinitionId Id { get; }

    public IReadOnlyList<int> Levels { get; }
}
}
