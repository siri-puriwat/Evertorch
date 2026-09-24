using System.Text;
using Evertorch.Protocol;

namespace Evertorch.Server
{
/// <summary>
///     The reason an operator gave for stopping the server, which the shutdown order sends every client as the text of
///     its <c>Maintenance</c> notice (Network Protocol §5). Written by the console thread, read by the stopping one.
/// </summary>
public sealed class ShutdownRequest
{
    private volatile string m_message = string.Empty;

    public string Message => m_message;

    /// <summary>
    ///     What a notice can carry of <paramref name="reason" />: at most 128 UTF-8 bytes, cut on a character boundary.
    ///     Ill-formed UTF-16 becomes U+FFFD, so the text always encodes.
    /// </summary>
    public static string ToNoticeText(string reason)
    {
        var text = new StringBuilder();
        int bytes = 0;
        foreach (Rune rune in reason.EnumerateRunes())
        {
            bytes += rune.Utf8SequenceLength;
            if (bytes > ProtocolLimits.MaxNoticeMessageBytes)
            {
                break;
            }

            text.Append(rune.ToString());
        }

        return text.ToString();
    }

    public void Set(string reason)
    {
        m_message = ToNoticeText(reason);
    }
}
}
