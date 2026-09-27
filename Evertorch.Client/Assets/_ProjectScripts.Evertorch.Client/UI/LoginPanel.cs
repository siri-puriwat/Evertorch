using System;
using System.Collections.Generic;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;

namespace Evertorch.Client
{
/// <summary>
///     What a player sees while not in the world (Prototype Content §2): the login, the password (masked), and the
///     gateway's host and port with Connect; the text of the last sign-in or disconnect, with Reconnect when it can
///     help; and the character selection once signed in (§4). The login and password stay in memory only: PlayerPrefs
///     would be shared by the Multiplayer Play Mode clones, and a password is never stored.
/// </summary>
public sealed class LoginPanel : MonoBehaviour
{
    // Over the status bar and the stick, under the development overlay.
    private const int SortingOrder = 8;
    private const float Width = 520f;
    private const float Margin = 16f;
    private const int Padding = 12;

    // The naming policy's longest name; the server enforces the rest.
    private const int CharacterNamePolicyLength = 23;

    private static readonly UiBuilder Ui = new(22f, 44f, 120f, 6f);

    private readonly GameObject[] m_enterButtons = new GameObject[CharacterList.MaxEntries];
    private readonly TMP_Text[] m_enterLabels = new TMP_Text[CharacterList.MaxEntries];
    private readonly CharacterListEntry[] m_shownEntries = new CharacterListEntry[CharacterList.MaxEntries];
    private GameClient? m_client;
    private GameObject? m_panel;
    private TMP_Text? m_message;
    private GameObject? m_connectForm;
    private TMP_InputField? m_port;
    private GameObject? m_reconnect;
    private GameObject? m_characters;
    private TMP_Text? m_heading;
    private TMP_InputField? m_name;
    private string? m_shownLogin;
    private int m_shownCount = -1;

    public bool IsVisible => m_panel != null && m_panel.activeSelf;

    private void Update()
    {
        if (m_client != null)
        {
            Refresh(m_client);
        }
    }

    public static LoginPanel Create(GameClient client)
    {
        // Built inactive, so every control is fully wired before any of them is enabled.
        var root = new GameObject("LoginPanel", typeof(RectTransform));
        root.SetActive(false);
        LoginPanel panel = root.AddComponent<LoginPanel>();
        panel.Build(client);
        root.SetActive(true);
        return panel;
    }

    private void Build(GameClient client)
    {
        m_client = client;
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        RectTransform panel =
            Ui.CreatePanel(transform, new Vector2(0.5f, 1f), new Vector2(0f, -Margin), Width, Padding);
        m_panel = panel.gameObject;
        m_message = Ui.CreateLabel("Message", panel);
        m_message.textWrappingMode = TextWrappingModes.Normal;

        m_connectForm = Ui.CreateColumn("Connect", panel);
        Transform form = m_connectForm.transform;
        Ui.CreateField(form, "Login", client.Login, TMP_InputField.ContentType.Standard)
            .onValueChanged.AddListener(value => client.Login = value);
        Ui.CreateField(form, "Password", string.Empty, TMP_InputField.ContentType.Password)
            .onValueChanged.AddListener(value => client.Password = value);
        Ui.CreateField(form, "Host", client.Host, TMP_InputField.ContentType.Standard)
            .onValueChanged.AddListener(value => client.Host = value);
        m_port = Ui.CreateField(form, "Port", client.Port.ToString(), TMP_InputField.ContentType.IntegerNumber);
        Ui.CreateButton("Connect", form, Connect);
        m_reconnect = Ui.CreateButton("Reconnect", form, Reconnect);

        m_characters = Ui.CreateColumn("Characters", panel);
        Transform characters = m_characters.transform;
        m_heading = Ui.CreateLabel("Heading", characters);
        for (int slot = 0; slot < CharacterList.MaxEntries; slot++)
        {
            int chosen = slot;
            m_enterButtons[slot] = Ui.CreateButton($"Enter {slot + 1}", characters, () => Enter(chosen));
            m_enterLabels[slot] = m_enterButtons[slot].GetComponentInChildren<TMP_Text>();
        }

        m_name = Ui.CreateField(characters, "New name", string.Empty, TMP_InputField.ContentType.Alphanumeric);
        m_name.characterLimit = CharacterNamePolicyLength;
        Ui.CreateButton("Create character", characters, () => client.CreateCharacter(m_name.text));
        Refresh(client);
    }

    // Hidden once the world is drawn, and through a map change. Until then it says what the client is doing, and the
    // list stays while an entry is unanswered, because a refused one never is (Network Protocol §4).
    private void Refresh(GameClient client)
    {
        bool isInWorld = client.IsInWorld;
        UiBuilder.SetActive(m_panel!, !isInWorld);
        if (isInWorld)
        {
            return;
        }

        ClientConnection? connection = client.Connection;
        bool isOpen = connection != null && connection.State != ClientConnectionState.Disconnected;
        UiBuilder.SetText(m_message!, client.Status);
        UiBuilder.SetActive(m_connectForm!, !isOpen && !client.IsSigningIn);
        UiBuilder.SetActive(m_reconnect!, client.CanReconnect);
        RefreshCharacters(client, connection);
    }

    private void RefreshCharacters(GameClient client, ClientConnection? connection)
    {
        bool isSelecting = connection != null && connection.IsOnCharacterList;
        UiBuilder.SetActive(m_characters!, isSelecting);
        if (!isSelecting)
        {
            return;
        }

        IReadOnlyList<CharacterListEntry> characters = connection!.Characters;
        if (!ReferenceEquals(m_shownLogin, client.Login) || m_shownCount != characters.Count)
        {
            m_shownLogin = client.Login;
            m_shownCount = characters.Count;
            m_heading!.text = $"Characters of {client.Login} ({characters.Count}/{CharacterList.MaxEntries})";
        }

        for (int slot = 0; slot < m_enterButtons.Length; slot++)
        {
            bool hasCharacter = slot < characters.Count;
            UiBuilder.SetActive(m_enterButtons[slot], hasCharacter);
            if (hasCharacter && !IsShown(slot, characters[slot]))
            {
                CharacterListEntry character = characters[slot];
                m_shownEntries[slot] = character;
                m_enterLabels[slot].text = $"Enter {character.Name} (level {character.BaseLevel})";
            }
        }
    }

    private bool IsShown(int slot, CharacterListEntry character)
    {
        CharacterListEntry shown = m_shownEntries[slot];
        return shown.Character == character.Character
            && shown.BaseLevel == character.BaseLevel
            && string.Equals(shown.Name, character.Name, StringComparison.Ordinal);
    }

    private void Connect()
    {
        if (m_client != null && int.TryParse(m_port!.text, out int port))
        {
            m_client.Port = port;
            m_client.Connect();
        }
    }

    private void Reconnect()
    {
        if (m_client != null && int.TryParse(m_port!.text, out int port))
        {
            m_client.Port = port;
            m_client.Reconnect();
        }
    }

    private void Enter(int slot)
    {
        IReadOnlyList<CharacterListEntry>? characters = m_client?.Connection?.Characters;
        if (m_client != null && characters != null && slot < characters.Count)
        {
            m_client.EnterWorld(characters[slot].Character);
        }
    }
}
}
