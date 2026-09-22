using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Evertorch.Game;
using Evertorch.Protocol;
using NUnit.Framework;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     The editor enters Play mode without reloading the domain, so a static field keeps its value from the previous
///     session. These assemblies run in Play mode and must hold no state that survives it.
/// </summary>
[TestFixture]
public sealed class StaticStateTests
{
    private const BindingFlags StaticFields =
        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Test]
    public void ClientAndSharedAssemblies_HaveNoMutableStaticFields()
    {
        Assembly[] assemblies =
        {
            typeof(GameClient).Assembly,
            typeof(WorldPosition).Assembly,
            typeof(MessageOpcode).Assembly
        };

        // Compiler-generated types hold lambda caches and array data, not state; auto-property backing fields are
        // still checked because they live on the declaring type.
        string[] fields = assemblies
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .SelectMany(type => type.GetFields(StaticFields))
            .Where(field => !field.IsLiteral && !field.IsInitOnly)
            .Select(field => $"{field.DeclaringType!.FullName}.{field.Name}")
            .ToArray();

        Assert.That(
            fields,
            Is.Empty,
            "make it instance state, or reset it in a SubsystemRegistration method and exempt it here");
    }
}
}
