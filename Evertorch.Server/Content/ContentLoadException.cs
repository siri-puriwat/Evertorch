using System;
using System.Collections.Generic;

namespace Evertorch.Server
{
public sealed class ContentLoadException : Exception
{
    public ContentLoadException(IReadOnlyList<string> problems)
        : base(Describe(problems))
    {
        Problems = problems;
    }

    /// <summary>
    ///     One entry per defect, formatted as <c>file: path: message</c>.
    /// </summary>
    public IReadOnlyList<string> Problems { get; }

    private static string Describe(IReadOnlyList<string> problems)
    {
        string problemList = string.Join(Environment.NewLine, problems);
        return $"The server content package is invalid:{Environment.NewLine}{problemList}";
    }
}
}
