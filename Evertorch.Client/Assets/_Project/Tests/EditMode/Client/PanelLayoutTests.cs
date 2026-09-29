using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Evertorch.Client.Tests.EditMode
{
/// <summary>
///     Where the skill bar, the feedback lines, and the windows sit (Prototype Content §2, owner decision 10), in canvas
///     units, which are 1,080 wide on every screen: the lines clear the bar, neither covers the stick or the touch
///     buttons, and no window row lies over a control.
/// </summary>
[TestFixture]
public sealed class PanelLayoutTests
{
    // Three lines of 24-point text with the panel's padding and spacing.
    private const float LinesHeight = FeedbackLines.MaxHeight;

    [TestCase(607.5f, TestName = "Landscape 1920 x 1080")]
    [TestCase(1920f, TestName = "Portrait 1080 x 1920")]
    public void FeedbackLines_SitAboutAFifthUp_RaisedToClearTheSkillBar(float canvasHeight)
    {
        float bottom = FeedbackLines.BottomFor(canvasHeight);

        Assert.That(bottom, Is.EqualTo(Math.Max(0.2f * canvasHeight, SkillBar.Top + 16f)).Within(1e-3f));
        Assert.That(bottom - SkillBar.Top, Is.GreaterThanOrEqualTo(16f));
        Assert.That(bottom + LinesHeight, Is.LessThan(canvasHeight * 0.6f), "the lines stay in the lower part");
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
    public void SkillBarAndFeedbackLines_NeverCoverTheStickOrTheTouchButtons(float canvasHeight)
    {
        const float width = ClientUI.CanvasWidth;
        Rect bar = SkillBar.BoundsFor(width, SkillSlots.Count);
        var lines = new Rect(
            (width - FeedbackLines.Width) / 2f,
            FeedbackLines.BottomFor(canvasHeight),
            FeedbackLines.Width,
            LinesHeight);
        Rect stick = TouchControls.StickBounds;
        Assert.That(bar.width, Is.EqualTo(628f), "eight slots of 70 with the padding and the spacing");
        Assert.That(SkillBar.WidthFor(5), Is.EqualTo(604f), "five keep their 112");
        Rect column = TouchControls.ButtonColumnBounds(width);

        Assert.That(bar.Overlaps(stick), Is.False, "the bar and the stick");
        Assert.That(bar.Overlaps(column), Is.False, "the bar and the touch buttons");
        Assert.That(lines.Overlaps(bar), Is.False, "the lines and the bar");
        Assert.That(lines.Overlaps(stick), Is.False, "the lines and the stick");
        Assert.That(lines.Overlaps(column), Is.False, "the lines and the touch buttons");
        Assert.That(bar.xMin, Is.GreaterThan(stick.xMax));
        Assert.That(bar.xMax, Is.LessThan(column.xMin));
    }

    // A row over a skill slot, the stick, or the feedback lines would hide them or take the presses meant for them, and
    // a press of a row buys or sells (Milestone 7 review finding 1).
    [TestCase(486f)]
    [TestCase(607.5f)]
    [TestCase(1920f)]
    public void NpcWindow_HangsBelowTheStatusBar_AndStopsAboveTheFeedbackLinesTheSkillBarAndTheStick(float canvasHeight)
    {
        const float width = ClientUI.CanvasWidth;
        Rect bar = SkillBar.BoundsFor(width, SkillSlots.Count);
        var lines = new Rect(
            (width - FeedbackLines.Width) / 2f,
            FeedbackLines.BottomFor(canvasHeight),
            FeedbackLines.Width,
            LinesHeight);
        Rect withTouch = NpcWindow.BoundsFor(canvasHeight, 100, true);
        Rect withoutTouch = NpcWindow.BoundsFor(canvasHeight, 100, false);

        Assert.That(withTouch.yMax, Is.EqualTo(canvasHeight - 64f).Within(1e-3f), "below the status bar");
        Assert.That(withTouch.Overlaps(TouchControls.StickBounds), Is.False, "no row over the stick");
        Assert.That(withTouch.Overlaps(bar), Is.False, "nor over the skill bar");
        Assert.That(withTouch.Overlaps(lines), Is.False, "nor over the feedback lines");
        Assert.That(
            withoutTouch.yMin,
            Is.EqualTo(FeedbackLines.TopFor(canvasHeight) + 8f).Within(1e-3f),
            "without the stick, to the lines");
        Assert.That(withTouch.height, Is.GreaterThan(56f + 2 * 30f + 6f), "two rows show on the shortest screen");
    }

    // The Stats window shares the NPC window's place and stops above the feedback lines, which it would otherwise
    // hide (Prototype Content §2; Milestone 7 review finding 1).
    [TestCase(486f)]
    [TestCase(607.5f)]
    [TestCase(1920f)]
    public void StatsWindow_HangsBelowTheStatusBar_AndStopsAboveTheFeedbackLinesAndTheStick(float canvasHeight)
    {
        const float width = ClientUI.CanvasWidth;
        var lines = new Rect(
            (width - FeedbackLines.Width) / 2f,
            FeedbackLines.BottomFor(canvasHeight),
            FeedbackLines.Width,
            LinesHeight);
        Rect withTouch = StatsWindow.BoundsFor(canvasHeight, true);
        Rect withoutTouch = StatsWindow.BoundsFor(canvasHeight, false);

        Assert.That(LinesHeight, Is.EqualTo(124f), "three lines of 32 with 4 between and 10 around");
        Assert.That(withTouch.yMax, Is.EqualTo(canvasHeight - 64f).Within(1e-3f), "below the status bar");
        Assert.That(withTouch.Overlaps(lines), Is.False, "clear of the feedback lines");
        Assert.That(withTouch.Overlaps(TouchControls.StickBounds), Is.False, "clear of the stick");
        Assert.That(withoutTouch.Overlaps(lines), Is.False);
        Assert.That(withTouch.yMin, Is.GreaterThanOrEqualTo(FeedbackLines.TopFor(canvasHeight) + 8f - 1e-3f));
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
    public void SkillsWindow_HangsBelowTheStatusBar_AndStopsAboveTheFeedbackLinesAndTheStick(float canvasHeight)
    {
        const float width = ClientUI.CanvasWidth;
        var lines = new Rect(
            (width - FeedbackLines.Width) / 2f,
            FeedbackLines.BottomFor(canvasHeight),
            FeedbackLines.Width,
            LinesHeight);
        Rect withTouch = SkillsWindow.BoundsFor(canvasHeight, 11, true);

        Assert.That(withTouch.yMax, Is.EqualTo(canvasHeight - 64f).Within(1e-3f), "below the status bar");
        Assert.That(withTouch.Overlaps(lines), Is.False, "clear of the feedback lines");
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
