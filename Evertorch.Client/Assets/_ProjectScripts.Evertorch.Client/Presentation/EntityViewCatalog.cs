using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using Object = UnityEngine.Object;

namespace Evertorch.Client
{
/// <summary>
///     Loads entity body prefabs by their logical Addressables key, once per key, and hands them to the views that
///     asked. A key that does not resolve gets an explicit placeholder and one warning; the view never waits forever.
///     It also keeps the worn pieces' stand-in meshes and materials, one of each shape and colour, for its views.
/// </summary>
public sealed class EntityViewCatalog : IDisposable
{
    private const float StandInSmoothness = 0.2f;

    private static readonly Color PlaceholderColor = new(1f, 0f, 1f);
    private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");

    private readonly Dictionary<string, Entry> m_entries = new(StringComparer.Ordinal);
    private readonly Dictionary<StandInShape, Mesh> m_standInMeshes = new();
    private readonly Dictionary<Color, Material> m_standInMaterials = new();
    private Material? m_placeholderMaterial;
    private bool m_isDisposed;

    public void Dispose()
    {
        if (m_isDisposed)
        {
            return;
        }

        m_isDisposed = true;
        foreach (Entry entry in m_entries.Values)
        {
            entry.Callbacks.Clear();
            if (entry.Asset.IsValid())
            {
                Addressables.Release(entry.Asset);
            }
        }

        m_entries.Clear();
        if (m_placeholderMaterial != null)
        {
            Object.Destroy(m_placeholderMaterial);
        }

        foreach (Mesh mesh in m_standInMeshes.Values)
        {
            Object.Destroy(mesh);
        }

        foreach (Material material in m_standInMaterials.Values)
        {
            Object.Destroy(material);
        }

        m_standInMeshes.Clear();
        m_standInMaterials.Clear();
    }

    /// <summary>
    ///     Calls <paramref name="onReady" /> with the prefab for <paramref name="key" />, or with null when the key
    ///     does not resolve. The call may happen immediately or on a later frame.
    /// </summary>
    public void Request(string key, Action<GameObject?> onReady)
    {
        if (m_isDisposed)
        {
            return;
        }

        if (key.Length == 0)
        {
            onReady(null);
            return;
        }

        if (!m_entries.TryGetValue(key, out Entry? entry))
        {
            entry = new Entry();
            m_entries.Add(key, entry);
            entry.Callbacks.Add(onReady);
            Load(key, entry);
            return;
        }

        if (entry.State == EntryState.Loading)
        {
            entry.Callbacks.Add(onReady);
            return;
        }

        onReady(entry.Prefab);
    }

    public Material GetPlaceholderMaterial()
    {
        if (m_placeholderMaterial == null)
        {
            m_placeholderMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                color = PlaceholderColor
            };
        }

        return m_placeholderMaterial;
    }

    /// <summary>
    ///     The unit mesh of a stand-in's <paramref name="shape" />, made once (<see cref="WornStandIns.CreateMesh" />).
    /// </summary>
    public Mesh GetStandInMesh(StandInShape shape)
    {
        if (!m_standInMeshes.TryGetValue(shape, out Mesh? mesh))
        {
            mesh = WornStandIns.CreateMesh(shape);
            m_standInMeshes.Add(shape, mesh);
        }

        return mesh;
    }

    /// <summary>
    ///     A matte lit material of <paramref name="colour" /> for stand-ins, made once per colour.
    /// </summary>
    public Material GetStandInMaterial(Color colour)
    {
        if (!m_standInMaterials.TryGetValue(colour, out Material? material))
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = colour };
            material.SetFloat(SmoothnessId, StandInSmoothness);
            m_standInMaterials.Add(colour, material);
        }

        return material;
    }

    private void Load(string key, Entry entry)
    {
        // Loading an unknown key throws inside Addressables and logs an exception; asking for its locations first
        // returns an empty list instead.
        AsyncOperationHandle<IList<IResourceLocation>> locations =
            Addressables.LoadResourceLocationsAsync(key, typeof(GameObject));
        locations.Completed += handle =>
        {
            IResourceLocation? location = handle.Status == AsyncOperationStatus.Succeeded && handle.Result.Count > 0
                ? handle.Result[0]
                : null;
            Addressables.Release(handle);
            if (m_isDisposed)
            {
                return;
            }

            if (location == null)
            {
                Finish(key, entry, null);
                return;
            }

            entry.Asset = Addressables.LoadAssetAsync<GameObject>(location);
            entry.Asset.Completed += asset =>
            {
                if (!m_isDisposed)
                {
                    Finish(key, entry, asset.Status == AsyncOperationStatus.Succeeded ? asset.Result : null);
                }
            };
        };
    }

    private static void Finish(string key, Entry entry, GameObject? prefab)
    {
        entry.Prefab = prefab;
        entry.State = prefab == null ? EntryState.Missing : EntryState.Ready;
        if (prefab == null)
        {
            Debug.LogWarning($"EntityViewKeyMissing: Addressables key '{key}' did not resolve; showing a placeholder.");
        }

        var callbacks = new List<Action<GameObject?>>(entry.Callbacks);
        entry.Callbacks.Clear();
        foreach (Action<GameObject?> callback in callbacks)
        {
            callback(prefab);
        }
    }

    private enum EntryState
    {
        Loading,
        Ready,
        Missing
    }

    private sealed class Entry
    {
        public EntryState State { get; set; } = EntryState.Loading;

        public GameObject? Prefab { get; set; }

        public AsyncOperationHandle<GameObject> Asset { get; set; }

        public List<Action<GameObject?>> Callbacks { get; } = new();
    }
}
}
