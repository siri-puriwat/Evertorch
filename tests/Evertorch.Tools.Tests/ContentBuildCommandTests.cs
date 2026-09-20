using System;
using System.IO;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
[TestFixture]
public sealed class ContentBuildCommandTests
{
    [Test]
    public void Validate_ForValidContent_ReturnsZeroAndWritesNothing()
    {
        using (ContentWorkspace workspace = new ContentWorkspace())
        {
            int exitCode = Run(out _, out string error, "content", "validate", "--content", workspace.ContentRoot);

            Assert.That(exitCode, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            Assert.That(Directory.Exists(workspace.OutputDirectory), Is.False);
        }
    }

    [Test]
    public void Validate_ForBrokenContent_ReturnsOneAndPrintsFileLineAndField()
    {
        using (ContentWorkspace workspace = new ContentWorkspace())
        {
            workspace.Replace("monsters/training_slime.yml", "chance: 0.7321", "chance: 1.5");

            int exitCode = Run(out _, out string error, "content", "validate", "--content", workspace.ContentRoot);

            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(
                error,
                Does.Contain("monsters/training_slime.yml(21): drops[0].chance: must be between 0 and 1"));
        }
    }

    [Test]
    public void Build_ForValidContent_WritesBothPackagesWithManifests()
    {
        using (ContentWorkspace workspace = new ContentWorkspace())
        {
            int exitCode = Build(workspace);

            Assert.That(exitCode, Is.EqualTo(0));
            foreach (string side in new[] { "server", "client" })
            {
                string directory = Path.Combine(workspace.OutputDirectory, side);
                Assert.That(
                    Directory.GetFiles(directory),
                    Has.Length.EqualTo(6),
                    "five definition files and a manifest in " + side);
                Assert.That(File.Exists(Path.Combine(directory, "manifest.json")), Is.True);
            }
        }
    }

    [Test]
    public void Build_WhenRunTwice_LeavesIdenticalFilesAndNoStagingDirectories()
    {
        using (ContentWorkspace workspace = new ContentWorkspace())
        {
            Build(workspace);
            byte[] first = File.ReadAllBytes(Path.Combine(workspace.OutputDirectory, "server", "monsters.json"));
            Build(workspace);
            byte[] second = File.ReadAllBytes(Path.Combine(workspace.OutputDirectory, "server", "monsters.json"));

            Assert.That(second, Is.EqualTo(first));
            Assert.That(Directory.Exists(workspace.OutputDirectory + ".staging"), Is.False);
            Assert.That(Directory.Exists(workspace.OutputDirectory + ".previous"), Is.False);
        }
    }

    [Test]
    public void Build_WhenPreviousOutputHoldsAStaleFile_RemovesIt()
    {
        using (ContentWorkspace workspace = new ContentWorkspace())
        {
            Build(workspace);
            string stale = Path.Combine(workspace.OutputDirectory, "client", "removed_kind.json");
            File.WriteAllText(stale, "{}");

            Build(workspace);

            Assert.That(File.Exists(stale), Is.False);
        }
    }

    [Test]
    public void Build_WhenContentBecomesInvalid_KeepsPreviousOutputUntouched()
    {
        using (ContentWorkspace workspace = new ContentWorkspace())
        {
            Build(workspace);
            string manifest = Path.Combine(workspace.OutputDirectory, "server", "manifest.json");
            byte[] before = File.ReadAllBytes(manifest);
            workspace.Replace("items/slime_gel.yml", "stackLimit: 999", "stackLimit: none");

            int exitCode = Build(workspace);

            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(manifest), Is.EqualTo(before));
        }
    }

    [Test]
    public void Build_WhenOutputDirectoryHoldsForeignFiles_RefusesAndKeepsThem()
    {
        using (ContentWorkspace workspace = new ContentWorkspace())
        {
            Directory.CreateDirectory(workspace.OutputDirectory);
            string foreign = Path.Combine(workspace.OutputDirectory, "notes.txt");
            File.WriteAllText(foreign, "not generated");

            int exitCode = Run(
                out _,
                out string error,
                "content",
                "build",
                "--content",
                workspace.ContentRoot,
                "--out",
                workspace.OutputDirectory);

            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(error, Does.Contain("Refusing to replace"));
            Assert.That(File.ReadAllText(foreign), Is.EqualTo("not generated"));
        }
    }

    [TestCase]
    [TestCase("content")]
    [TestCase("content", "publish")]
    [TestCase("content", "build", "--out")]
    [TestCase("content", "build", "--unknown", "value")]
    public void Run_WhenArgumentsAreInvalid_ReturnsTwoAndPrintsUsage(params string[] args)
    {
        int exitCode = Run(out _, out string error, args);

        Assert.That(exitCode, Is.EqualTo(2));
        Assert.That(error, Does.Contain("Usage:"));
    }

    private static int Build(ContentWorkspace workspace)
    {
        return Run(
            out _,
            out _,
            "content",
            "build",
            "--content",
            workspace.ContentRoot,
            "--out",
            workspace.OutputDirectory);
    }

    private static int Run(out string output, out string error, params string[] args)
    {
        using (StringWriter outputWriter = new StringWriter())
        using (StringWriter errorWriter = new StringWriter())
        {
            int exitCode = Program.Run(args, outputWriter, errorWriter);
            output = outputWriter.ToString();
            error = errorWriter.ToString().Replace(Environment.NewLine, "\n", StringComparison.Ordinal);
            return exitCode;
        }
    }
}
}
