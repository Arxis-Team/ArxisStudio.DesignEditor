using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Контракт изменений, на котором строится undo/redo.
/// </summary>
/// <remarks>
/// Проверяется не факт мутации, а гранулярность и полнота потока: одна запись
/// на жест, только реально изменившиеся targets, и возможность вернуть состояние
/// без появления новой записи.
/// </remarks>
public class EditContractTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    private static Point NestedCentre => new(
        ContainerLocation.X + EditorHarness.NestedOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static (EditorHarness Harness, List<DesignEditCompletedEventArgs> Edits) Create(int nodeCount = 1)
    {
        var harness = EditorHarness.Create(nodeCount: nodeCount);
        for (var i = 0; i < nodeCount; i++)
            harness.PlaceContainer(i, ContainerLocation + new Vector(i * 320, 0), ContainerSize);

        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);
        return (harness, edits);
    }

    private static void Drag(EditorHarness harness, Point from, Vector delta)
    {
        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + new Vector(10, 6));
        harness.Window.MouseMove(from + delta);
        harness.Window.MouseUp(from + delta, MouseButton.Left);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Drag_Produces_One_Edit_For_The_Whole_Gesture()
    {
        var (harness, edits) = Create();

        Drag(harness, NestedCentre, new Vector(60, 40));

        // Кадров перетаскивания было несколько, запись — одна.
        var edit = Assert.Single(edits);
        Assert.Equal(DesignEditKind.Move, edit.Kind);
        var change = Assert.Single(edit.Changes);
        Assert.Same(harness.Nested(0), change.Target);
    }

    [AvaloniaFact]
    public void Edit_Carries_Geometry_Before_And_After()
    {
        var (harness, edits) = Create();

        Drag(harness, NestedCentre, new Vector(60, 40));

        var change = edits.Single().Changes.Single();

        Assert.Equal(change.OldBounds.X + 60, change.NewBounds.X, 1);
        Assert.Equal(change.OldBounds.Y + 40, change.NewBounds.Y, 1);
        Assert.Equal(change.OldBounds.Width, change.NewBounds.Width, 1);
        Assert.Equal(change.OldBounds.Height, change.NewBounds.Height, 1);
    }

    [AvaloniaFact]
    public void Click_Without_Movement_Produces_No_Edit()
    {
        var (harness, edits) = Create();

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.RunLayout();

        // Клик выделяет, но геометрию не меняет — в стек undo класть нечего.
        Assert.Empty(edits);
    }

    [AvaloniaFact]
    public void Gesture_Returning_To_The_Origin_Produces_No_Edit()
    {
        var (harness, edits) = Create();
        var start = NestedCentre;

        harness.Window.MouseDown(start, MouseButton.Left);
        harness.Window.MouseMove(start + new Vector(40, 30));
        harness.Window.MouseMove(start + new Vector(80, 60));
        harness.Window.MouseMove(start);
        harness.Window.MouseUp(start, MouseButton.Left);
        harness.RunLayout();

        // Элемент вернулся на место: изменения нет, несмотря на движение.
        Assert.Empty(edits);
    }

    [AvaloniaFact]
    public void Group_Drag_Reports_Every_Moved_Target_In_One_Edit()
    {
        var (harness, edits) = Create();

        // Собираем группу из двух соседей внутри одного контейнера.
        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);

        var sibling = new Point(
            ContainerLocation.X + EditorHarness.SiblingOffset + (EditorHarness.NestedWidth / 2),
            ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));
        harness.Window.MouseDown(sibling, MouseButton.Left, RawInputModifiers.Shift);
        harness.Window.MouseUp(sibling, MouseButton.Left, RawInputModifiers.Shift);
        harness.RunLayout();
        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);

        edits.Clear();
        Drag(harness, sibling, new Vector(50, 20));

        var edit = Assert.Single(edits);
        var targets = edit.Changes.Select(c => c.Target).ToList();

        // Перетаскивание группы — одна запись со всеми участниками.
        Assert.Equal(2, edit.Changes.Count);
        Assert.Contains(harness.Nested(0), targets);
        Assert.Contains(harness.Named(0, "Sibling"), targets);
    }

    [AvaloniaFact]
    public void ApplyGeometry_Restores_The_Previous_State()
    {
        var (harness, edits) = Create();

        Drag(harness, NestedCentre, new Vector(60, 40));
        var change = edits.Single().Changes.Single();

        harness.Editor.ApplyGeometry(change.Target, change.OldBounds);
        harness.RunLayout();

        var restored = new Rect(
            ArxisStudio.Attached.Layout.GetDesignX(change.Target),
            ArxisStudio.Attached.Layout.GetDesignY(change.Target),
            change.Target.Bounds.Width,
            change.Target.Bounds.Height);

        Assert.Equal(change.OldBounds.X, restored.X, 1);
        Assert.Equal(change.OldBounds.Y, restored.Y, 1);
    }

    [AvaloniaFact]
    public void ApplyGeometry_Does_Not_Produce_A_New_Edit()
    {
        var (harness, edits) = Create();

        Drag(harness, NestedCentre, new Vector(60, 40));
        var change = edits.Single().Changes.Single();

        edits.Clear();
        harness.Editor.ApplyGeometry(change.Target, change.OldBounds);
        harness.RunLayout();

        // Иначе отмена сама попадала бы в стек и он никогда не пустел бы.
        Assert.Empty(edits);
    }

    [AvaloniaFact]
    public void ApplyGeometry_Rejects_Null_Target()
    {
        var (harness, _) = Create();

        Assert.Throws<ArgumentNullException>(() => harness.Editor.ApplyGeometry(null!, default));
    }
}
