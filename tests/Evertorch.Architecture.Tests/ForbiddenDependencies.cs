using System;
using System.Linq;

namespace Evertorch.Architecture.Tests
{
internal static class ForbiddenDependencies
{
    private static readonly string[] ExactNames =
    {
        "LiteNetLib",
        "Npgsql",
        "Evertorch.Client",
        "Evertorch.Persistence",
        "Evertorch.Rules",
        "Evertorch.Server",
        "Evertorch.Tools",
    };

    private static readonly string[] Prefixes =
    {
        "UnityEngine",
        "UnityEditor",
        "Unity.",
        "LiteNetLib.",
        "Npgsql.",
        "Microsoft.EntityFrameworkCore",
        "Microsoft.AspNetCore",
        "Microsoft.Extensions.",
        "Evertorch.Client.",
        "Evertorch.Persistence.",
        "Evertorch.Rules.",
        "Evertorch.Server.",
        "Evertorch.Tools.",
    };

    public static bool IsForbiddenForSharedAssembly(string dependencyName)
    {
        return ExactNames.Contains(dependencyName, StringComparer.OrdinalIgnoreCase)
               || Prefixes.Any(prefix => dependencyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
    }
}
}
