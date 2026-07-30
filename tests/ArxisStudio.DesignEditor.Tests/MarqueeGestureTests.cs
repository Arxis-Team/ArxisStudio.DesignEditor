using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Смысл перетаскивания, начатого на пустой области контейнера.
/// </summary>
/// <remarks>
/// Политика задаётся через <see cref="DesignEditorInputGestures.ContainerEmptyAreaDrag"/>.
/// По умолчанию пустая область — фон для рамки выделения, как в form designer'ах;
/// перемещение контейнера остаётся доступным через модификатор и через
/// перетаскивание уже выбранного контейнера.
/// </remarks>
public class MarqueeGestureTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    /// <summary>Пустая область контейнера: правый нижний угол, ниже обоих контролов.</summary>
    private static Point EmptyArea => new(
        ContainerLocation.X + ContainerSize.Width - 20,
        ContainerLocation.Y + ContainerSize.Height - 20);

    /// <summary>Точка выше и левее обоих контролов.</summary>
    private static Point AboveBoth => new(ContainerLocation.X + 5, ContainerLocation.Y + 5);

    private static EditorHarness CreatePlaced()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        return harness;
    }

    private static void Drag(EditorHarness harness, Point from, Point to, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        harness.Window.MouseDown(from, MouseButton.Left, modifiers);
        harness.Window.MouseMove(from + ((to - from) * 0.5), modifiers);
        harness.Window.MouseMove(to, modifiers);
        harness.Window.MouseUp(to, MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Default_Gesture_Is_Marquee()
    {
        var harness = EditorHarness.Create();

        Assert.Equal(
            ContainerEmptyAreaDragGesture.Marquee,
            harness.Editor.InputGestures.ContainerEmptyAreaDrag);
    }

    [AvaloniaFact]
    public void Drag_On_Empty_Area_Selects_Children_Instead_Of_Moving_Container()
    {
        var harness = CreatePlaced();
        var container = harness.Container(0);

        Drag(harness, EmptyArea, AboveBoth);

        var targets = harness.Editor.SelectedDesignTargets.Select(t => t.Target).ToList();

        Assert.Contains(harness.Nested(0), targets);
        Assert.Contains(harness.Named(0, "Sibling"), targets);

        // Контейнер остался на месте: жест ушёл рамке, а не перемещению.
        Assert.Equal(ContainerLocation, container.Location);
    }

    [AvaloniaFact]
    public void MoveContainer_Gesture_Restores_Dragging_By_Empty_Area()
    {
        var harness = CreatePlaced();
        harness.Editor.InputGestures.ContainerEmptyAreaDrag = ContainerEmptyAreaDragGesture.MoveContainer;
        var container = harness.Container(0);

        Drag(harness, EmptyArea, EmptyArea + new Vector(40, 25));

        Assert.NotEqual(ContainerLocation, container.Location);
    }

    [AvaloniaFact]
    public void Container_Interaction_Modifier_Still_Moves_The_Container()
    {
        var harness = CreatePlaced();
        var container = harness.Container(0);

        // Ctrl удерживает жест за контейнером даже при политике Marquee.
        Drag(harness, EmptyArea, EmptyArea + new Vector(40, 25), RawInputModifiers.Control);

        Assert.NotEqual(ContainerLocation, container.Location);
    }

    [AvaloniaFact]
    public void Already_Selected_Container_Is_Draggable_Without_Modifiers()
    {
        var harness = CreatePlaced();
        var container = harness.Container(0);

        // Клик по пустой области выбирает контейнер...
        harness.Window.MouseDown(EmptyArea, MouseButton.Left);
        harness.Window.MouseUp(EmptyArea, MouseButton.Left);
        harness.RunLayout();
        Assert.Same(container, harness.Editor.PrimarySelectionTarget!.Target);

        // ...после чего его можно тянуть без модификаторов.
        Drag(harness, EmptyArea, EmptyArea + new Vector(40, 25));

        Assert.NotEqual(ContainerLocation, container.Location);
    }

    [AvaloniaFact]
    public void Click_Without_Drag_Still_Selects_The_Container()
    {
        var harness = CreatePlaced();
        var container = harness.Container(0);

        harness.Window.MouseDown(EmptyArea, MouseButton.Left);
        harness.Window.MouseUp(EmptyArea, MouseButton.Left);
        harness.RunLayout();

        // Пустая рамка трактуется как клик: контейнер выбран, а не «ничего».
        Assert.Same(container, harness.Editor.PrimarySelectionTarget!.Target);
        Assert.Equal(DesignSelectionScope.Container, harness.Editor.PrimarySelectionTarget.Scope);
        Assert.Equal(ContainerLocation, container.Location);
    }
}
