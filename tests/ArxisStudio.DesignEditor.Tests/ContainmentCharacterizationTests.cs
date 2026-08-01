using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ArxisStudio.Controls;
using ArxisStudio.States;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Что происходит, когда вложенный контрол растягивают за пределы карточки.
/// </summary>
/// <remarks>
/// Фиксирует сегодняшнее поведение; ограничение по контейнеру перевернёт
/// последнее утверждение.
/// <para>
/// Замер снял главный вопрос по видео <c>Resize_BUG</c>: рамка не врёт.
/// <c>Bounds</c>, запрошенный <c>Height</c> и <c>SelectionBounds</c> совпадают
/// до единицы — расходится не геометрия, а отрисовка: карточка обрезает контрол,
/// а рамка честно показывает его настоящий размер. Поэтому чинить надо не рамку,
/// а отсутствие верхней границы у resize.
/// </para>
/// </remarks>
public class ContainmentCharacterizationTests
{
    private static readonly Point CardLocation = new(100, 100);
    private static readonly Size CardSize = new(340, 400);

    private const double Overshoot = 400;

    private static (EditorHarness Harness, Button Action) ResizeBeyondCard()
    {
        var harness = EditorHarness.CreateStackHosted();
        var container = harness.PlaceContainer(0, CardLocation, CardSize);
        var action = harness.Find<Button>(0, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        var state = new ItemResizingState(container, action, ResizeDirection.Bottom);
        container.PushState(state);
        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(0, Overshoot), ResizeDirection.Bottom, DesignEditorItem.ResizeDeltaEvent));
        harness.RunLayout();

        return (harness, action);
    }

    [AvaloniaFact]
    public void Frame_Requested_And_Arranged_Sizes_All_Agree()
    {
        var harness = EditorHarness.CreateStackHosted();
        var container = harness.PlaceContainer(0, CardLocation, CardSize);
        var action = harness.Find<Button>(0, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        var before = action.Bounds.Height;

        var state = new ItemResizingState(container, action, ResizeDirection.Bottom);
        container.PushState(state);
        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(0, Overshoot), ResizeDirection.Bottom, DesignEditorItem.ResizeDeltaEvent));
        harness.RunLayout();

        // Раскладка отдала ровно столько, сколько попросили: StackPanel не зажимает ребёнка.
        Assert.Equal(before + Overshoot, action.Bounds.Height, 1);

        // Запрошенный и фактический размеры совпадают...
        Assert.Equal(action.Height, action.Bounds.Height, 1);

        // ...и рамка равна фактическому. Расхождения понятий размера нет.
        Assert.Equal(action.Bounds.Height, harness.Editor.SelectionBounds.Height, 1);
    }

    [AvaloniaFact]
    public void Resize_Is_Not_Constrained_By_The_Owning_Card()
    {
        var (harness, _) = ResizeBeyondCard();

        var cardBottom = CardLocation.Y + CardSize.Height;

        // Это и есть дефект: у resize нет верхней границы вообще — ни MaxHeight,
        // ни контейнера. Контрол уходит на 139 единиц ниже карточки, карточка его
        // обрезает, и ручки выделения оказываются на пустом холсте.
        Assert.True(
            harness.Editor.SelectionBounds.Bottom > cardBottom,
            $"ожидался выход за карточку: рамка {harness.Editor.SelectionBounds}, низ карточки {cardBottom}");
    }

    [AvaloniaFact]
    public void MaxHeight_Clamps_The_Written_Size()
    {
        var harness = EditorHarness.CreateStackHosted();
        var container = harness.PlaceContainer(0, CardLocation, CardSize);
        var action = harness.Find<Button>(0, "Action");
        action.MaxHeight = 60;
        harness.RunLayout();

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        var state = new ItemResizingState(container, action, ResizeDirection.Bottom);
        container.PushState(state);
        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(0, Overshoot), ResizeDirection.Bottom, DesignEditorItem.ResizeDeltaEvent));
        harness.RunLayout();

        // Редактор читает MaxHeight сам, поэтому запрошенное и фактическое совпадают.
        // Раньше он писал 429, раскладка выдавала 60, и дальше всё считалось от 429.
        Assert.Equal(60, action.Height, 1);
        Assert.Equal(60, action.Bounds.Height, 1);
    }
}
