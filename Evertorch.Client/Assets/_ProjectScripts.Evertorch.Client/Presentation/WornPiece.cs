using System;
using System.Collections.Generic;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     A worn piece's model (Prototype Content §2.1): its rigid parts, each hung at no offset on the body socket it
///     names, and the material slot of each that takes the player's tint. The art importer builds it from a delivery's
///     manifest.
/// </summary>
public sealed class WornPiece : MonoBehaviour
{
    /// <summary>
    ///     The socket the first accessory's parts name; the second accessory hangs the same model on
    ///     <see cref="SecondAccessorySocket" />.
    /// </summary>
    public const string FirstAccessorySocket = "Socket_Hip_L";

    public const string SecondAccessorySocket = "Socket_Hip_R";

    [SerializeField]
    private Part[] m_parts = Array.Empty<Part>();

    public IReadOnlyList<Part> Parts => m_parts;

    public void Configure(Part[] parts)
    {
        m_parts = parts ?? throw new ArgumentNullException(nameof(parts));
    }

    /// <summary>
    ///     One rigid part: its node, the socket it hangs from, and its Trim slot (-1 when it has none).
    /// </summary>
    [Serializable]
    public struct Part
    {
        [SerializeField]
        private Transform? m_node;

        [SerializeField]
        private string m_socket;

        [SerializeField]
        private int m_trimSlot;

        public Part(Transform node, string socket, int trimSlot)
        {
            m_node = node;
            m_socket = socket;
            m_trimSlot = trimSlot;
        }

        public Transform? Node => m_node;

        public string Socket => m_socket ?? string.Empty;

        public int TrimSlot => m_trimSlot;
    }
}
}
