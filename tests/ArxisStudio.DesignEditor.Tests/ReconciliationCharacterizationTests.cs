using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArxisStudio.Controls;
using Xunit;
using DesignLayout = ArxisStudio.Attached.Layout;

namespace ArxisStudio.Tests;

/// <summary>
/// Согласование design-координат с фактической раскладкой и единицы слоя адорнеров.
/// </summary>
/// <remarks>
/// Фиксирует сегодняшнее поведение. Оба утверждения будут перевёрнуты:
/// согласование должно уступать активному жесту, а экстент слоя — не зависеть от зума.
/// </remarks>
public class ReconciliationCharacterizationTests
{
    private static readonly Point CardLocation = new(100, 100);
    private static readonly Size CardSize = new(340, 400);

    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(300, 200);

    [AvaloniaFact]
    public void Design_Position_Is_Overwritten_When_The_Parent_Owns_It()
    {
        var harness = EditorHarness.CreateStackHosted();
        harness.PlaceContainer(0, CardLocation, CardSize);
        var action = harness.Find<Button>(0, "Action");

        // Даём DesignY устояться: пересчёт всегда идёт через dispatcher,
        // поэтому сразу после layout там ещё значение по умолчанию.
        Dispatcher.UIThread.RunJobs();
        harness.RunLayout();
        var arranged = DesignLayout.GetDesignY(action);
        Assert.True(arranged > 0, "координата не успела посчитаться");

        DesignLayout.SetDesignY(action, arranged + 120);
        harness.RunLayout();
        Dispatcher.UIThread.RunJobs();
        harness.RunLayout();

        // UpdateDesignPosition идёт через dispatcher и пересчитывает DesignX/Y
        // из фактического положения. Guard IsUpdatingPosition этому не мешает:
        // он ставится и снимается синхронно, а читается уже в отложенном замыкании.
        Assert.Equal(arranged, DesignLayout.GetDesignY(action), 1);
    }

    [AvaloniaFact]
    public void Adorner_Layer_Extent_Depends_On_Zoom()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);

        var nested = harness.Nested(0);
        var sibling = harness.Named(0, "Sibling");

        Select(harness, nested);
        Select(harness, sibling, additive: true);
        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);

        var layer = harness.Editor.GetVisualDescendants().OfType<SelectionAdornerLayer>().Single();

        harness.Editor.ViewportZoom = 1.0;
        harness.RunLayout();
        var atOne = layer.DesiredSize;

        harness.Editor.ViewportZoom = 2.0;
        harness.RunLayout();
        var atTwo = layer.DesiredSize;

        // MeasureOverride складывает мировой X с размером, умноженным на зум,
        // поэтому экстент «дышит» при масштабировании, хотя слой лежит внутри
        // уже отмасштабированного Canvas и должен считаться в мировых единицах.
        Assert.NotEqual(atOne.Width, atTwo.Width, 1);
    }

    private static void Select(EditorHarness harness, Control target, bool additive = false)
    {
        var modifiers = additive ? RawInputModifiers.Shift : RawInputModifiers.None;
        var centre = harness.CentreOf(target);
        harness.Window.MouseDown(centre, MouseButton.Left, modifiers);
        harness.Window.MouseUp(centre, MouseButton.Left, modifiers);
        harness.RunLayout();
    }
}
