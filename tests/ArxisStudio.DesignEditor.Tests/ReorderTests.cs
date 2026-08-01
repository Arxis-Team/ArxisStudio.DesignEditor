using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Attached;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Перестановка контрола среди соседей в раскладке, которая владеет позицией.
/// </summary>
/// <remarks>
/// Там, где координату задать нельзя, перетаскивание меняет порядок детей —
/// единственное, что вообще меняет их положение в такой панели.
/// </remarks>
public class ReorderTests
{
    private static readonly Point CardLocation = new(100, 100);
    private static readonly Size CardSize = new(340, 400);

    private static (EditorHarness Harness, List<DesignEditCompletedEventArgs> Edits) Create()
    {
        var harness = EditorHarness.CreateStackHosted();
        harness.PlaceContainer(0, CardLocation, CardSize);

        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);
        return (harness, edits);
    }

    private static Panel PanelOf(EditorHarness harness, Control child)
        => (Panel)child.GetVisualParent()!;

    private static void Drag(EditorHarness harness, Point from, Vector delta)
    {
        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + new Vector(4, 6));
        harness.Window.MouseMove(from + delta);
        harness.Window.MouseUp(from + delta, MouseButton.Left);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Dragging_A_Stack_Child_Upwards_Changes_Its_Order()
    {
        var (harness, _) = Create();
        var action = harness.Find<Button>(0, "Action");
        var field = harness.Find<TextBox>(0, "Field");
        var panel = PanelOf(harness, action);

        Assert.Equal(0, panel.Children.IndexOf(field));
        Assert.Equal(1, panel.Children.IndexOf(action));

        var centre = harness.CentreOf(action);
        Drag(harness, centre, new Vector(0, -60));

        // Кнопка перетащена выше середины поля, значит встаёт перед ним.
        Assert.Equal(0, panel.Children.IndexOf(action));
        Assert.Equal(1, panel.Children.IndexOf(field));
    }

    [AvaloniaFact]
    public void Reorder_Produces_One_Edit_With_Both_Indices()
    {
        var (harness, edits) = Create();
        var action = harness.Find<Button>(0, "Action");

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        var edit = Assert.Single(edits);
        Assert.Equal(DesignEditKind.Reorder, edit.Kind);
        var change = Assert.IsType<DesignChildOrderChange>(Assert.Single(edit.Changes));
        Assert.Same(action, change.Target);
        Assert.Equal(1, change.OldIndex);
        Assert.Equal(0, change.NewIndex);
    }

    [AvaloniaFact]
    public void Reorder_Is_Revertible()
    {
        var (harness, edits) = Create();
        var action = harness.Find<Button>(0, "Action");
        var panel = PanelOf(harness, action);

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));
        Assert.Equal(0, panel.Children.IndexOf(action));

        harness.Editor.Revert(edits.Single().Changes.Single());
        harness.RunLayout();

        Assert.Equal(1, panel.Children.IndexOf(action));

        // Отмена не порождает новой записи, иначе стек никогда не пустел бы.
        Assert.Single(edits);
    }

    [AvaloniaFact]
    public void Gesture_Returning_To_The_Original_Slot_Records_Nothing()
    {
        var (harness, edits) = Create();
        var action = harness.Find<Button>(0, "Action");

        // Смещение меньше половины соседа: точка вставки не меняется.
        Drag(harness, harness.CentreOf(action), new Vector(0, -4));

        Assert.Empty(edits);
    }

    [AvaloniaFact]
    public void User_Lock_Blocks_Reorder()
    {
        var (harness, edits) = Create();
        var action = harness.Find<Button>(0, "Action");
        var panel = PanelOf(harness, action);
        DesignInteraction.SetMovePolicy(action, MovePolicy.None);

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        // Запрет пользователя сильнее любой раскладки, включая перестановку.
        Assert.Equal(1, panel.Children.IndexOf(action));
        Assert.Empty(edits);
    }

    [AvaloniaFact]
    public void Indicator_Is_Shown_During_The_Gesture_And_Cleared_After()
    {
        var (harness, _) = Create();
        var action = harness.Find<Button>(0, "Action");
        var centre = harness.CentreOf(action);

        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseMove(centre + new Vector(4, 6));
        harness.Window.MouseMove(centre + new Vector(0, -60));
        harness.RunLayout();

        Assert.True(harness.Editor.IsReordering);
        Assert.True(harness.Editor.ReorderIndicator.Width > 0);

        harness.Window.MouseUp(centre + new Vector(0, -60), MouseButton.Left);
        harness.RunLayout();

        Assert.False(harness.Editor.IsReordering);
    }

    [AvaloniaFact]
    public void Absolute_Child_Still_Moves_Instead_Of_Reordering()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, CardLocation, new Size(300, 200));
        var nested = harness.Nested(0);

        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        Drag(harness, harness.CentreOf(nested), new Vector(40, 30));

        // Раскладка с абсолютным позиционированием не превращается в перестановку.
        var edit = Assert.Single(edits);
        Assert.Equal(DesignEditKind.Move, edit.Kind);
        Assert.IsType<DesignGeometryChange>(Assert.Single(edit.Changes));
    }
}
