using System;
using System.IO;

namespace Evertorch.Tools
{
public static class Program
{
    private const int Success = 0;
    private const int Failure = 1;
    private const int UsageError = 2;

    public static int Main(string[] args)
    {
        return Run(args, Console.Out, Console.Error);
    }

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        if (args.Length < 2 || args[0] != "content" || (args[1] != "validate" && args[1] != "build"))
        {
            WriteUsage(error);
            return UsageError;
        }

        string? contentRoot = null;
        string? outputDirectory = null;
        string? clientDirectory = null;
        for (int index = 2; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                WriteUsage(error);
                return UsageError;
            }

            switch (args[index])
            {
                case "--content":
                    contentRoot = args[index + 1];
                    break;
                case "--out":
                    outputDirectory = args[index + 1];
                    break;
                case "--client-out":
                    clientDirectory = args[index + 1];
                    break;
                default:
                    WriteUsage(error);
                    return UsageError;
            }
        }

        bool isBuild = args[1] == "build";
        if (!isBuild && clientDirectory != null)
        {
            WriteUsage(error);
            return UsageError;
        }

        if (contentRoot == null || (isBuild && outputDirectory == null))
        {
            string? repositoryRoot = FindRepositoryRoot(Directory.GetCurrentDirectory());
            if (repositoryRoot == null)
            {
                error.WriteLine("Evertorch.sln was not found above the current directory; pass --content and --out.");
                return UsageError;
            }

            contentRoot ??= Path.Combine(repositoryRoot, "content");
            outputDirectory ??= Path.Combine(repositoryRoot, "artifacts", "content");
        }

        ContentPipelineResult result = ContentPipeline.Run(contentRoot);
        foreach (ContentDiagnostic diagnostic in result.Diagnostics)
        {
            error.WriteLine(diagnostic.ToString());
        }

        if (result.Packages == null)
        {
            error.WriteLine("Content validation failed with " + result.Diagnostics.Count + " error(s).");
            return Failure;
        }

        if (isBuild)
        {
            try
            {
                ContentPackageDeployer.Deploy(result.Packages, outputDirectory!);
                if (clientDirectory != null)
                {
                    ContentPackageDeployer.DeployClient(result.Packages.Client, clientDirectory);
                }
            }
            catch (InvalidOperationException exception)
            {
                error.WriteLine(exception.Message);
                return Failure;
            }
            catch (IOException exception)
            {
                error.WriteLine("Could not write content packages: " + exception.Message);
                return Failure;
            }
            catch (UnauthorizedAccessException exception)
            {
                error.WriteLine("Could not write content packages: " + exception.Message);
                return Failure;
            }

            output.WriteLine("Content packages written to " + Path.GetFullPath(outputDirectory!));
            if (clientDirectory != null)
            {
                output.WriteLine("Client package copied to " + Path.GetFullPath(clientDirectory));
            }
        }

        output.WriteLine("Server content version: " + result.Packages.Server.Version);
        output.WriteLine("Client content version: " + result.Packages.Client.Version);
        return Success;
    }

    private static string? FindRepositoryRoot(string start)
    {
        DirectoryInfo? directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Evertorch.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static void WriteUsage(TextWriter error)
    {
        error.WriteLine("Usage:");
        error.WriteLine("  Evertorch.Tools content validate [--content <dir>]");
        error.WriteLine("  Evertorch.Tools content build [--content <dir>] [--out <dir>] [--client-out <dir>]");
    }
}
}
