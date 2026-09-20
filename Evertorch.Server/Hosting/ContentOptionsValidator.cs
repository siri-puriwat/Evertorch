using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
public sealed class ContentOptionsValidator : IValidateOptions<ContentOptions>
{
    public ValidateOptionsResult Validate(string? name, ContentOptions options)
    {
        return string.IsNullOrWhiteSpace(options.ServerPackagePath)
            ? ValidateOptionsResult.Fail(ContentOptions.SectionName + ":ServerPackagePath must not be empty.")
            : ValidateOptionsResult.Success;
    }
}
}
