using System;

namespace Evertorch.Server.Tests
{
internal sealed class FakeTimeProvider : TimeProvider
{
    public DateTimeOffset UtcNow { get; set; } = new DateTimeOffset(2026, 9, 20, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        return UtcNow;
    }
}
}
