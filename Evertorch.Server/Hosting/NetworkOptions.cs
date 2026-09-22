using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
public sealed class NetworkOptions
{
    public const string SectionName = "Network";

    /// <summary>
    ///     IPv4 address to listen on. The default is loopback, so a server reachable from other machines is always a
    ///     deliberate configuration choice.
    /// </summary>
    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>
    ///     UDP port. 0 lets the operating system choose one, which tests rely on.
    /// </summary>
    public int Port { get; set; } = 7777;

    public int MaxConnections { get; set; } = 256;

    /// <summary>
    ///     Shared by every client build. It keeps stray traffic out; it is not a secret and not authentication.
    /// </summary>
    public string ConnectionKey { get; set; } = "evertorch";

    public int DisconnectTimeoutMs { get; set; } = 10000;

    /// <summary>
    ///     How long a connection may stay silent before its hello. Converted to ticks at the configured rate.
    /// </summary>
    public int HandshakeTimeoutMs { get; set; } = 5000;

    /// <summary>
    ///     Decoded messages that may wait for the next tick. More than this are dropped and counted.
    /// </summary>
    public int MaxInboundEvents { get; set; } = 4096;
}

internal sealed class NetworkOptionsValidator : IValidateOptions<NetworkOptions>
{
    public ValidateOptionsResult Validate(string? name, NetworkOptions options)
    {
        var failures = new List<string>();
        if (options.HandshakeTimeoutMs < 100 || options.HandshakeTimeoutMs > 60000)
        {
            failures.Add(NetworkOptions.SectionName + ":HandshakeTimeoutMs must be between 100 and 60000.");
        }

        if (options.MaxInboundEvents < 16 || options.MaxInboundEvents > 1000000)
        {
            failures.Add(NetworkOptions.SectionName + ":MaxInboundEvents must be between 16 and 1000000.");
        }

        if (!IPAddress.TryParse(options.BindAddress, out IPAddress? address)
            || address.AddressFamily != AddressFamily.InterNetwork)
        {
            failures.Add(NetworkOptions.SectionName + ":BindAddress must be an IPv4 address.");
        }

        if (options.Port < 0 || options.Port > 65535)
        {
            failures.Add(NetworkOptions.SectionName + ":Port must be between 0 and 65535.");
        }

        if (options.MaxConnections < 1 || options.MaxConnections > 10000)
        {
            failures.Add(NetworkOptions.SectionName + ":MaxConnections must be between 1 and 10000.");
        }

        if (string.IsNullOrEmpty(options.ConnectionKey) || options.ConnectionKey.Length > 64)
        {
            failures.Add(NetworkOptions.SectionName + ":ConnectionKey must be 1 to 64 characters.");
        }

        if (options.DisconnectTimeoutMs < 1000 || options.DisconnectTimeoutMs > 120000)
        {
            failures.Add(NetworkOptions.SectionName + ":DisconnectTimeoutMs must be between 1000 and 120000.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
}
