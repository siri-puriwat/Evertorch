using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Evertorch.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The trade window (Gameplay Systems §16; Prototype Content §2), in the side slot while a trade is open: the
///     partner's offer and coins, the player's own, an amount field whose number a press on a bag row offers, a coin
///     field, and Lock, Trade, and Cancel. Every press asks the server through <see cref="GameClient" />; only the sides
///     the server sends change what it shows.
/// </summary>
public sealed class TradeWindow : MonoBehaviour
{
    public const string Lock = "Lock";
    public const string Confirm = "Trade";
    public const string Cancel = "Cancel";
    public const string OfferCoins = "Offer coins";
    public const string AmountField = "Amount";
    public const string CoinsField = "Coins";

    // With the side windows, under the chat and the login panel.
    private const int SortingOrder = 6;
    private const float Width = 420f;
    private const float Margin = 8f;
    private const float StatusBarHeight = 56f;
    private const int Padding = 10;
    private const float RowHeight = 30f;
    private const float RowSpacing = 6f;
    private const float ButtonWidth = 110f;
    private const float StateWidth = 110f;

    private static readonly UiBuilder Ui = new(20f, RowHeight, 0f, RowSpacing);

    private readonly StringBuilder m_text = new();
    private GameClient? m_client;
    private GameObject? m_panel;
    private TMP_Text? m_title;
    private TMP_Text? m_theirs;
    private TMP_Text? m_theirState;
    private TMP_Text? m_ours;
    private TMP_Text? m_ourState;
    private TMP_InputField? m_amount;
    private TMP_InputField? m_coins;
    private Button? m_lock;
    private Button? m_confirm;
    private float m_left = Margin;
    private TradeSide? m_shownOwn;
    private TradeSide? m_shownTheirs;
    private string? m_shownPartner;
    private bool m_hadContent;

    public bool IsOpen => m_panel != null && m_panel.activeSelf;

    /// <summary>
    ///     The window as shown, one line each: the title, the partner's state and lines, the player's state and lines.
    /// </summary>
    public string Text { get; private set; } = string.Empty;

    /// <summary>
    ///     The amount field's number: how many of a bag row a press offers; at least 1.
    /// </summary>
    public uint Amount =>
        m_amount != null &&
        uint.TryParse(m_amount.text, NumberStyles.None, CultureInfo.InvariantCulture, out uint amount)
        && amount > 0
            ? amount
            : 1;

    public TMP_InputField? AmountInput => m_amount;

    public TMP_InputField? CoinsInput => m_coins;

    private void Update()
    {
        ClientWorld? world = m_client != null ? m_client.World : null;
        bool isTrading = world != null && world.Trade.IsOpen;
        if (isTrading != IsOpen)
        {
            UiBuilder.SetActive(m_panel!, isTrading);
            if (!isTrading)
            {
                // A field that had focus when the trade ended gives the keys back.
                m_client?.SetTyping(m_amount!, false);
                m_client?.SetTyping(m_coins!, false);
                m_shownPartner = null;
                return;
            }

            m_amount!.text = "1";
            m_coins!.text = string.Empty;
        }

        if (!isTrading)
        {
            return;
        }

        PlaceLeft(m_client!.Touch != null && m_client.Touch.IsVisible);
        Show(world!.Trade, m_client.Content);
    }

    public static TradeWindow Create(GameClient client)
    {
        var root = new GameObject("TradeWindow", typeof(RectTransform));
        root.SetActive(false);
        TradeWindow window = root.AddComponent<TradeWindow>();
        window.Build(client);
        root.SetActive(true);
        return window;
    }

    // Rewrites the lines only when a side, the partner, or the names to show changed.
    private void Show(ClientTrade trade, ClientContent? content)
    {
        bool hasContent = content != null;
        if (trade.Own == m_shownOwn
            && trade.Theirs == m_shownTheirs
            && trade.Partner == m_shownPartner
            && hasContent == m_hadContent)
        {
            return;
        }

        m_shownOwn = trade.Own;
        m_shownTheirs = trade.Theirs;
        m_shownPartner = trade.Partner;
        m_hadContent = hasContent;
        string partner = trade.Partner ?? string.Empty;
        m_title!.text = $"Trading with {partner}";
        m_theirState!.text = TradeMessages.State(trade.Theirs);
        m_ourState!.text = TradeMessages.State(trade.Own);
        m_theirs!.text = Lines(TradeMessages.Lines(trade.Theirs, content));
        m_ours!.text = Lines(TradeMessages.Lines(trade.Own, content));
        m_lock!.interactable = !trade.Own.IsLocked;
        m_confirm!.interactable = trade.Own.IsLocked && trade.Theirs.IsLocked && !trade.Own.IsConfirmed;

        m_text.Clear();
        m_text.Append(m_title.text);
        m_text.Append('\n').Append($"{partner} offers: {m_theirState.text}".TrimEnd(' ', ':'));
        AppendLines(trade.Theirs, content);
        m_text.Append('\n').Append($"You offer: {m_ourState.text}".TrimEnd(' ', ':'));
        AppendLines(trade.Own, content);
        Text = m_text.ToString();
    }

    private void AppendLines(TradeSide side, ClientContent? content)
    {
        foreach (string line in TradeMessages.Lines(side, content))
        {
            m_text.Append('\n').Append(line);
        }
    }

    private static string Lines(IReadOnlyList<string> lines)
    {
        return lines.Count == 0 ? "Nothing yet" : string.Join("\n", lines);
    }

    // Beside the touch window buttons while they show, as the other side windows are.
    private void PlaceLeft(bool isTouchShown)
    {
        float left = NpcWindow.LeftFor(isTouchShown);
        if (left != m_left)
        {
            m_left = left;
            var panel = (RectTransform)m_panel!.transform;
            panel.anchoredPosition = new Vector2(left, panel.anchoredPosition.y);
        }
    }

    private void OfferTheCoins()
    {
        uint coins = m_coins != null
            && uint.TryParse(m_coins.text, NumberStyles.None, CultureInfo.InvariantCulture, out uint typed)
                ? typed
                : 0;
        m_client!.OfferCoinsInTrade(coins);
    }

    private void Build(GameClient client)
    {
        m_client = client;
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        RectTransform panel = Ui.CreatePanel(
            transform,
            new Vector2(0f, 1f),
            new Vector2(Margin, -(StatusBarHeight + Margin)),
            Width,
            Padding);
        m_panel = panel.gameObject;

        GameObject heading = Ui.CreateRow("Heading", panel);
        m_title = Ui.CreateLabel("Title", heading.transform);
        m_title.fontStyle = FontStyles.Bold;
        m_title.textWrappingMode = TextWrappingModes.NoWrap;
        m_title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        Ui.CreateButton(Cancel, heading.transform, () => client.CancelTrade())
            .GetComponent<LayoutElement>().preferredWidth = ButtonWidth;

        m_theirState = CreateSideHeading(panel, "Theirs", "Their offer");
        m_theirs = CreateLines(panel, "Their lines");
        m_ourState = CreateSideHeading(panel, "Ours", "Your offer");
        m_ours = CreateLines(panel, "Your lines");

        m_amount = Ui.CreateField(panel, AmountField, "1", TMP_InputField.ContentType.IntegerNumber);
        WatchFocus(m_amount);
        m_coins = Ui.CreateField(panel, CoinsField, string.Empty, TMP_InputField.ContentType.IntegerNumber);
        m_coins.characterLimit = 10;
        WatchFocus(m_coins);

        GameObject buttons = Ui.CreateRow("Buttons", panel);
        Ui.CreateButton(OfferCoins, buttons.transform, OfferTheCoins);
        m_lock = Ui.CreateButton(Lock, buttons.transform, () => client.LockTrade()).GetComponent<Button>();
        m_confirm = Ui.CreateButton(Confirm, buttons.transform, () => client.ConfirmTrade()).GetComponent<Button>();

        TMP_Text hint = Ui.CreateLabel("Hint", panel);
        hint.text = "Press a bag row to offer the amount above.";
        hint.fontSize = 16f;
        hint.gameObject.AddComponent<LayoutElement>().preferredHeight = RowHeight;

        m_panel.SetActive(false);
    }

    private static TMP_Text CreateSideHeading(RectTransform panel, string objectName, string title)
    {
        GameObject row = Ui.CreateRow(objectName, panel);
        TMP_Text label = Ui.CreateLabel("Title", row.transform);
        label.text = title;
        label.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        TMP_Text state = Ui.CreateLabel("State", row.transform);
        state.alignment = TextAlignmentOptions.MidlineRight;
        state.gameObject.AddComponent<LayoutElement>().preferredWidth = StateWidth;
        return state;
    }

    private static TMP_Text CreateLines(RectTransform panel, string objectName)
    {
        TMP_Text lines = Ui.CreateLabel(objectName, panel);
        lines.alignment = TextAlignmentOptions.TopLeft;
        lines.textWrappingMode = TextWrappingModes.NoWrap;
        lines.enableAutoSizing = true;
        lines.fontSizeMin = 12f;
        lines.fontSizeMax = lines.fontSize;
        lines.gameObject.AddComponent<LayoutElement>().preferredHeight = 4 * RowHeight;
        return lines;
    }

    // While a field has focus the gameplay keys are shut, as the chat's are (Prototype Content §4).
    private void WatchFocus(TMP_InputField field)
    {
        field.onSelect.AddListener(_ => m_client!.SetTyping(field, true));
        field.onDeselect.AddListener(_ => m_client!.SetTyping(field, false));
    }
}
}
