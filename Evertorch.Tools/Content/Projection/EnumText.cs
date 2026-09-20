using System;

namespace Evertorch.Tools
{
internal static class EnumText
{
    /// <summary>The camelCase spelling authors use in YAML, reused in generated output.</summary>
    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        string name = value.ToString();
        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}
}
