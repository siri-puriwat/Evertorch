using System.Diagnostics.Metrics;

namespace Evertorch.Server.Tests
{
/// <summary>
///     Server instruments on a meter of their own, so a test's listener can tell its server's measurements from those
///     of every other server the test run builds.
/// </summary>
internal static class TestInstruments
{
    public static ServerInstruments Create()
    {
        return new ServerInstruments(new OwnMeterFactory());
    }

    private sealed class OwnMeterFactory : IMeterFactory
    {
        public Meter Create(MeterOptions options)
        {
            return new Meter(options);
        }

        public void Dispose()
        {
        }
    }
}
}
