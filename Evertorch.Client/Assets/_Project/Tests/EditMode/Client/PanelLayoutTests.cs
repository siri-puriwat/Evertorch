using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Where the skill bar, the chat, and the windows sit (Prototype Content §2, owner decisions 10 and 13), in canvas
///     units, which are 1,080 wide on every screen: the chat clears the bar, neither covers the stick or the touch
///     buttons, no window row lies over a control, and the party list and the invite's question stand clear of them all.
/// </summary>
[TestFixture]
public sealed class PanelLayoutTests
{
    // The touch Chat button has a place of its own: clear of the log, the skill bar, the stick, the target buttons, the
    // window buttons, and every side window (Prototype Content §2).
    [TestCase(486f)]
    [TestCase(607.5f)]
    [TestCase(1920f)]
    public void ChatButton_WithTheTouchControls_OverlapsNothing(float canvasHeight)
    {
        const float width = ClientUI.CanvasWidth;
        Rect chat = TouchControls.ChatButtonBounds;
        Rect[] others =
        {
            ChatPanel.BoundsFor(true), SkillBar.BoundsFor(width, SkillSlots.Count), TouchControls.StickBounds,
            TouchControls.ButtonColumnBounds(width), TouchControls.StatsButtonBounds(canvasHeight),
            TouchControls.SkillsButtonBounds(canvasHeight), TouchControls.OverlayToggleBounds(canvasHeight),
            NpcWindow.BoundsFor(canvasHeight, 100, true), StatsWindow.BoundsFor(canvasHeight, true),
            SkillsWindow.BoundsFor(canvasHeight, 11, true), InventoryWindow.BoundsFor(canvasHeight, 60, true)
        };

        Assert.That(others.Where(chat.Overlaps), Is.Empty);
        Assert.That(chat.xMax, Is.LessThanOrEqualTo(width));
        Assert.That(chat.size, Is.EqualTo(new Vector2(80f, 72f)), "as tall as the log beside it");
    }

    [TestCase(486f, TestName = "Landscape 20:9")]
    [TestCase(607.5f, TestName = "Landscape 16:9")]
    [TestCase(1920f, TestName = "Portrait 9:16")]
    public void InventoryWindow_HangsBelowTheStatusBar_AndStopsAboveTheTouchButtonsWhileTheyShow(float canvasHeight)
    {
        Rect column = TouchControls.ButtonColumnBounds(ClientUI.CanvasWidth);
        Rect withTouch = InventoryWindow.BoundsFor(canvasHeight, 60, true);
        Rect withoutTouch = InventoryWindow.BoundsFor(canvasHeight, 60, false);

        Assert.That(withTouch.yMax, Is.EqualTo(canvasHeight - 64f).Within(1e-3f), "below the status bar");
        Assert.That(withTouch.Overlaps(column), Is.False, "no row under a touch button");
        Assert.That(withTouch.Overlaps(TouchControls.StickBounds), Is.False, "nor under the stick");
        Assert.That(withoutTouch.yMin, Is.EqualTo(8f).Within(1e-3f), "without them, down to the bottom margin");
    }

    [TestCase(607.5f)]
    [TestCase(1920f)]
    public void SkillBarAndChat_NeverCoverTheStickOrTheTouchButtons(float canvasHeight)
    {
        const float width = ClientUI.CanvasWidth;
        Rect bar = SkillBar.BoundsFor(width, SkillSlots.Count);
        Rect lines = ChatPanel.BoundsFor(true);
        Rect stick = TouchControls.StickBounds;
        Assert.That(bar.width, Is.EqualTo(628f), "eight slots of 70 with the padding and the spacing");
        Assert.That(SkillBar.WidthFor(5), Is.EqualTo(604f), "five keep their 112");
        Rect column = TouchControls.ButtonColumnBounds(width);

        Assert.That(bar.Overlaps(stick), Is.False, "the bar and the stick");
        Assert.That(bar.Overlaps(column), Is.False, "the bar and the touch buttons");
        Assert.That(lines.Overlaps(bar), Is.False, "the log and the bar");
        Assert.That(lines.Overlaps(stick), Is.False, "the log and the stick");
        Assert.That(lines.Overlaps(column), Is.False, "the log and the touch buttons");
        Assert.That(ChatPanel.BoundsFor(false).Overlaps(bar), Is.False, "the desktop chat and the bar");
        Assert.That(bar.xMin, Is.GreaterThan(stick.xMax));
        Assert.That(bar.xMax, Is.LessThan(column.xMin));
    }

    // A row over a skill slot, the stick, or the chat would hide them or take the presses meant for them, and a press of
    // a row buys or sells (Milestone 7 review finding 1).
    [TestCase(486f)]
    [TestCase(607.5f)]
    [TestCase(1920f)]
    public void NpcWindow_HangsBelowTheStatusBar_AndStopsAboveTheChatTheSkillBarAndTheStick(float canvasHeight)
    {
        const float width = ClientUI.CanvasWidth;
        Rect bar = SkillBar.BoundsFor(width, SkillSlots.Count);
        Rect lines = ChatPanel.BoundsFor(true);
        Rect withTouch = NpcWindow.BoundsFor(canvasHeight, 100, true);
        Rect withoutTouch = NpcWindow.BoundsFor(canvasHeight, 100, false);

        Assert.That(withTouch.yMax, Is.EqualTo(canvasHeight - 64f).Within(1e-3f), "below the status bar");
        Assert.That(withTouch.Overlaps(TouchControls.StickBounds), Is.False, "no row over the stick");
        Assert.That(withTouch.Overlaps(bar), Is.False, "nor over the skill bar");
        Assert.That(withTouch.Overlaps(lines), Is.False, "nor over the chat");
        Assert.That(withoutTouch.Overlaps(ChatPanel.BoundsFor(false)), Is.False, "the desktop chat");
        Assert.That(withoutTouch.yMin, Is.EqualTo(270f).Within(1e-3f), "without the stick, to y 270 over the chat");
        Assert.That(withTouch.height, Is.GreaterThan(56f + 2 * 30f + 6f), "two rows show on the shortest screen");
    }

    // The Stats window shares the NPC window's place and stops above the chat, which it would otherwise hide
    // (Prototype Content §2; Milestone 7 review finding 1).
    [TestCase(486f)]
    [TestCase(607.5f)]
    [TestCase(1920f)]
    public void StatsWindow_HangsBelowTheStatusBar_AndStopsAboveTheChatAndTheStick(float canvasHeight)
    {
        Rect withTouch = StatsWindow.BoundsFor(canvasHeight, true);
        Rect withoutTouch = StatsWindow.BoundsFor(canvasHeight, false);

        Assert.That(withTouch.yMax, Is.EqualTo(canvasHeight - 64f).Within(1e-3f), "below the status bar");
        Assert.That(withTouch.Overlaps(ChatPanel.BoundsFor(true)), Is.False, "clear of the chat");
        Assert.That(withTouch.Overlaps(TouchControls.StickBounds), Is.False, "clear of the stick");
        Assert.That(withoutTouch.Overlaps(ChatPanel.BoundsFor(false)), Is.False);
        Assert.That(withTouch.yMin, Is.GreaterThanOrEqualTo(ChatPanel.TopFor(canvasHeight, true) + 8f - 1e-3f));
        Assert.That(withTouch.height, Is.GreaterThanOrEqualTo(56f + 2 * 30f), "two rows show on the shortest screen");
    }

    // The Stats and Skills buttons stand above the Dev button's place on the left edge, clear of the stick and the Dev
    // button.
    [TestCase(486f)]
    [TestCase(607.5f)]
    [TestCase(1920f)]
    public void StatsButton_StandsAboveTheDevButton_ClearOfTheStick(float canvasHeight)
    {
        Rect stats = TouchControls.StatsButtonBounds(canvasHeight);
        Rect skills = TouchControls.SkillsButtonBounds(canvasHeight);
        Rect dev = TouchControls.OverlayToggleBounds(canvasHeight);

        Assert.That(skills.yMin, Is.GreaterThan(stats.yMax), "Skills above Stats");
        Assert.That(skills.yMax, Is.LessThan(canvasHeight), "on the screen");
        Assert.That(stats.Overlaps(TouchControls.StickBounds), Is.False, "the stick");
        Assert.That(dev.Overlaps(TouchControls.StickBounds), Is.False, "the Dev button and the stick");
        Assert.That(stats.Overlaps(dev), Is.False, "the Dev button");
        Assert.That(stats.yMin, Is.GreaterThan(dev.yMax));
        Assert.That(stats.xMin, Is.Zero, "on the left edge");
    }

    [TestCase(486f)]
    [TestCase(607.5f)]
    [TestCase(1920f)]
    public void SkillsWindow_HangsBelowTheStatusBar_AndStopsAboveTheChatAndTheStick(float canvasHeight)
    {
        Rect withTouch = SkillsWindow.BoundsFor(canvasHeight, 11, true);
        Rect withoutTouch = SkillsWindow.BoundsFor(canvasHeight, 11, false);

        Assert.That(withTouch.yMax, Is.EqualTo(canvasHeight - 64f).Within(1e-3f), "below the status bar");
        Assert.That(withTouch.Overlaps(ChatPanel.BoundsFor(true)), Is.False, "clear of the chat");
        Assert.That(withoutTouch.Overlaps(ChatPanel.BoundsFor(false)), Is.False, "clear of the desktop chat");
        Assert.That(withTouch.Overlaps(TouchControls.StickBounds), Is.False, "clear of the stick");
        Assert.That(withTouch.height, Is.GreaterThanOrEqualTo(56f + 2 * 30f), "two rows show on the shortest screen");
    }

    // While the touch controls show, the windows at the top left stand beside the Dev, Stats, and Skills buttons, so a
    // tap on those never lands on a window, and the target frame beside them still stops short of the inventory
    // window (finding C2 of the Milestone 9 review).
    [TestCase(486f)]
    [TestCase(607.5f)]
    [TestCase(1920f)]
    public void SideWindows_WhileTheTouchControlsShow_StandBesideTheWindowButtons(float canvasHeight)
    {
        Rect[] buttons =
        {
            TouchControls.StatsButtonBounds(canvasHeight), TouchControls.SkillsButtonBounds(canvasHeight),
            TouchControls.OverlayToggleBounds(canvasHeight)
        };
        Rect[] windows =
        {
            NpcWindow.BoundsFor(canvasHeight, 100, true), StatsWindow.BoundsFor(canvasHeight, true),
            SkillsWindow.BoundsFor(canvasHeight, 11, true)
        };
        Vector2 beside = TargetFrame.XRangeFor(true, true);

        Assert.That(
            windows.SelectMany(window => buttons.Where(window.Overlaps)),
            Is.Empty,
            "no window over a button");
        Assert.That(windows.Select(window => window.xMax), Is.All.EqualTo(NpcWindow.RightFor(true)));
        Assert.That(NpcWindow.BoundsFor(canvasHeight, 100, false).xMin, Is.EqualTo(8f), "without them, at the margin");
        Assert.That(beside.x, Is.GreaterThan(NpcWindow.RightFor(true)), "the frame clears the windows");
        Assert.That(beside.y, Is.LessThan(InventoryWindow.Left), "and the inventory window");
        Assert.That(beside.y - beside.x, Is.GreaterThanOrEqualTo(200f), "still wide enough for a name and a bar");
    }

    // The party list hangs under the status bar at the left and the invite's question stands beside the chat, or
    // over the touch log: neither lies over the chat, the skill bar, the stick, a touch button, or the target frame,
    // nor over each other, on every screen (Prototype Content §2).
    [TestCase(486f, false)]
    [TestCase(607.5f, false)]
    [TestCase(1920f, false)]
    [TestCase(486f, true)]
    [TestCase(607.5f, true)]
    [TestCase(1920f, true)]
    public void PartyListAndInvitePrompt_OverlapNothing(float canvasHeight, bool isTouch)
    {
        const float width = ClientUI.CanvasWidth;
        Rect list = PartyList.BoundsFor(canvasHeight, isTouch);
        Rect prompt = PartyInvitePrompt.BoundsFor(isTouch);
        Vector2 frame = TargetFrame.XRangeFor(false);
        var target = new Rect(
            frame.x,
            TargetFrame.BottomFor(canvasHeight),
            frame.y - frame.x,
            TargetFrame.TallestHeight);
        Rect[] others = isTouch
            ? new[]
            {
                ChatPanel.BoundsFor(true), SkillBar.BoundsFor(width, SkillSlots.Count), TouchControls.StickBounds,
                TouchControls.ButtonColumnBounds(width), TouchControls.StatsButtonBounds(canvasHeight),
                TouchControls.SkillsButtonBounds(canvasHeight), TouchControls.OverlayToggleBounds(canvasHeight),
                TouchControls.ChatButtonBounds, target
            }
            : new[] { ChatPanel.BoundsFor(false), SkillBar.BoundsFor(width, SkillSlots.Count), target };

        Assert.That(others.Where(list.Overlaps), Is.Empty, "the list");
        Assert.That(others.Where(prompt.Overlaps), Is.Empty, "the prompt");
        Assert.That(list.Overlaps(prompt), Is.False, "the list and the prompt");
        Assert.That(list.yMax, Is.EqualTo(canvasHeight - 64f).Within(1e-3f), "under the status bar");
        Assert.That(prompt.xMax, Is.LessThanOrEqualTo(InventoryWindow.Left), "short of the inventory window");
        Assert.That(PartyList.RowHeightFor(canvasHeight, isTouch), Is.GreaterThanOrEqualTo(24f), "a row stays legible");
    }

    // The layout table of Prototype Content §2: x 8 to 540 and y 128 to 262 on the desktop, x 226 to 762 and y 128 to
    // 200 with the touch controls.
    [Test]
    public void Chat_StandsWhereTheLayoutPutsIt_AboveTheSkillBar()
    {
        Rect desktop = ChatPanel.BoundsFor(false);
        Rect touch = ChatPanel.BoundsFor(true);

        Assert.That((desktop.xMin, desktop.xMax, desktop.yMin, desktop.yMax), Is.EqualTo((8f, 540f, 128f, 262f)));
        Assert.That((touch.xMin, touch.xMax, touch.yMin, touch.yMax), Is.EqualTo((226f, 762f, 128f, 200f)));
        Assert.That(desktop.yMin - SkillBar.Top, Is.GreaterThanOrEqualTo(8f), "clear of the bar");
        Assert.That(ChatPanel.TopFor(607.5f, false), Is.EqualTo(262f), "the windows stop above it");
    }

    [Test]
    public void InventoryWindow_WhileItsRowsFit_IsJustTallEnoughForThem()
    {
        Rect three = InventoryWindow.BoundsFor(1920f, 3, true);

        Assert.That(three.height, Is.EqualTo(54f + 3 * 30f + 2 * 4f).Within(1e-3f));
    }

    [Test]
    public void NpcWindow_WhileItsRowsFit_IsJustTallEnoughForThem()
    {
        Rect three = NpcWindow.BoundsFor(1920f, 3, true);

        Assert.That(three.height, Is.EqualTo(56f + 3 * 30f + 2 * 6f).Within(1e-3f));
    }

    // The layout table's place: x 8 to 188 from y 543.5 down to 339.5 on the desktop, x 118 to 298 with touch.
    [Test]
    public void PartyList_StandsWhereTheLayoutPutsIt()
    {
        Rect desktop = PartyList.BoundsFor(607.5f, false);
        Rect touch = PartyList.BoundsFor(607.5f, true);

        Assert.That((desktop.xMin, desktop.xMax, desktop.yMin, desktop.yMax), Is.EqualTo((8f, 188f, 339.5f, 543.5f)));
        Assert.That((touch.xMin, touch.xMax), Is.EqualTo((118f, 298f)));
        Assert.That(touch.xMin, Is.GreaterThan(TouchControls.WindowButtonsRight), "clear of the window buttons");
        Assert.That(PartyList.BoundsFor(607.5f, false, 2).height, Is.EqualTo(24f + 2 * 36f), "as tall as its rows");
    }

    [Test]
    public void SkillsWindow_WhileItsRowsFit_IsJustTallEnoughForThem()
    {
        Rect three = SkillsWindow.BoundsFor(1920f, 3, true);

        Assert.That(three.height, Is.EqualTo(56f + 9 * 30f + 8 * 6f).Within(1e-3f), "a heading and two lines each");
    }

    [Test]
    public void StatsWindow_OnATallScreen_ShowsEveryRow()
    {
        Rect bounds = StatsWindow.BoundsFor(1920f, true);

        Assert.That(bounds.height, Is.EqualTo(56f + 10 * 30f + 9 * 6f).Within(1e-3f));
    }

    // The windows at the top left share one place and one right edge; the target frame moves right of it while one
    // shows, and stays left of the inventory window (Milestone 7 review finding 1).
    [Test]
    public void TargetFrame_BesideAWindow_ClearsItAndTheInventoryWindow()
    {
        Vector2 centred = TargetFrame.XRangeFor(false);
        Vector2 beside = TargetFrame.XRangeFor(true);
        Rect inventory = InventoryWindow.BoundsFor(1920f, 3, false);

        Assert.That(NpcWindow.BoundsFor(1920f, 3, false).xMax, Is.EqualTo(NpcWindow.Right));
        Assert.That(StatsWindow.BoundsFor(1920f, false).xMax, Is.EqualTo(NpcWindow.Right));
        Assert.That(SkillsWindow.BoundsFor(1920f, 3, false).xMax, Is.EqualTo(NpcWindow.Right));
        Assert.That(centred.x, Is.LessThan(NpcWindow.Right), "centred, the frame would lie under a window");
        Assert.That(beside.x, Is.GreaterThan(NpcWindow.Right), "beside, it clears the window");
        Assert.That(beside.y, Is.LessThan(inventory.xMin), "and the inventory window");
        Assert.That(beside.y - beside.x, Is.GreaterThanOrEqualTo(300f), "wide enough for a name and a bar");
    }
}
}
