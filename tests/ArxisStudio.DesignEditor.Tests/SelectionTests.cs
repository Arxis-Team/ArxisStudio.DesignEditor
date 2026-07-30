using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Поведение выделения при клике.
/// </summary>
/// <remarks>
/// Тесты намеренно опираются только на публичное наблюдаемое состояние
/// (<see cref="DesignEditor.SelectedDesignTargets"/>, <see cref="DesignEditor.PrimarySelectionTarget"/>,
/// счётчики), а не на внутренние структуры выбора. Внутреннее представление
/// будет переписано при переходе к рекурсивной вложенности, ожидания — нет.
/// </remarks>
public class SelectionTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    /// <summary>Точка внутри вложенного Border в координатах редактора.</summary>
    private static Point NestedCenter => new(
        ContainerLocation.X + EditorHarness.NestedOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static void Click(EditorHarness harness, Point point)
    {
        harness.Window.MouseDown(point, MouseButton.Left);
        harness.Window.MouseUp(point, MouseButton.Left);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Nothing_Is_Selected_Initially()
    {
        var harness = EditorHarness.Create();

        Assert.Empty(harness.Editor.SelectedDesignTargets);
        Assert.Null(harness.Editor.PrimarySelectionTarget);
        Assert.False(harness.Editor.HasSingleSelection);
        Assert.False(harness.Editor.HasMultipleSelection);
        Assert.Equal(default, harness.Editor.SelectionBounds);
    }

    [AvaloniaFact]
    public void Click_On_Nested_Control_Selects_It_As_Nested_Target()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        var nested = harness.Nested(0);

        Click(harness, NestedCenter);

        Assert.True(harness.Editor.HasSingleSelection);
        Assert.Equal(1, harness.Editor.SelectedDesignTargetsCount);

        var primary = harness.Editor.PrimarySelectionTarget;
        Assert.NotNull(primary);
        Assert.Same(nested, primary!.Target);
        Assert.Equal(DesignSelectionScope.NestedTarget, primary.Scope);
        Assert.Same(harness.Container(0), primary.Container);
    }

    [AvaloniaFact]
    public void Selection_Bounds_Match_Nested_Control_Geometry()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        Click(harness, NestedCenter);

        var expected = new Rect(
            ContainerLocation.X + EditorHarness.NestedOffset,
            ContainerLocation.Y + EditorHarness.NestedOffset,
            EditorHarness.NestedWidth,
            EditorHarness.NestedHeight);

        Assert.Equal(expected.X, harness.Editor.SelectionBounds.X, 1);
        Assert.Equal(expected.Y, harness.Editor.SelectionBounds.Y, 1);
        Assert.Equal(expected.Width, harness.Editor.SelectionBounds.Width, 1);
        Assert.Equal(expected.Height, harness.Editor.SelectionBounds.Height, 1);
    }

    [AvaloniaFact]
    public void Click_On_Empty_Surface_Clears_Selection()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        Click(harness, NestedCenter);
        Assert.True(harness.Editor.HasSingleSelection);

        // Точка заведомо вне контейнера.
        Click(harness, new Point(600, 500));

        Assert.Empty(harness.Editor.SelectedDesignTargets);
        Assert.Null(harness.Editor.PrimarySelectionTarget);
        Assert.False(harness.Editor.HasSingleSelection);
    }

    [AvaloniaFact]
    public void Click_On_Untracked_Area_Inside_Container_Falls_Back_To_Container()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        var container = harness.Container(0);

        // Правый нижний угол контейнера: внутри него, но вне вложенного Border
        // и без designer-метаданных под курсором.
        Click(harness, new Point(
            ContainerLocation.X + ContainerSize.Width - 10,
            ContainerLocation.Y + ContainerSize.Height - 10));

        var primary = harness.Editor.PrimarySelectionTarget;
        Assert.NotNull(primary);
        Assert.Same(container, primary!.Target);
        Assert.Equal(DesignSelectionScope.Container, primary.Scope);
    }
}
