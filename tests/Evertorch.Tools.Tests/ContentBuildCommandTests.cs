using System;
using System.IO;
using NUnit.Framework;

namespace Evertorch.Tools.Tests
{
[TestFixture]
public sealed class ContentBuildCommandTests
{
    [TestCase]
    [TestCase("content")]
    [TestCase("content", "publish")]
    [TestCase("content", "build", "--out")]
    [TestCase("content", "build", "--unknown", "value")]
    [TestCase("content", "build", "--client-out")]
    [TestCase("content", "validate", "--client-out", "anywhere")]
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

    private static int BuildWithClientOut(ContentWorkspace workspace, out string output, out string error)
    {
        return Run(
            out output,
            out error,
            "content",
            "build",
            "--content",
            workspace.ContentRoot,
            "--out",
            workspace.OutputDirectory,
            "--client-out",
            workspace.ClientDirectory);
    }

    private static int Run(out string output, out string error, params string[] args)
    {
        using (var outputWriter = new StringWriter())
        using (var errorWriter = new StringWriter())
        {
            int exitCode = Program.Run(args, outputWriter, errorWriter);
            output = outputWriter.ToString();
            error = errorWriter.ToString().Replace(Environment.NewLine, "\n", StringComparison.Ordinal);
            return exitCode;
        }
    }

    [Test]
    public void Build_ForValidContent_WritesBothPackagesWithManifests()
    {
        using (var workspace = new ContentWorkspace())
        {
            int exitCode = Build(workspace);

            Assert.That(exitCode, Is.EqualTo(0));
            // The experience tables stay on the server (Content Pipeline §5); the status effects go to both.
            foreach ((string side, int files) in new[] { ("server", 10), ("client", 9) })
            {
                string directory = Path.Combine(workspace.OutputDirectory, side);
                Assert.That(
                    Directory.GetFiles(directory),
                    Has.Length.EqualTo(files),
                    $"{files - 1} definition files and a manifest in {side}");
                Assert.That(File.Exists(Path.Combine(directory, "manifest.json")), Is.True);
            }
        }
    }

    [Test]
    public void Build_WhenContentBecomesInvalid_KeepsPreviousOutputUntouched()
    {
        using (var workspace = new ContentWorkspace())
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
        using (var workspace = new ContentWorkspace())
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

    [Test]
    public void Build_WhenPreviousOutputHoldsAStaleFile_RemovesIt()
    {
        using (var workspace = new ContentWorkspace())
        {
            Build(workspace);
            string stale = Path.Combine(workspace.OutputDirectory, "client", "removed_kind.json");
            File.WriteAllText(stale, "{}");

            Build(workspace);

            Assert.That(File.Exists(stale), Is.False);
        }
    }

    [Test]
    public void Build_WhenRunTwice_LeavesIdenticalFilesAndNoStagingDirectories()
    {
        using (var workspace = new ContentWorkspace())
        {
            Build(workspace);
            byte[] first = File.ReadAllBytes(Path.Combine(workspace.OutputDirectory, "server", "monsters.json"));
            Build(workspace);
            byte[] second = File.ReadAllBytes(Path.Combine(workspace.OutputDirectory, "server", "monsters.json"));

            Assert.That(second, Is.EqualTo(first));
            Assert.That(Directory.Exists($"{workspace.OutputDirectory}.staging"), Is.False);
            Assert.That(Directory.Exists($"{workspace.OutputDirectory}.previous"), Is.False);
        }
    }

    [Test]
    public void Build_WithClientOutAndInvalidContent_LeavesThePreviousCopyUntouched()
    {
        using (var workspace = new ContentWorkspace())
        {
            BuildWithClientOut(workspace, out _, out _);
            string manifest = Path.Combine(workspace.ClientDirectory, "manifest.json");
            byte[] before = File.ReadAllBytes(manifest);
            workspace.Replace("monsters/training_slime.yml", "chance: 0.7321", "chance: 1.5");

            int exitCode = BuildWithClientOut(workspace, out _, out _);

            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(File.ReadAllBytes(manifest), Is.EqualTo(before));
        }
    }

    [Test]
    public void Build_WithClientOutHoldingForeignJson_RefusesAndKeepsIt()
    {
        using (var workspace = new ContentWorkspace())
        {
            Directory.CreateDirectory(workspace.ClientDirectory);
            string foreign = Path.Combine(workspace.ClientDirectory, "settings.json");
            File.WriteAllText(foreign, "not generated");

            int exitCode = BuildWithClientOut(workspace, out _, out string error);

            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(error, Does.Contain("Refusing to write into"));
            Assert.That(File.ReadAllText(foreign), Is.EqualTo("not generated"));
            Assert.That(File.Exists(Path.Combine(workspace.ClientDirectory, "manifest.json")), Is.False);
        }
    }

    [Test]
    public void Build_WithClientOutHoldingSomeoneElsesManifest_RefusesAndDeletesNothing()
    {
        using (var workspace = new ContentWorkspace())
        {
            // The shape of a Unity Packages folder: a manifest.json that is not this tool's, beside a lock file.
            Directory.CreateDirectory(workspace.ClientDirectory);
            string manifest = Path.Combine(workspace.ClientDirectory, "manifest.json");
            string lockFile = Path.Combine(workspace.ClientDirectory, "packages-lock.json");
            File.WriteAllText(manifest, "{ \"dependencies\": { \"com.unity.ugui\": \"2.0.0\" } }");
            File.WriteAllText(lockFile, "{ \"dependencies\": {} }");

            int exitCode = BuildWithClientOut(workspace, out _, out string error);

            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(error, Does.Contain("Refusing to write into"));
            Assert.That(File.ReadAllText(manifest), Does.Contain("com.unity.ugui"));
            Assert.That(File.Exists(lockFile), Is.True);
        }
    }

    [Test]
    public void Build_WithClientOutLeftHalfWrittenByAnInterruptedCopy_CompletesIt()
    {
        using (var workspace = new ContentWorkspace())
        {
            BuildWithClientOut(workspace, out _, out _);
            File.Delete(Path.Combine(workspace.ClientDirectory, "manifest.json"));
            File.Delete(Path.Combine(workspace.ClientDirectory, "skills.json"));

            int exitCode = BuildWithClientOut(workspace, out _, out string error);

            Assert.That(exitCode, Is.EqualTo(0), error);
            Assert.That(Directory.GetFiles(workspace.ClientDirectory), Has.Length.EqualTo(9));
        }
    }

    [Test]
    public void Build_WithClientOut_CopiesTheClientPackageByteForByte()
    {
        using (var workspace = new ContentWorkspace())
        {
            int exitCode = BuildWithClientOut(workspace, out string output, out _);

            Assert.That(exitCode, Is.EqualTo(0));
            Assert.That(output, Does.Contain("Client package copied to"));
            string client = Path.Combine(workspace.OutputDirectory, "client");
            foreach (string file in Directory.GetFiles(client))
            {
                string copy = Path.Combine(workspace.ClientDirectory, Path.GetFileName(file));
                Assert.That(File.ReadAllBytes(copy), Is.EqualTo(File.ReadAllBytes(file)), Path.GetFileName(file));
            }

            Assert.That(Directory.GetFiles(workspace.ClientDirectory), Has.Length.EqualTo(9));
        }
    }

    [Test]
    public void Build_WithClientOut_RemovesStaleJsonAndKeepsTheOtherToolsSideFiles()
    {
        using (var workspace = new ContentWorkspace())
        {
            BuildWithClientOut(workspace, out _, out _);
            string stale = Path.Combine(workspace.ClientDirectory, "retired.json");
            string sideFile = Path.Combine(workspace.ClientDirectory, "items.json.meta");
            File.WriteAllText(stale, "{}");
            File.WriteAllText(sideFile, "guid: 1");

            int exitCode = BuildWithClientOut(workspace, out _, out _);

            Assert.That(exitCode, Is.EqualTo(0));
            Assert.That(File.Exists(stale), Is.False);
            Assert.That(File.ReadAllText(sideFile), Is.EqualTo("guid: 1"));
        }
    }

    [Test]
    public void Validate_ForBrokenContent_ReturnsOneAndPrintsFileLineAndField()
    {
        using (var workspace = new ContentWorkspace())
        {
            workspace.Replace("monsters/training_slime.yml", "chance: 0.7321", "chance: 1.5");

            int exitCode = Run(out _, out string error, "content", "validate", "--content", workspace.ContentRoot);

            Assert.That(exitCode, Is.EqualTo(1));
            Assert.That(
                error,
                Does.Contain("monsters/training_slime.yml(24): drops[0].chance: must be between 0 and 1"));
        }
    }

    [Test]
    public void Validate_ForValidContent_ReturnsZeroAndWritesNothing()
    {
        using (var workspace = new ContentWorkspace())
        {
            int exitCode = Run(out _, out string error, "content", "validate", "--content", workspace.ContentRoot);

            Assert.That(exitCode, Is.EqualTo(0));
            Assert.That(error, Is.Empty);
            Assert.That(Directory.Exists(workspace.OutputDirectory), Is.False);
        }
    }
}
}
