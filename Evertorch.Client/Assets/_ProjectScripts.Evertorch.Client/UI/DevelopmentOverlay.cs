using System;
using System.Collections.Generic;
using System.Text;
using Evertorch.Game;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     Development-only readout and controls: connection state, prediction health, and the simulated link quality.
///     F1 hides it.
/// </summary>
public sealed class DevelopmentOverlay : MonoBehaviour
{
    // Drawn over the on-screen stick, whose canvas keeps the default order.
    private const int SortingOrder = 10;
    private const float Width = 380f;
    private const float Margin = 10f;
    private const int Padding = 8;
    private const float Spacing = 4f;
    private const float RowHeight = 26f;
    private const float LabelWidth = 120f;
    private const float FontSize = 14f;
    private const float HandleWidth = 10f;
    private const int FieldCharacterLimit = 64;

    // The naming policy's longest name; the server enforces the rest.
    private const int CharacterNamePolicyLength = 23;

    private static readonly Color PanelColor = new(0.08f, 0.09f, 0.11f, 0.85f);
    private static readonly Color ControlColor = new(0.24f, 0.27f, 0.32f, 1f);
    private static readonly Color AccentColor = new(0.25f, 0.65f, 0.95f);
    private static readonly Color TextColor = new(0.92f, 0.94f, 0.96f);

    // A clicked control is never selected: the UI navigate action shares WASD, the arrow keys, and the gamepad stick
    // with movement, so a selected slider would change while the player walks.
    private static readonly Navigation NoNavigation = new() { mode = Navigation.Mode.None };

    private View? m_view;

    public RectTransform? Panel { get; private set; }

    public bool IsVisible => Panel != null && Panel.gameObject.activeSelf;

    private void Update()
    {
        Keyboard? keyboard = Keyboard.current;
        if (Panel != null && keyboard != null && keyboard.f1Key.wasPressedThisFrame)
        {
            Panel.gameObject.SetActive(!Panel.gameObject.activeSelf);
        }

        if (IsVisible)
        {
            m_view?.Refresh();
        }
    }

    public static DevelopmentOverlay Create(GameClient client)
    {
        // Built inactive, so every control is fully wired before any of them is enabled.
        var root = new GameObject("DevelopmentOverlay", typeof(RectTransform));
        root.SetActive(false);
        DevelopmentOverlay overlay = root.AddComponent<DevelopmentOverlay>();
        overlay.Build(client);
        root.SetActive(true);
        return overlay;
    }

    private void Build(GameClient client)
    {
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        GameObject panel = CreateUiObject("Panel", transform);
        Panel = (RectTransform)panel.transform;
        Panel.anchorMin = new Vector2(0f, 1f);
        Panel.anchorMax = new Vector2(0f, 1f);
        Panel.pivot = new Vector2(0f, 1f);
        Panel.anchoredPosition = new Vector2(Margin, -Margin);
        Panel.sizeDelta = new Vector2(Width, 0f);
        panel.AddComponent<Image>().color = PanelColor;
        AddColumnLayout(panel, Padding);
        panel.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        m_view = new View(client, panel.transform);
        m_view.Refresh();
    }

    private static GameObject CreateUiObject(string objectName, Transform parent)
    {
        var uiObject = new GameObject(objectName, typeof(RectTransform));
        uiObject.transform.SetParent(parent, false);
        return uiObject;
    }

    private static RectTransform Stretch(GameObject target, float insetX, float insetY)
    {
        var rect = (RectTransform)target.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(insetX, insetY);
        rect.offsetMax = new Vector2(-insetX, -insetY);
        return rect;
    }

    private static void AddColumnLayout(GameObject target, int padding)
    {
        VerticalLayoutGroup layout = target.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(padding, padding, padding, padding);
        layout.spacing = Spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private static GameObject CreateColumn(string objectName, Transform parent)
    {
        GameObject column = CreateUiObject(objectName, parent);
        AddColumnLayout(column, 0);
        return column;
    }

    private static GameObject CreateRow(string objectName, Transform parent)
    {
        GameObject row = CreateUiObject(objectName, parent);
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = Spacing;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;
        row.AddComponent<LayoutElement>().preferredHeight = RowHeight;
        return row;
    }

    private static Image CreateImage(string objectName, Transform parent, Color color, float insetX, float insetY)
    {
        GameObject imageObject = CreateUiObject(objectName, parent);
        Stretch(imageObject, insetX, insetY);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    private static TMP_Text CreateLabel(string objectName, Transform parent)
    {
        TextMeshProUGUI label = CreateUiObject(objectName, parent).AddComponent<TextMeshProUGUI>();
        label.fontSize = FontSize;
        label.color = TextColor;
        label.raycastTarget = false;
        return label;
    }

    private static TMP_Text CreateRowLabel(string text, Transform row)
    {
        TMP_Text label = CreateLabel("Label", row);
        label.text = text;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.gameObject.AddComponent<LayoutElement>().preferredWidth = LabelWidth;
        return label;
    }

    private static GameObject CreateButton(string label, Transform parent, UnityAction onClick)
    {
        GameObject buttonObject = CreateUiObject(label, parent);
        buttonObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;
        Image background = buttonObject.AddComponent<Image>();
        background.color = ControlColor;
        Button button = buttonObject.AddComponent<Button>();
        button.navigation = NoNavigation;
        button.targetGraphic = background;
        button.onClick.AddListener(onClick);

        TMP_Text text = CreateLabel("Label", buttonObject.transform);
        Stretch(text.gameObject, 0f, 0f);
        text.text = label;
        text.alignment = TextAlignmentOptions.Center;
        return buttonObject;
    }

    private static TMP_InputField CreateField(
        Transform parent,
        string label,
        string value,
        TMP_InputField.ContentType contentType)
    {
        GameObject row = CreateRow(label, parent);
        CreateRowLabel(label, row.transform);

        GameObject fieldObject = CreateUiObject("Field", row.transform);
        fieldObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Image background = fieldObject.AddComponent<Image>();
        background.color = ControlColor;
        GameObject area = CreateUiObject("Text Area", fieldObject.transform);
        area.AddComponent<RectMask2D>();
        RectTransform viewport = Stretch(area, 6f, 2f);
        TMP_Text text = CreateLabel("Text", area.transform);
        Stretch(text.gameObject, 0f, 0f);
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.textWrappingMode = TextWrappingModes.NoWrap;

        TMP_InputField field = fieldObject.AddComponent<TMP_InputField>();
        field.targetGraphic = background;
        field.textViewport = viewport;
        field.textComponent = text;
        field.customCaretColor = true;
        field.caretColor = TextColor;
        field.contentType = contentType;
        field.characterLimit = FieldCharacterLimit;
        field.text = value;
        return field;
    }

    private static Slider CreateSlider(Transform parent, int max)
    {
        GameObject sliderObject = CreateUiObject("Slider", parent);
        sliderObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        CreateImage("Background", sliderObject.transform, ControlColor, 0f, 8f);
        GameObject fillArea = CreateUiObject("Fill Area", sliderObject.transform);
        Stretch(fillArea, HandleWidth / 2f, 8f);
        Image fill = CreateImage("Fill", fillArea.transform, AccentColor, 0f, 0f);
        GameObject handleArea = CreateUiObject("Handle Area", sliderObject.transform);
        Stretch(handleArea, HandleWidth / 2f, 0f);
        Image handle = CreateImage("Handle", handleArea.transform, TextColor, 0f, 0f);
        handle.rectTransform.sizeDelta = new Vector2(HandleWidth, 0f);

        Slider slider = sliderObject.AddComponent<Slider>();
        slider.navigation = NoNavigation;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.wholeNumbers = true;
        slider.maxValue = max;
        return slider;
    }

    private static Toggle CreateToggle(Transform parent, string label, UnityAction<bool> onChanged)
    {
        GameObject row = CreateRow(label, parent);
        GameObject box = CreateUiObject("Box", row.transform);
        box.AddComponent<LayoutElement>().preferredWidth = RowHeight;
        Image background = box.AddComponent<Image>();
        background.color = ControlColor;
        Image check = CreateImage("Check", box.transform, AccentColor, 6f, 6f);
        TMP_Text text = CreateLabel("Label", row.transform);
        text.text = label;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.raycastTarget = true;

        Toggle toggle = row.AddComponent<Toggle>();
        toggle.navigation = NoNavigation;
        toggle.targetGraphic = background;
        toggle.graphic = check;
        toggle.onValueChanged.AddListener(onChanged);
        return toggle;
    }

    private sealed class View
    {
        private readonly GameClient m_client;
        private readonly TMP_Text m_connection;
        private readonly GameObject m_disconnect;
        private readonly GameObject m_respawn;
        private readonly GameObject m_logout;
        private readonly GameObject m_connectForm;
        private readonly TMP_InputField m_port;
        private readonly GameObject m_reconnect;
        private readonly GameObject m_characters;
        private readonly TMP_Text m_charactersLabel;
        private readonly GameObject[] m_enterButtons = new GameObject[CharacterList.MaxEntries];
        private readonly TMP_Text[] m_enterLabels = new TMP_Text[CharacterList.MaxEntries];
        private readonly TMP_InputField m_name;
        private readonly TMP_Text m_world;
        private readonly GameObject m_link;
        private readonly TMP_Text m_linkCounters;
        private readonly LinkSlider[] m_linkSliders;
        private readonly Toggle m_stick;

        public View(GameClient client, Transform panel)
        {
            m_client = client;
            m_connection = CreateLabel("Connection", panel);
            m_disconnect = CreateButton("Disconnect", panel, client.Disconnect);
            m_respawn = CreateButton("Respawn", panel, client.RequestRespawn);
            m_logout = CreateButton("Logout", panel, client.Logout);

            m_connectForm = CreateColumn("Connect", panel);
            Transform form = m_connectForm.transform;
            CreateField(form, "Host", client.Host, TMP_InputField.ContentType.Standard)
                .onValueChanged.AddListener(value => client.Host = value);
            m_port = CreateField(form, "Port", client.Port.ToString(), TMP_InputField.ContentType.IntegerNumber);
            CreateField(form, "Identity", client.Identity, TMP_InputField.ContentType.Standard)
                .onValueChanged.AddListener(value => client.Identity = value);
            CreateButton("Connect", form, Connect);
            m_reconnect = CreateButton("Reconnect", form, Reconnect);

            m_characters = CreateColumn("Characters", panel);
            Transform characters = m_characters.transform;
            m_charactersLabel = CreateLabel("Heading", characters);
            for (int slot = 0; slot < CharacterList.MaxEntries; slot++)
            {
                int chosen = slot;
                m_enterButtons[slot] = CreateButton($"Enter {slot + 1}", characters, () => Enter(chosen));
                m_enterLabels[slot] = m_enterButtons[slot].GetComponentInChildren<TMP_Text>();
            }

            m_name = CreateField(characters, "New name", string.Empty, TMP_InputField.ContentType.Alphanumeric);
            m_name.characterLimit = CharacterNamePolicyLength;
            CreateButton("Create character", characters, () => client.CreateCharacter(m_name.text));

            m_world = CreateLabel("World", panel);

            m_link = CreateColumn("Link", panel);
            Transform link = m_link.transform;
            m_linkCounters = CreateLabel("Counters", link);
            m_linkSliders = new[]
            {
                new LinkSlider(
                    link,
                    "Latency ms",
                    300,
                    client,
                    transport => transport.LatencyMilliseconds,
                    (transport, value) => transport.LatencyMilliseconds = value),
                new LinkSlider(
                    link,
                    "Jitter ms",
                    100,
                    client,
                    transport => transport.JitterMilliseconds,
                    (transport, value) => transport.JitterMilliseconds = value),
                new LinkSlider(
                    link,
                    "Loss %",
                    30,
                    client,
                    transport => transport.LossPercent,
                    (transport, value) => transport.LossPercent = value),
                new LinkSlider(
                    link,
                    "Reorder %",
                    30,
                    client,
                    transport => transport.ReorderPercent,
                    (transport, value) => transport.ReorderPercent = value)
            };

            m_stick = CreateToggle(panel, "On-screen stick", isShown => client.Touch?.SetVisible(isShown));
        }

        public void Refresh()
        {
            ClientConnection? connection = m_client.Connection;
            bool isOpen = connection != null && connection.State != ClientConnectionState.Disconnected;
            m_connection.text = connection == null
                ? m_client.Status
                : $"{m_client.Status}\nState {connection.State}   RTT {connection.RoundTripMilliseconds} ms"
                + $"   Server {connection.ServerBuildVersion}"
                + $"\nMalformed {connection.MalformedMessages}   Unexpected {connection.UnexpectedMessages}";
            m_disconnect.SetActive(isOpen);
            m_respawn.SetActive(m_client.World?.IsLocalDead == true);
            m_logout.SetActive(connection != null && connection.State == ClientConnectionState.InWorld);
            m_connectForm.SetActive(!isOpen);
            m_reconnect.SetActive(m_client.CanReconnect);
            RefreshCharacters(connection);
            RefreshWorld();
            RefreshLink();

            TouchControls? touch = m_client.Touch;
            m_stick.gameObject.SetActive(touch != null);
            if (touch != null)
            {
                m_stick.SetIsOnWithoutNotify(touch.IsVisible);
            }
        }

        private void Connect()
        {
            if (int.TryParse(m_port.text, out int port))
            {
                m_client.Port = port;
                m_client.Connect();
            }
        }

        private void Reconnect()
        {
            if (int.TryParse(m_port.text, out int port))
            {
                m_client.Port = port;
                m_client.Reconnect();
            }
        }

        private void Enter(int slot)
        {
            IReadOnlyList<CharacterListEntry>? characters = m_client.Connection?.Characters;
            if (characters != null && slot < characters.Count)
            {
                m_client.EnterWorld(characters[slot].Character);
            }
        }

        private void RefreshCharacters(ClientConnection? connection)
        {
            bool isSelecting = connection != null && connection.IsOnCharacterList;
            m_characters.SetActive(isSelecting);
            if (!isSelecting)
            {
                return;
            }

            IReadOnlyList<CharacterListEntry> characters = connection!.Characters;
            m_charactersLabel.text =
                $"Characters of {m_client.Identity} ({characters.Count}/{CharacterList.MaxEntries})";
            for (int slot = 0; slot < m_enterButtons.Length; slot++)
            {
                bool hasCharacter = slot < characters.Count;
                m_enterButtons[slot].SetActive(hasCharacter);
                if (hasCharacter)
                {
                    CharacterListEntry character = characters[slot];
                    m_enterLabels[slot].text = $"Enter {character.Name} (level {character.BaseLevel})";
                }
            }
        }

        private void RefreshWorld()
        {
            ClientWorld? world = m_client.World;
            m_world.gameObject.SetActive(world != null);
            if (world == null)
            {
                return;
            }

            MovementPredictor predictor = world.Predictor;
            WorldPosition position = predictor.Position;
            RenderSmoother smoother = world.Smoother;
            string life = world.IsLocalDead ? "   dead" : string.Empty;
            string text = $"HP {world.LocalHealth}/{world.LocalMaximumHealth}{life}"
                + $"\nTick {world.LatestServerTick}   Pos {position.X:F2}, {position.Y:F2}, {position.Z:F2}"
                + $"\nPending {predictor.PendingCount}   Ack {predictor.LastAcknowledgedSequence}"
                + $"   Dropped {predictor.DroppedPendingInputs}"
                + $"\nCorrection last {smoother.LastCorrection:F3} m   max {smoother.LargestCorrection:F3} m"
                + $"   snaps {smoother.Snaps}"
                + $"\nSnapshots {world.SnapshotsApplied}   stale {world.StaleSnapshots}"
                + $"   unknown states {world.UnknownEntityStates}   remotes {world.Remotes.Count}";
            MovementController? controller = m_client.Controller;
            if (controller != null)
            {
                text += $"\nWalk {(controller.HasPath ? "active" : "none")}"
                    + $"   refused clicks {controller.RejectedMoveRequests}   cancelled {controller.CancelledPaths}";
            }

            if (world.LastRejection != CommandRejectionReason.None)
            {
                text += $"\nLast refused command: {world.LastRejection}";
            }

            text += InventoryText(world.Inventory);

            if (m_client.Clock != null && m_client.Clock.SkippedTicks > 0)
            {
                text += $"\nSkipped client ticks {m_client.Clock.SkippedTicks}";
            }

            m_world.text = text;
        }

        // A placeholder list until the game has an inventory window.
        private string InventoryText(ClientInventory inventory)
        {
            if (!inventory.IsCurrent)
            {
                return "\nInventory: waiting for the server";
            }

            var text = new StringBuilder($"\nInventory (revision {inventory.Revision})");
            if (inventory.Rows.Count == 0)
            {
                text.Append(": empty");
            }

            foreach (InventoryEntry row in inventory.Rows)
            {
                string name = m_client.Content != null
                    && m_client.Content.TryGetItem(row.Item, out ClientItem? item)
                    && item != null
                        ? item.DisplayName
                        : row.Item.Value;
                text.Append($"\n  {name} x {row.Quantity}");
            }

            return text.ToString();
        }

        private void RefreshLink()
        {
            LossyTransport? link = m_client.Link;
            m_link.SetActive(link != null);
            if (link == null)
            {
                return;
            }

            m_linkCounters.text =
                $"Simulated link (each way)   dropped {link.Dropped}   reordered {link.Reordered}";
            foreach (LinkSlider slider in m_linkSliders)
            {
                slider.Show(link);
            }
        }
    }

    private sealed class LinkSlider
    {
        private readonly string m_name;
        private readonly Func<LossyTransport, int> m_read;
        private readonly Slider m_slider;
        private readonly TMP_Text m_label;
        private int m_shown = -1;

        public LinkSlider(
            Transform parent,
            string name,
            int max,
            GameClient client,
            Func<LossyTransport, int> read,
            Action<LossyTransport, int> write)
        {
            m_name = name;
            m_read = read;
            GameObject row = CreateRow(name, parent);
            m_label = CreateRowLabel(name, row.transform);
            m_slider = CreateSlider(row.transform, max);
            m_slider.onValueChanged.AddListener(value =>
            {
                // Every connect replaces the link, so the current one is looked up on each change.
                LossyTransport? link = client.Link;
                if (link != null)
                {
                    write(link, Mathf.RoundToInt(value));
                }
            });
        }

        public void Show(LossyTransport link)
        {
            int value = m_read(link);
            if (value == m_shown)
            {
                return;
            }

            m_shown = value;
            m_slider.SetValueWithoutNotify(value);
            m_label.text = $"{m_name} {value}";
        }
    }
}
}
