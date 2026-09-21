using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
/// Every control scheme has to end in the same intent. These scans fail when a second place starts building
/// intents or movement messages, which is how a scheme-specific path would begin.
/// </summary>
[TestFixture]
public sealed class SingleIntentPathTests
{
    private const string RuntimeFolder = "_ProjectScripts.Evertorch.Client";

    [Test]
    public void MoveIntent_IsBuiltOnlyByTheProducer()
    {
        Assert.That(FilesMatching(@"new\s+MoveIntent\s*\("), Is.EqualTo(new[] { "MoveIntentProducer.cs" }));
    }

    [Test]
    public void MovementMessages_AreBuiltOnlyByTheConnection()
    {
        Assert.That(
            FilesMatching(@"new\s+(MoveInput|StopMovement)\s*\("),
            Is.EqualTo(new[] { "ClientConnection.cs" }));
    }

    [Test]
    public void MovementController_IsTheOnlyCallerOfThePathfinder()
    {
        Assert.That(FilesMatching(@"new\s+GridPathfinder\s*\("), Is.EqualTo(new[] { "MovementController.cs" }));
    }

    private static IReadOnlyList<string> FilesMatching(string pattern)
    {
        string root = Path.Combine(Application.dataPath, RuntimeFolder);
        Regex regex = new Regex(pattern);
        return Directory
            .GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => regex.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetFileName(file))
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToArray();
    }
}
}
