using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace Evertorch.Client
{
/// <summary>
/// Reads the client content package from <c>StreamingAssets/GameData</c>.
/// </summary>
public sealed class StreamingContentLoader
{
    public const string FolderName = "GameData";

    public const string MissingPackageHint =
        "Run: dotnet run --project Evertorch.Tools -- content build "
        + "--client-out Evertorch.Client/Assets/StreamingAssets/GameData";

    public ClientContent? Content { get; private set; }

    public string Error { get; private set; } = string.Empty;

    public bool IsDone { get; private set; }

    // On Android the folder is inside the application archive and only a web request can read it, so the same
    // route is used everywhere.
    public IEnumerator Load()
    {
        Content = null;
        Error = string.Empty;
        IsDone = false;

        FileRequest manifestRequest = new FileRequest(ClientContentParser.ManifestFile);
        yield return manifestRequest.Send();
        if (manifestRequest.Bytes == null)
        {
            Fail("No client content was found in StreamingAssets/" + FolderName + ". " + MissingPackageHint);
            yield break;
        }

        IReadOnlyList<string> names = ClientContentParser.ReadFileList(manifestRequest.Bytes, out string listError);
        if (names.Count == 0)
        {
            Fail(listError);
            yield break;
        }

        Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string name in names)
        {
            FileRequest request = new FileRequest(name);
            yield return request.Send();
            if (request.Bytes == null)
            {
                Fail("Content file '" + name + "' could not be read. " + MissingPackageHint);
                yield break;
            }

            files[name] = request.Bytes;
        }

        Content = ClientContentParser.Parse(manifestRequest.Bytes, files, out string parseError);
        if (Content == null)
        {
            Fail(parseError + " " + MissingPackageHint);
            yield break;
        }

        IsDone = true;
    }

    private void Fail(string error)
    {
        Error = error;
        IsDone = true;
    }

    private sealed class FileRequest
    {
        private readonly string m_url;

        public FileRequest(string fileName)
        {
            string path = Application.streamingAssetsPath + "/" + FolderName + "/" + fileName;
            m_url = path.Contains("://") ? path : new Uri(path).AbsoluteUri;
        }

        public byte[]? Bytes { get; private set; }

        public IEnumerator Send()
        {
            using (UnityWebRequest request = UnityWebRequest.Get(m_url))
            {
                yield return request.SendWebRequest();
                if (request.result == UnityWebRequest.Result.Success)
                {
                    Bytes = request.downloadHandler.data;
                }
            }
        }
    }
}
}
