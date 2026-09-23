using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Every control scheme has to end in the same intent. These scans fail when a second place starts building
///     intents or movement messages, which is how a scheme-specific path would begin.
/// </summary>
[TestFixture]
public sealed class SingleIntentPathTests
{
    private const string RuntimeFolder = "_ProjectScripts.Evertorch.Client";

    /// <summary>
    ///     Any construction of the named types: <c>new T(...)</c>, and the target-typed <c>new(...)</c> that Full
    ///     Cleanup writes where the type is already named (<c>T x = new(...)</c>, <c>T X => new(...)</c>,
    ///     <c>T X(...) => new(...)</c>).
    /// </summary>
    private static string Construction(string types)
    {
        return $@"new\s+({types})\s*\(|\b({types})\??\s+\w+\s*(\([^)]*\)\s*)?(=|=>)\s*new\s*\(";
    }

    private static IReadOnlyList<string> FilesMatching(string pattern)
    {
        string root = Path.Combine(Application.dataPath, RuntimeFolder);
        var regex = new Regex(pattern);
        return Directory
            .GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => regex.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetFileName(file))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    [Test]
    public void Construction_MatchesExplicitAndTargetTypedNewButNotOtherUses()
    {
        var pattern = new Regex(Construction("MoveIntent"));
        string[] constructions =
        {
            "return new MoveIntent(1, 1, 0f, 0f);", "MoveIntent intent = new(1, 1, 0f, 0f);",
            "private MoveIntent? m_last = new(1, 1, 0f, 0f);", "MoveIntent Stop => new(0, 0, 0f, 0f);",
            "MoveIntent Next(uint sequence) => new(sequence, 1, 0f, 0f);"
        };
        string[] others =
        {
            "private readonly Queue<MoveIntent> m_pending = new();", "MoveIntent intent = producer.Next(1, default);",
            "void Send(MoveIntent intent);"
        };

        Assert.That(constructions.Where(line => !pattern.IsMatch(line)), Is.Empty);
        Assert.That(others.Where(line => pattern.IsMatch(line)), Is.Empty);
    }

    [Test]
    public void MoveIntent_IsBuiltOnlyByTheProducer()
    {
        Assert.That(FilesMatching(Construction("MoveIntent")), Is.EqualTo(new[] { "MoveIntentProducer.cs" }));
    }

    [Test]
    public void MovementController_IsTheOnlyCallerOfThePathfinder()
    {
        Assert.That(FilesMatching(Construction("GridPathfinder")), Is.EqualTo(new[] { "MovementController.cs" }));
    }

    [Test]
    public void MovementMessages_AreBuiltOnlyByTheConnection()
    {
        Assert.That(
            FilesMatching(Construction("MoveInput|StopMovement")),
            Is.EqualTo(new[] { "ClientConnection.cs" }));
    }
}
}
