using System.IO;
using Evertorch.Client.Editor;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class ServeWebToolbarButtonTests
{
    [Test]
    public void ScriptPathPointsAtTheWebServer()
    {
        Assert.That(File.Exists(ServeWebToolbarButton.ScriptPath), Is.True, ServeWebToolbarButton.ScriptPath);
    }
}
}
