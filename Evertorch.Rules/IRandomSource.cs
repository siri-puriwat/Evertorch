namespace Evertorch.Rules
{
/// <summary>
/// The only source of randomness a rule may use. The server owns the instance; tests supply a scripted one.
/// </summary>
public interface IRandomSource
{
    /// <summary>Returns a value from 0 up to, but not including, <paramref name="exclusiveMax" />.</summary>
    int Next(int exclusiveMax);
}
}
