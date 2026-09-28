using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.Extensions.Options;

namespace Evertorch.Server
{
/// <summary>
///     The HTTPS gateway (System Architecture §12). Off in code and on in <c>appsettings.json</c>, which also names the
///     port, so a host built without that file serves it only when asked to.
/// </summary>
public sealed class GatewayOptions
{
    public const string SectionName = "Gateway";

    public bool Enabled { get; set; }

    public string BindAddress { get; set; } = "127.0.0.1";

    /// <summary>
    ///     The TCP port; 0 lets the operating system choose one.
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    ///     A PKCS#12 file holding the certificate and its key. Unset, the current user's ASP.NET Core development
    ///     certificate serves.
    /// </summary>
    public string? CertificatePath { get; set; }

    /// <summary>
    ///     The password of <see cref="CertificatePath" />, set through the environment variable
    ///     <c>Gateway__CertificatePassword</c> so it never appears on a command line. Never logged or repeated.
    /// </summary>
    public string? CertificatePassword { get; set; }

    /// <summary>
    ///     The host a sign-in answer names. Unset, the UDP bind address unless it is a wildcard, else the host the
    ///     request named.
    /// </summary>
    public string? PublicHost { get; set; }

    /// <summary>
    ///     Sign-ins checked at once; each costs a password hash. More are answered 503.
    /// </summary>
    public int MaxConcurrentSignIns { get; set; } = 4;

    /// <summary>
    ///     The web pages that may call <c>/session</c> from a browser (<see cref="GatewayOrigins" />); none by default.
    /// </summary>
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
}

public sealed class GatewayOptionsValidator : IValidateOptions<GatewayOptions>
{
    public ValidateOptionsResult Validate(string? name, GatewayOptions options)
    {
        var failures = new List<string>();
        if (!IPAddress.TryParse(options.BindAddress, out IPAddress? _))
        {
            failures.Add($"{GatewayOptions.SectionName}:BindAddress must be an IP address.");
        }

        if (options.Port < 0 || options.Port > 65535)
        {
            failures.Add($"{GatewayOptions.SectionName}:Port must be between 0 and 65535.");
        }

        if (options.PublicHost != null && Uri.CheckHostName(options.PublicHost) == UriHostNameType.Unknown)
        {
            failures.Add($"{GatewayOptions.SectionName}:PublicHost must be a host name or an IP address.");
        }

        if (options.MaxConcurrentSignIns < 1 || options.MaxConcurrentSignIns > 64)
        {
            failures.Add($"{GatewayOptions.SectionName}:MaxConcurrentSignIns must be between 1 and 64.");
        }

        foreach (string origin in options.AllowedOrigins)
        {
            if (!GatewayOrigins.IsValidEntry(origin))
            {
                failures.Add(
                    $"{GatewayOptions.SectionName}:AllowedOrigins holds '{origin}', which is not an http or https origin.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
}
