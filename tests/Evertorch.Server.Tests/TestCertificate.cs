using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A certificate made for one test run, written as a PKCS#12 file for the gateway and pinned by the test's own
///     client, so no test depends on what the machine trusts (Coding Standards §10). It names <c>localhost</c> and
///     <c>127.0.0.1</c>.
/// </summary>
internal sealed class TestCertificate : IDisposable
{
    private readonly TemporaryDirectory m_directory = new();

    public TestCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddDnsName("localhost");
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") }, false));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        using X509Certificate2 made = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(1));

        // Through PKCS#12 and back: SChannel refuses a certificate whose key exists only in memory.
        byte[] pkcs12 = made.Export(X509ContentType.Pkcs12);
        Path = System.IO.Path.Combine(m_directory.Path, "gateway.pfx");
        File.WriteAllBytes(Path, pkcs12);
        Thumbprint = made.Thumbprint;
    }

    public string Path { get; }

    public string Thumbprint { get; }

    public void Dispose()
    {
        m_directory.Dispose();
    }

    /// <summary>
    ///     A client that trusts this certificate alone.
    /// </summary>
    public HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                    certificate != null
                    && string.Equals(certificate.GetCertHashString(), Thumbprint, StringComparison.OrdinalIgnoreCase)
            }
        };
        return new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>
    ///     A WebSocket client that trusts this certificate alone and keeps the handshake's HTTP status.
    /// </summary>
    public ClientWebSocket CreateWebSocket()
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.Zero;
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            certificate != null
            && string.Equals(certificate.GetCertHashString(), Thumbprint, StringComparison.OrdinalIgnoreCase);
        return socket;
    }

    /// <summary>
    ///     The request body a native client sends: its login, its password, and the one transport it can use.
    /// </summary>
    public static string SignInJson(string login, string password)
    {
        return $"{{\"login\":\"{login}\",\"password\":\"{password}\",\"transports\":[\"udp\"]}}";
    }

    public static StringContent JsonContent(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }
}
}
