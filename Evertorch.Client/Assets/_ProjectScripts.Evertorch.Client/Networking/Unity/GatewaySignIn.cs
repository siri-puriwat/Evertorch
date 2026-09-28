using System;
using System.Collections;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Evertorch.Client
{
/// <summary>
///     One sign-in at the server's HTTPS gateway (Network Protocol §4): <c>POST /session</c> with the login, the
///     password, and the transports this build can use. It lives outside the engine-free networking files, which the
///     server tests compile, and outside <c>UI/</c>, which never sends. The password goes only into the request's body.
/// </summary>
public sealed class GatewaySignIn
{
    public const string Path = "/session";

    private const int TimeoutSeconds = 15;

    private readonly Uri m_url;
    private readonly string m_login;
    private readonly string m_password;
    private readonly string m_transport;
    private readonly string? m_pinnedThumbprint;

    /// <param name="transport">The one transport this build connects with: UDP natively, WebSocket in a browser.</param>
    /// <param name="pinnedThumbprint">
    ///     When set, the one certificate trusted, by its SHA-1 thumbprint, for tests that must not depend on what the
    ///     machine trusts; unset, the operating system decides.
    /// </param>
    public GatewaySignIn(
        string host,
        int port,
        string login,
        string password,
        string transport,
        string? pinnedThumbprint)
    {
        m_url = new UriBuilder(Uri.UriSchemeHttps, host, port, Path).Uri;
        m_login = login;
        m_password = password;
        m_transport = transport;
        m_pinnedThumbprint = pinnedThumbprint;
    }

    public SignInAnswer? Answer { get; private set; }

    /// <summary>
    ///     What to tell the player when <see cref="Answer" /> is null.
    /// </summary>
    public string Error { get; private set; } = string.Empty;

    /// <summary>
    ///     The request body: <c>{"login":…,"password":…,"transports":["udp"]}</c>, or <c>["websocket"]</c>.
    /// </summary>
    public static string RequestJson(string login, string password, string transport)
    {
        return JsonUtility.ToJson(
            new RequestDto
            {
                login = login,
                password = password,
                transports = new[] { transport }
            });
    }

    /// <summary>
    ///     The answer's fields, or null when the text is not the answer's JSON.
    /// </summary>
    public static SignInAnswer? ReadAnswer(string json, string transport)
    {
        AnswerDto? answer;
        try
        {
            answer = JsonUtility.FromJson<AnswerDto>(json);
        }
        catch (ArgumentException)
        {
            return null;
        }

        return answer != null
            && SignInAnswer.TryCreate(
                transport,
                answer.transport,
                answer.host,
                answer.port,
                answer.token,
                out SignInAnswer? read)
                ? read
                : null;
    }

    /// <summary>
    ///     Signs in; <see cref="Answer" /> or <see cref="Error" /> is set when it ends.
    /// </summary>
    public IEnumerator Run()
    {
        byte[] body = Encoding.UTF8.GetBytes(RequestJson(m_login, m_password, m_transport));
        using (var request = new UnityWebRequest(
                   m_url,
                   UnityWebRequest.kHttpVerbPOST,
                   new DownloadHandlerBuffer(),
                   new UploadHandlerRaw(body)))
        {
            request.SetRequestHeader("Content-Type", "application/json");
            request.timeout = TimeoutSeconds;
            if (m_pinnedThumbprint != null)
            {
                request.certificateHandler = new PinnedCertificate(m_pinnedThumbprint);
            }

            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                Answer = ReadAnswer(request.downloadHandler.text, m_transport);
                Error = Answer == null ? SignInMessages.UnreadableAnswer : string.Empty;
            }
            else
            {
                Error = SignInMessages.ForStatus(
                    request.result == UnityWebRequest.Result.ProtocolError ? request.responseCode : 0);
            }
        }
    }

    private sealed class PinnedCertificate : CertificateHandler
    {
        private readonly string m_thumbprint;

        public PinnedCertificate(string thumbprint)
        {
            m_thumbprint = thumbprint;
        }

        protected override bool ValidateCertificate(byte[] certificateData)
        {
            using (var sha1 = SHA1.Create())
            {
                byte[] hash = sha1.ComputeHash(certificateData);
                var text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    text.Append(value.ToString("X2"));
                }

                return string.Equals(text.ToString(), m_thumbprint, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    // JsonUtility binds by field name, so these fields carry the JSON spelling instead of the usual naming. Each has
    // an initializer because nothing but JsonUtility ever assigns the answer's.
    // ReSharper disable InconsistentNaming, RedundantDefaultMemberInitializer
    [Serializable]
    private sealed class RequestDto
    {
        public string login = string.Empty;
        public string password = string.Empty;
        public string[] transports = Array.Empty<string>();
    }

    [Serializable]
    private sealed class AnswerDto
    {
        public string? transport = string.Empty;
        public string? host = string.Empty;
        public int port = 0;
        public string? token = string.Empty;
    }
    // ReSharper restore InconsistentNaming, RedundantDefaultMemberInitializer
}
}
