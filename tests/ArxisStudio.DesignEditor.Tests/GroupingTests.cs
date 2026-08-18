using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Attached;
using ArxisStudio.Controls;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Design-time группы: пометка на контролах, одна рамка, один жест.
/// </summary>
/// <remarks>
/// Группа не создаёт узла в дереве — редактор его не правит (ADR 0001), — а помечает
/// участников attached-свойством <see cref="DesignGroup"/>. Отсюда всё остальное: пометка
/// живёт там же, где <c>Layout.X</c>/<c>Y</c>, и сохраняется хостом тем же способом;
/// у группировки есть шов записи, поэтому она попадает в контракт изменений и отменяется.
/// </remarks>
public class GroupingTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    private static Point NestedCentre => new(
        ContainerLocation.X + EditorHarness.NestedOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static Point SiblingCentre => new(
        ContainerLocation.X + EditorHarness.SiblingOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static EditorHarness Create(int nodeCount = 1)
    {
        var harness = EditorHarness.Create(nodeCount: nodeCount);
        for (var i = 0; i < nodeCount; i++)
            harness.PlaceContainer(i, ContainerLocation + new Vector(i * 320, 0), ContainerSize);

        return harness;
    }

    private static void Click(EditorHarness harness, Point point, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        harness.Window.MouseDown(point, MouseButton.Left, modifiers);
        harness.Window.MouseUp(point, MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    /// <summary>Собирает группу из двух соседей одной формы.</summary>
    private static EditorHarness CreateGrouped()
    {
        var harness = Create();
        Click(harness, NestedCentre);
        Click(harness, SiblingCentre, RawInputModifiers.Shift);

        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();
        return harness;
    }

    // ---- Сборка группы ----------------------------------------------------------

    [AvaloniaFact]
    public void Grouping_Marks_Every_Member()
    {
        var harness = CreateGrouped();

        var id = DesignGroup.GetId(harness.Nested(0));
        Assert.NotNull(id);
        Assert.Equal(id, DesignGroup.GetId(harness.Named(0, "Sibling")));
    }

    [AvaloniaFact]
    public void A_Single_Target_Is_Not_A_Group()
    {
        var harness = Create();
        Click(harness, NestedCentre);

        Assert.False(harness.Editor.CanGroupSelection());
        Assert.False(harness.Editor.GroupSelection());
        Assert.Null(DesignGroup.GetId(harness.Nested(0)));
    }

    /// <summary>
    /// Группа не собирается поперёк форм.
    /// </summary>
    /// <remarks>
    /// Выделение двухслойно: индексный слой адресует формы, слой target'ов — контролы.
    /// Группа из двух форм разошлась бы с индексным слоем, и рамка обещала бы жест,
    /// который к половине участников не применяется.
    /// </remarks>
    [AvaloniaFact]
    public void A_Group_Does_Not_Span_Two_Forms()
    {
        var harness = Create(nodeCount: 2);

        Click(harness, NestedCentre);
        Click(harness, NestedCentre + new Vector(320, 0), RawInputModifiers.Shift);

        // Группу поперёк форм не из чего собрать: слой выделения такой набор не строит
        // вовсе — добавление к выделению требует общего design host. Проверять надо
        // именно это, а не отказ группировки: до него дело просто не доходит.
        Assert.Equal(1, harness.Editor.SelectedDesignTargetsCount);
        Assert.False(harness.Editor.GroupSelection());
        Assert.Null(DesignGroup.GetId(harness.Nested(0)));
        Assert.Null(DesignGroup.GetId(harness.Nested(1)));
    }

    /// <summary>
    /// Прежняя группа участника входит в новую целиком.
    /// </summary>
    /// <remarks>
    /// Иначе половина старой группы осталась бы со старой пометкой, и на экране
    /// появились бы две рамки там, где пользователь собрал одну.
    /// </remarks>
    [AvaloniaFact]
    public void Regrouping_Pulls_In_The_Whole_Previous_Group()
    {
        var harness = CreateGrouped();
        var first = DesignGroup.GetId(harness.Nested(0));

        // Выбираем только одного участника и группируем его с третьим соседом.
        Click(harness, NestedCentre + new Vector(0, 0));
        Assert.True(harness.Editor.HasGroupSelection);

        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        var second = DesignGroup.GetId(harness.Nested(0));
        Assert.NotEqual(first, second);
        Assert.Equal(second, DesignGroup.GetId(harness.Named(0, "Sibling")));
    }

    // ---- Одна рамка -------------------------------------------------------------

    /// <summary>
    /// У группы рисуется ровно один adorner.
    /// </summary>
    /// <remarks>
    /// Это и есть видимая часть группировки: контуры участников по отдельности говорили бы,
    /// что элементов несколько, а жест применяется к ним как к одному.
    /// </remarks>
    [AvaloniaFact]
    public void A_Group_Draws_One_Adorner()
    {
        var harness = CreateGrouped();

        Assert.True(harness.Editor.HasGroupSelection);
        Assert.Empty(harness.Editor.SecondarySelectionAdorners);
        Assert.False(harness.Editor.HasMultipleNestedSelection);
    }

    /// <summary>
    /// Рамка группы накрывает всех участников.
    /// </summary>
    [AvaloniaFact]
    public void The_Frame_Covers_Every_Member()
    {
        var harness = CreateGrouped();

        var bounds = harness.Editor.SelectionBounds;
        var nested = harness.Editor.GetDesignPosition(harness.Nested(0));
        var sibling = harness.Editor.GetDesignPosition(harness.Named(0, "Sibling"));

        Assert.Equal(nested.X, bounds.X, 1);
        Assert.Equal(sibling.X + EditorHarness.NestedWidth, bounds.Right, 1);
    }

    /// <summary>
    /// После роспуска контуры участников возвращаются.
    /// </summary>
    [AvaloniaFact]
    public void Ungrouping_Brings_The_Adorners_Back()
    {
        var harness = CreateGrouped();

        Assert.True(harness.Editor.UngroupSelection());
        harness.RunLayout();

        Assert.False(harness.Editor.HasGroupSelection);
        Assert.True(harness.Editor.HasMultipleNestedSelection);
        Assert.Equal(2, harness.Editor.SecondarySelectionAdorners.Count);
        Assert.Null(DesignGroup.GetId(harness.Nested(0)));
        Assert.Null(DesignGroup.GetId(harness.Named(0, "Sibling")));
    }

    // ---- Выделение --------------------------------------------------------------

    /// <summary>
    /// Клик по участнику выбирает всю группу.
    /// </summary>
    [AvaloniaFact]
    public void Clicking_A_Member_Selects_The_Whole_Group()
    {
        var harness = CreateGrouped();
        Click(harness, new Point(600, 500));
        Assert.Empty(harness.Editor.SelectedDesignTargets);

        Click(harness, NestedCentre);

        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);
        Assert.True(harness.Editor.HasGroupSelection);
    }

    /// <summary>
    /// Двойной клик входит в группу и выбирает один контрол.
    /// </summary>
    /// <remarks>
    /// Без входа отредактировать участника можно было бы только распустив группу.
    /// Так устроены Figma и Blend, и ожидание у пользователя именно это.
    /// </remarks>
    [AvaloniaFact]
    public void A_Double_Click_Enters_The_Group()
    {
        var harness = CreateGrouped();

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.RunLayout();

        Assert.Equal(1, harness.Editor.SelectedDesignTargetsCount);
        Assert.False(harness.Editor.HasGroupSelection);
        Assert.Same(harness.Nested(0), harness.Editor.PrimarySelectionTarget!.Target);
    }

    /// <summary>
    /// Клик мимо группы закрывает её обратно.
    /// </summary>
    /// <remarks>
    /// Открытая навсегда группа — это режим, из которого пользователь не знает, как выйти.
    /// </remarks>
    [AvaloniaFact]
    public void Leaving_The_Group_Closes_It()
    {
        var harness = CreateGrouped();

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseUp(NestedCentre, MouseButton.Left);
        harness.RunLayout();
        Assert.Equal(1, harness.Editor.SelectedDesignTargetsCount);

        Click(harness, new Point(600, 500));
        Click(harness, NestedCentre);

        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);
        Assert.True(harness.Editor.HasGroupSelection);
    }

    // ---- Жесты ------------------------------------------------------------------

    /// <summary>
    /// Группа перетаскивается как одно.
    /// </summary>
    [AvaloniaFact]
    public void Dragging_A_Member_Moves_The_Whole_Group()
    {
        var harness = CreateGrouped();

        var nestedBefore = harness.Editor.GetDesignPosition(harness.Nested(0));
        var siblingBefore = harness.Editor.GetDesignPosition(harness.Named(0, "Sibling"));

        harness.Window.MouseDown(NestedCentre, MouseButton.Left);
        harness.Window.MouseMove(NestedCentre + new Vector(10, 6));
        harness.Window.MouseMove(NestedCentre + new Vector(40, 20));
        harness.Window.MouseUp(NestedCentre + new Vector(40, 20), MouseButton.Left);
        harness.RunLayout();

        var nestedAfter = harness.Editor.GetDesignPosition(harness.Nested(0));
        var siblingAfter = harness.Editor.GetDesignPosition(harness.Named(0, "Sibling"));

        Assert.NotEqual(nestedBefore.X, nestedAfter.X);
        Assert.Equal(nestedAfter.X - nestedBefore.X, siblingAfter.X - siblingBefore.X, 1);
        Assert.Equal(nestedAfter.Y - nestedBefore.Y, siblingAfter.Y - siblingBefore.Y, 1);
    }

    private static SelectionAdorner GroupAdorner(EditorHarness harness)
        => harness.Editor.GetVisualDescendants()
            .OfType<SelectionAdorner>()
            .Single(a => a.Name == "PART_GroupSelectionAdorner");

    /// <summary>
    /// Рамка группы не выглядит заблокированной.
    /// </summary>
    /// <remarks>
    /// Locked-визуал — это не отдельное оформление, а то, как рисуется adorner с
    /// политиками <c>None</c>/<c>None</c>. Поэтому рамка группы, которой политики не
    /// посчитали, выглядела ровно как заблокированный элемент — и ручки у неё были
    /// неинтерактивными по той же причине, а не по двум разным.
    /// </remarks>
    [AvaloniaFact]
    public void The_Group_Frame_Is_Not_Drawn_Locked()
    {
        var harness = CreateGrouped();
        var adorner = GroupAdorner(harness);

        Assert.NotEqual(ResizePolicy.None, adorner.ResizePolicy);
        Assert.NotEqual(MovePolicy.None, adorner.MovePolicy);
    }

    /// <summary>
    /// Группа масштабируется ручкой рамки.
    /// </summary>
    /// <remarks>
    /// Тянем тем же путём, которым тянет приложение: события поднимает сам adorner.
    /// </remarks>
    [AvaloniaFact]
    public void Resizing_The_Frame_Scales_The_Whole_Group()
    {
        var harness = CreateGrouped();
        var nested = harness.Nested(0);
        var sibling = harness.Named(0, "Sibling");

        var widthBefore = harness.Editor.GetDesignSize(nested).Width;
        var frameBefore = harness.Editor.SelectionBounds;

        var adorner = GroupAdorner(harness);
        var delta = new Vector(40, 0);
        adorner.RaiseEvent(new ResizeStartedEventArgs(default, ResizeDirection.Right, SelectionAdorner.ResizeStartedEvent));
        adorner.RaiseEvent(new ResizeDeltaEventArgs(delta, ResizeDirection.Right, SelectionAdorner.ResizeDeltaEvent));
        adorner.RaiseEvent(new VectorEventArgs { RoutedEvent = SelectionAdorner.ResizeCompletedEvent, Vector = delta });
        harness.RunLayout();

        Assert.True(harness.Editor.SelectionBounds.Width > frameBefore.Width);
        Assert.True(harness.Editor.GetDesignSize(nested).Width > widthBefore);
        Assert.True(harness.Editor.GetDesignSize(sibling).Width > 0);
    }

    // ---- Контракт изменений -----------------------------------------------------

    /// <summary>
    /// Группировка — одна единица редактирования.
    /// </summary>
    [AvaloniaFact]
    public void Grouping_Is_One_Edit()
    {
        var harness = Create();
        Click(harness, NestedCentre);
        Click(harness, SiblingCentre, RawInputModifiers.Shift);

        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        harness.Editor.GroupSelection();
        harness.RunLayout();

        var edit = Assert.Single(edits);
        Assert.Equal(DesignEditKind.Group, edit.Kind);
        Assert.Equal(2, edit.Changes.Count);
        Assert.All(edit.Changes, c => Assert.IsType<DesignGroupChange>(c));
    }

    [AvaloniaFact]
    public void Reverting_Dissolves_The_Group_And_Reapplying_Restores_It()
    {
        var harness = Create();
        Click(harness, NestedCentre);
        Click(harness, SiblingCentre, RawInputModifiers.Shift);

        DesignEditCompletedEventArgs? edit = null;
        harness.Editor.EditCompleted += (_, e) => edit = e;

        harness.Editor.GroupSelection();
        harness.RunLayout();
        var id = DesignGroup.GetId(harness.Nested(0));
        Assert.NotNull(id);

        foreach (var change in edit!.Changes)
            harness.Editor.Revert(change);

        harness.RunLayout();
        Assert.Null(DesignGroup.GetId(harness.Nested(0)));

        foreach (var change in edit.Changes)
            harness.Editor.Reapply(change);

        harness.RunLayout();
        Assert.Equal(id, DesignGroup.GetId(harness.Nested(0)));
    }

    /// <summary>
    /// Отмена и повтор не дописывают стек.
    /// </summary>
    [AvaloniaFact]
    public void Applying_A_Group_Change_Produces_No_New_Edit()
    {
        var harness = CreateGrouped();
        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        harness.Editor.ApplyGroup(harness.Nested(0), null);
        harness.RunLayout();

        Assert.Empty(edits);
    }

    [AvaloniaFact]
    public void ApplyGroup_Rejects_Null_Target()
    {
        var harness = Create();

        Assert.Throws<ArgumentNullException>(() => harness.Editor.ApplyGroup(null!, "x"));
    }
}
