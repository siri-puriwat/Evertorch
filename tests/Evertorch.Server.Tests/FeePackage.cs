using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Evertorch.Server.Tests
{
/// <summary>
///     A server-only test package for the Smith's fees in parts (Network Protocol §6): the seeded package, the
///     repository's content without the quarry's spawns, with more cards, each a copy of the Training Slime Card under an
///     ID of its own, so the fees take more than one part; signed again with the repository's client content version, so
///     the real client still connects.
/// </summary>
internal static class FeePackage
{
    private const string Items = "items.json";
    private const string Model = "item.card.training_slime";

    /// <summary>The seeded package with <paramref name="extra" /> more cards, <c>item.card.test_fee_00</c> on.</summary>
    public static Dictionary<string, byte[]> Build(int extra)
    {
        Dictionary<string, byte[]> files = PackageFixture.BuildSeededPackage();
        string clientContentVersion = PackageFixture.ClientContentVersionOf(files);
        JsonArray definitions = JsonNode.Parse(files[Items])!["definitions"]!.AsArray();
        JsonNode model = definitions.Single(node => (string?)node!["id"] == Model)!;
        for (int index = 0; index < extra; index++)
        {
            JsonObject card = JsonNode.Parse(model.ToJsonString())!.AsObject();
            card["id"] = $"item.card.test_fee_{index:D2}";
            card["displayName"] = $"Test Fee Card {index:D2}";
            definitions.Add(card);
        }

        files[Items] = Encoding.UTF8.GetBytes(definitions.Root.ToJsonString());
        PackageFixture.RewriteManifest(files, clientContentVersion);
        return files;
    }
}
}
