using UnityEditor.Build;
using UnityEngine;

namespace Evertorch.Client.Editor
{
/// <summary>
///     Fails a player build whose client content names presentation this project cannot load. It runs before
///     Addressables builds its content (Addressables' own processor has order 1), so nothing is built in vain.
/// </summary>
public sealed class AddressablesKeyCheckBuildProcessor : BuildPlayerProcessor
{
    public override int callbackOrder => 0;

    public override void PrepareForBuild(BuildPlayerContext buildPlayerContext)
    {
        AddressablesKeyCheckResult result = AddressablesKeyCheck.Run();
        foreach (string icon in result.MissingOptional)
        {
            Debug.LogWarning($"Addressables key check: optional {icon} has no Addressables entry.");
        }

        if (!result.IsValid)
        {
            throw new BuildFailedException(result.Describe());
        }
    }
}
}
