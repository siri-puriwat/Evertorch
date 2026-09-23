using System;
using System.Collections.Generic;
using System.Text;

namespace Evertorch.Client.Editor
{
public sealed class AddressablesKeyCheckResult
{
    public AddressablesKeyCheckResult(
        string packageError,
        IReadOnlyList<string> missing,
        IReadOnlyList<string> missingOptional)
    {
        PackageError = packageError ?? throw new ArgumentNullException(nameof(packageError));
        Missing = missing ?? throw new ArgumentNullException(nameof(missing));
        MissingOptional = missingOptional ?? throw new ArgumentNullException(nameof(missingOptional));
    }

    /// <summary>
    ///     Why the generated client package could not be read; empty when it was.
    /// </summary>
    public string PackageError { get; }

    /// <summary>
    ///     Required presentation that does not resolve. Any entry fails the player build.
    /// </summary>
    public IReadOnlyList<string> Missing { get; }

    /// <summary>
    ///     Optional presentation (icons) that does not resolve; reported as warnings only.
    /// </summary>
    public IReadOnlyList<string> MissingOptional { get; }

    public bool IsValid => PackageError.Length == 0 && Missing.Count == 0;

    public static AddressablesKeyCheckResult PackageUnusable(string error)
    {
        return new AddressablesKeyCheckResult(error, Array.Empty<string>(), Array.Empty<string>());
    }

    public string Describe()
    {
        if (PackageError.Length > 0)
        {
            return $"Addressables key check could not read the client content: {PackageError}";
        }

        var text = new StringBuilder("Addressables key check found required presentation that does not resolve:");
        foreach (string problem in Missing)
        {
            text.Append("\n  ").Append(problem);
        }

        return text.ToString();
    }
}
}
