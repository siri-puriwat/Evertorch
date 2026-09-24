using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace Evertorch.Architecture.Tests
{
/// <summary>
///     Log event IDs and names are stable identities that operators search for, so no two events may share either. The
///     ranges per area are specified privately; the scan checks that every ID lies within them as a whole.
/// </summary>
[TestFixture]
public sealed class EventIdTests
{
    private const int FirstEventId = 1001;
    private const int LastEventId = 6999;

    private static readonly string[] LoggingProjects = { "Evertorch.Server", "Evertorch.Persistence" };

    private static readonly Regex EventIdDeclaration = new(
        @"new\s+EventId\(\s*(?<id>\d+)\s*,\s*""(?<name>[^""]*)""\s*\)",
        RegexOptions.CultureInvariant);

    private static IReadOnlyList<LogEvent> DeclaredEvents()
    {
        var events = new List<LogEvent>();
        foreach (string project in LoggingProjects)
        {
            string root = Path.Combine(RepositoryLayout.RootDirectory, project);
            foreach (string path in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match match in EventIdDeclaration.Matches(File.ReadAllText(path)))
                {
                    events.Add(
                        new LogEvent(
                            int.Parse(match.Groups["id"].Value, CultureInfo.InvariantCulture),
                            match.Groups["name"].Value,
                            $"{project}/{Path.GetRelativePath(root, path).Replace('\\', '/')}"));
                }
            }
        }

        return events;
    }

    private static IEnumerable<string> Duplicates<TKey>(IEnumerable<LogEvent> events, Func<LogEvent, TKey> key)
    {
        return events
            .GroupBy(key)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(", ", group.Select(item => $"{item.Name} in {item.File}"))}");
    }

    private sealed class LogEvent
    {
        public LogEvent(int id, string name, string file)
        {
            Id = id;
            Name = name;
            File = file;
        }

        public int Id { get; }

        public string Name { get; }

        public string File { get; }
    }

    [Test]
    public void EventIds_AreFoundInServerSources()
    {
        Assert.That(
            DeclaredEvents(),
            Has.Some.Matches<LogEvent>(item => item.Id == 1001 && item.Name == "TickOverrun"),
            "the checks below would pass vacuously if the declarations changed shape");
    }

    [Test]
    public void EventIds_AreUnique()
    {
        Assert.That(Duplicates(DeclaredEvents(), item => item.Id), Is.Empty);
    }

    [Test]
    public void EventIds_LieInTheAssignedRanges()
    {
        IEnumerable<string> outside = DeclaredEvents()
            .Where(item => item.Id < FirstEventId || item.Id > LastEventId || item.Id % 1000 == 0)
            .Select(item => $"{item.Id} {item.Name} in {item.File}");

        Assert.That(outside, Is.Empty);
    }

    [Test]
    public void EventNames_AreUniqueAndNamed()
    {
        IReadOnlyList<LogEvent> events = DeclaredEvents();

        Assert.That(events.Where(item => item.Name.Length == 0).Select(item => item.File), Is.Empty);
        Assert.That(Duplicates(events, item => item.Name), Is.Empty);
    }
}
}
