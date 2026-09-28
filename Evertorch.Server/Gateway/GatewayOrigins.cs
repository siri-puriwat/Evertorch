using System;
using System.Collections.Generic;

namespace Evertorch.Server
{
/// <summary>
///     The web pages that may call <c>/session</c> from a browser (CORS; System Architecture §12): each entry an origin,
///     <c>scheme://host</c> with an optional port, or <c>scheme://host:*</c> for the host on any port. The WebSocket's
///     origin is not checked: a token, not a cookie, signs it in (Network Protocol §7).
/// </summary>
public static class GatewayOrigins
{
    private const string AnyPort = ":*";

    public static bool IsValidEntry(string entry)
    {
        string origin = entry.EndsWith(AnyPort, StringComparison.Ordinal) ? entry[..^AnyPort.Length] : entry;
        return Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.Equals(uri.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsAllowed(string? origin, IReadOnlyList<string> entries)
    {
        if (string.IsNullOrEmpty(origin)
            || !Uri.TryCreate(origin, UriKind.Absolute, out Uri? uri)
            || !string.Equals(uri.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string withoutPort = $"{uri.Scheme}://{uri.Host}";
        foreach (string entry in entries)
        {
            bool isMatch = entry.EndsWith(AnyPort, StringComparison.Ordinal)
                ? string.Equals(entry[..^AnyPort.Length], withoutPort, StringComparison.OrdinalIgnoreCase)
                : string.Equals(entry, origin, StringComparison.OrdinalIgnoreCase);
            if (isMatch)
            {
                return true;
            }
        }

        return false;
    }
}
}
