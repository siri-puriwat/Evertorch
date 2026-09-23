using System.IO;
using Evertorch.Client.Editor;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class RunServerToolbarButtonTests
{
    [Test]
    public void ScriptPathPointsAtTheServerLauncher()
    {
        Assert.That(File.Exists(RunServerToolbarButton.ScriptPath), Is.True, RunServerToolbarButton.ScriptPath);
    }
}
}
