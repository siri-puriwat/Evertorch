using System.IO;
using Evertorch.Client.Editor;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
[TestFixture]
public sealed class TerminalToolbarButtonTests
{
    [Test]
    public void WorkingDirectoryHoldsTheComposeFile()
    {
        string compose = Path.Combine(TerminalToolbarButton.WorkingDirectory, "docker", "compose.yaml");

        Assert.That(File.Exists(compose), Is.True, compose);
    }
}
}
