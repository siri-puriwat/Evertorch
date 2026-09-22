using System;
using System.IO;
using NUnit.Framework;

namespace Evertorch.Server.Tests
{
[TestFixture]
[NonParallelizable]
public sealed class ProgramTests
{
    [Test]
    public void Main_WithoutAContentPackage_ReturnsOneAndSaysHowToStartForDevelopment()
    {
        using var root = new TemporaryDirectory();
        string missing = Path.Combine(root.Path, "no-such-package");
        TextWriter original = Console.Error;
        var error = new StringWriter();
        int exitCode;

        Console.SetError(error);
        try
        {
            exitCode = Program.Main(new[] { "--Content:ServerPackagePath=" + missing });
        }
        finally
        {
            Console.SetError(original);
        }

        string text = error.ToString();
        Assert.That(exitCode, Is.EqualTo(1));
        Assert.That(text, Does.Contain("no usable content package"));
        Assert.That(text, Does.Contain("scripts\\run-server.cmd"));
        Assert.That(text, Does.Contain("DOTNET_ENVIRONMENT=Development"));
        Assert.That(text, Does.Not.Contain("Press Enter"), "only a window of its own makes the server wait");
    }
}
}
