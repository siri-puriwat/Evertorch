using System.Collections;
using System.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client.Tests.PlayMode
{
/// <summary>
///     Signs a live test's real client in as a player does (Network Protocol §4): an account made at the server's
///     console, then the login panel's Connect. The client pins the gateway's certificate by the thumbprint the server
///     logged, so no test depends on what the machine trusts (Coding Standards §10).
/// </summary>
internal static class LiveSignIn
{
    private const float ContentTimeoutSeconds = 30f;

    /// <summary>
    ///     Makes an account for <paramref name="client" /> and points it at the gateway. Call it before the client
    ///     starts.
    /// </summary>
    public static void Prepare(GameClient client, LiveServer server)
    {
        Assert.That(server.TryReadGateway(out int port, out string thumbprint), Is.True, server.JoinOutput());
        client.Host = "127.0.0.1";
        client.Port = port;
        client.PinnedThumbprint = thumbprint;
        client.Login = server.CreateAccount();
    }

    /// <summary>
    ///     Types the password into the login panel and presses Connect once the content has loaded. The login is
    ///     already in its field.
    /// </summary>
    public static IEnumerator PressConnect(GameClient client)
    {
        float deadline = Time.realtimeSinceStartup + ContentTimeoutSeconds;
        while (client.Content == null && Time.realtimeSinceStartup < deadline)
        {
            yield return null;
        }

        Assert.That(client.Content, Is.Not.Null, client.Status);
        TMP_InputField password = client.GetComponentsInChildren<TMP_InputField>(true)
            .Single(field => field.transform.parent.name == "Password");
        password.text = LiveServer.AccountPassword;
        Button connect = client.GetComponentsInChildren<Button>(true).Single(button => button.name == "Connect");
        Assert.That(connect.gameObject.activeInHierarchy, Is.True, "the login panel offers Connect");
        Assert.That(client.Connection, Is.Null, "nothing connected by itself");
        connect.onClick.Invoke();
    }
}
}
