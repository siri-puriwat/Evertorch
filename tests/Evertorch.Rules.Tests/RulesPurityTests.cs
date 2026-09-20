using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace Evertorch.Rules.Tests
{
/// <summary>
/// The rules must run with nothing but their inputs: no host, container, file system, network, or engine.
/// </summary>
[TestFixture]
public sealed class RulesPurityTests
{
    private static readonly string[] AllowedAssemblies = { "Evertorch.Game", "System.Runtime" };

    private static readonly Type[] RuleImplementations =
    {
        typeof(RenewalCharacterRules), typeof(RenewalCombatRules), typeof(RenewalMovementRules),
    };

    [Test]
    public void RulesAssembly_ReferencesOnlyTheSharedDomainAndTheCoreRuntime()
    {
        string[] referenced = typeof(RenewalCombatRules).Assembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name!)
            .ToArray();

        Assert.That(referenced, Is.SubsetOf(AllowedAssemblies));
    }

    [Test]
    public void RuleImplementations_NeedNoServicesToConstruct()
    {
        foreach (Type type in RuleImplementations)
        {
            ConstructorInfo[] constructors = type.GetConstructors();

            Assert.That(constructors, Has.Length.EqualTo(1), type.Name);
            Assert.That(constructors[0].GetParameters(), Is.Empty, type.Name);
        }
    }

    [Test]
    public void RuleImplementations_HoldNoState()
    {
        foreach (Type type in RuleImplementations)
        {
            FieldInfo[] fields = type.GetFields(
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(fields.Where(field => !field.IsLiteral), Is.Empty, type.Name);
        }
    }
}
}
