using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Evertorch.Client
{
/// <summary>
///     The question an invite or a trade's request asks (Prototype Content §2): "Anna invites you to a party." or
///     "Anna wants to trade with you." with Accept and Decline, the newer of the two while both wait, for the 30 s the
///     server keeps each. It stands beside the chat on the desktop and over the touch log, clear
///     of the target frame, the party list, and the side windows.
/// </summary>
public sealed class PartyInvitePrompt : MonoBehaviour
{
    public const string Accept = "Accept";
    public const string Decline = "Decline";

    // Over the panels it shares the screen with, under the login panel.
    private const int SortingOrder = 7;
    private const float FontSize = 16f;
    private const float ButtonWidth = 100f;
    private const float Inset = 6f;
    private const float Gap = 8f;

    /// <summary>
    ///     On the desktop, right of the chat and above the skill bar: x 548 to 764, y 128 to 228.
    /// </summary>
    public static readonly Rect DesktopBounds = new(548f, 128f, 216f, 100f);

    private static readonly UiBuilder Ui = new(FontSize, 32f, 0f, 0f);

    private GameClient? m_client;
    private GameObject? m_panel;
    private RectTransform? m_panelRect;
    private TMP_Text? m_question;
    private RectTransform? m_accept;
    private RectTransform? m_decline;
    private bool? m_isTouchLayout;
    private bool m_isTrade;

    public bool IsVisible => m_panel != null && m_panel.activeSelf;

    public string Text => m_question != null ? m_question.text : string.Empty;

    private void Update()
    {
        ClientParty? party = m_client != null ? m_client.Party : null;
        if (m_client == null || party == null)
        {
            return;
        }

        double now = Time.realtimeSinceStartupAsDouble;
        party.ExpireInvite(now);
        ClientTrade? trade = m_client.World?.Trade;
        trade?.ExpireRequest(now);
        bool isTrade = trade?.Requester != null && (party.Inviter == null || trade.RequestEndsAt > party.InviteEndsAt);
        bool isShown = m_client.World != null && (party.Inviter != null || isTrade);
        UiBuilder.SetActive(m_panel!, isShown);
        if (!isShown)
        {
            return;
        }

        m_isTrade = isTrade;

        bool isTouch = m_client.Touch != null && m_client.Touch.IsVisible;
        if (isTouch != m_isTouchLayout)
        {
            Place(isTouch);
        }

        UiBuilder.SetText(
            m_question!,
            isTrade ? TradeMessages.Request(trade!.Requester!) : PartyMessages.Invitation(party.Inviter!));
    }

    public static PartyInvitePrompt Create(GameClient client)
    {
        var root = new GameObject("PartyInvitePrompt", typeof(RectTransform));
        root.SetActive(false);
        PartyInvitePrompt prompt = root.AddComponent<PartyInvitePrompt>();
        prompt.Build(client);
        root.SetActive(true);
        return prompt;
    }

    /// <summary>
    ///     Where the prompt sits, from the canvas's bottom-left corner: beside the chat on the desktop, and with the touch
    ///     controls over the log's place, from x 226 to 762 and 8 above it, one line tall.
    /// </summary>
    public static Rect BoundsFor(bool isTouchShown)
    {
        if (!isTouchShown)
        {
            return DesktopBounds;
        }

        Rect log = ChatPanel.BoundsFor(true);
        return new Rect(log.xMin, log.yMax + Gap, log.width, 52f);
    }

    private void Build(GameClient client)
    {
        m_client = client;
        ClientUI.EnsureEventSystem(transform);
        ClientUI.AddScreenCanvas(gameObject, SortingOrder);

        GameObject panel = UiBuilder.CreateUiObject("Panel", transform);
        m_panel = panel;
        m_panelRect = (RectTransform)panel.transform;
        m_panelRect.anchorMin = Vector2.zero;
        m_panelRect.anchorMax = Vector2.zero;
        m_panelRect.pivot = Vector2.zero;
        panel.AddComponent<Image>().color = UiBuilder.PanelColor;

        m_question = Ui.CreateLabel("Question", panel.transform);
        m_question.richText = false;
        m_question.textWrappingMode = TextWrappingModes.Normal;
        m_question.alignment = TextAlignmentOptions.MidlineLeft;
        m_accept = (RectTransform)Ui.CreateButton(Accept, panel.transform, () => Answer(true)).transform;
        m_decline = (RectTransform)Ui.CreateButton(Decline, panel.transform, () => Answer(false)).transform;
        Place(false);
        panel.SetActive(false);
    }

    private void Answer(bool isAccepted)
    {
        if (m_isTrade)
        {
            m_client!.AnswerTradeRequest(isAccepted);
        }
        else
        {
            m_client!.AnswerPartyInvite(isAccepted);
        }
    }

    // The desktop's narrow place puts the question above the buttons; the touch strip puts them beside it.
    private void Place(bool isTouch)
    {
        m_isTouchLayout = isTouch;
        Rect bounds = BoundsFor(isTouch);
        m_panelRect!.anchoredPosition = bounds.position;
        m_panelRect.sizeDelta = bounds.size;
        RectTransform question = m_question!.rectTransform;
        question.anchorMin = Vector2.zero;
        question.anchorMax = Vector2.one;
        question.pivot = new Vector2(0.5f, 0.5f);
        if (isTouch)
        {
            question.offsetMin = new Vector2(Inset, Inset);
            question.offsetMax = new Vector2(-(2f * ButtonWidth + 3f * Inset), -Inset);
            PlaceButton(m_accept!, bounds.width - 2f * (ButtonWidth + Inset), Inset, bounds.height - 2f * Inset);
            PlaceButton(m_decline!, bounds.width - ButtonWidth - Inset, Inset, bounds.height - 2f * Inset);
            return;
        }

        const float buttonHeight = 32f;
        question.offsetMin = new Vector2(Inset, buttonHeight + 2f * Inset);
        question.offsetMax = new Vector2(-Inset, -Inset);
        PlaceButton(m_accept!, Inset, Inset, buttonHeight);
        PlaceButton(m_decline!, bounds.width - ButtonWidth - Inset, Inset, buttonHeight);
    }

    private static void PlaceButton(RectTransform button, float x, float y, float height)
    {
        button.anchorMin = Vector2.zero;
        button.anchorMax = Vector2.zero;
        button.pivot = Vector2.zero;
        button.anchoredPosition = new Vector2(x, y);
        button.sizeDelta = new Vector2(ButtonWidth, height);
    }
}
}
