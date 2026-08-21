using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using ArxisStudio.Attached;
using ArxisStudio.Controls;
using Xunit;
using DesignLayout = ArxisStudio.Attached.Layout;

namespace ArxisStudio.Tests;

/// <summary>
/// Группа внутри группы: путь вместо плоской пометки и рамка на кластер.
/// </summary>
/// <remarks>
/// Стенд свой, на четыре контрола: у общего <see cref="EditorHarness"/> их два, а вложенность
/// на двух не показать — нужен и состав группы, и сосед за её пределами.
/// </remarks>
public class NestedGroupingTests
{
    private const double CellWidth = 60;
    private const double CellHeight = 40;

    private static readonly string[] Names = { "A", "B", "C", "D" };

    private static EditorHarness Create()
    {
        var nodes = new List<TestNode> { new("form0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var panel = new AbsolutePanel();

                for (var i = 0; i < Names.Length; i++)
                {
                    var cell = new Border
                    {
                        Name = Names[i],
                        Width = CellWidth,
                        Height = CellHeight,
                        Background = Brushes.Transparent
                    };

                    DesignLayout.SetX(cell, 10 + (i * 90));
                    DesignLayout.SetY(cell, 10);
                    panel.Children.Add(cell);
                }

                return panel;
            }, supportsRecycling: false)
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        editor.InteractionOptions.IsSnapToGridEnabled = false;
        editor.InteractionOptions.IsSnapToGuidesEnabled = false;
        window.Show();

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();
        harness.PlaceContainer(0, new Point(100, 100), new Size(400, 150));
        return harness;
    }

    private static Border Cell(EditorHarness harness, string name) => harness.Named(0, name);

    private static void Click(EditorHarness harness, string name, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = harness.CentreOf(Cell(harness, name));
        harness.Window.MouseDown(point, MouseButton.Left, modifiers);
        harness.Window.MouseUp(point, MouseButton.Left, modifiers);
        harness.RunLayout();
    }

    private static void Select(EditorHarness harness, params string[] names)
    {
        for (var i = 0; i < names.Length; i++)
            Click(harness, names[i], i == 0 ? RawInputModifiers.None : RawInputModifiers.Shift);
    }

    /// <summary>Собирает группу из A и B.</summary>
    private static EditorHarness CreateWithGroup()
    {
        var harness = Create();
        Select(harness, "A", "B");
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();
        return harness;
    }

    private static string? PathOf(EditorHarness harness, string name) => DesignGroup.GetId(Cell(harness, name));

    // ---- Вложенность ------------------------------------------------------------

    /// <summary>
    /// Группа плюс сосед дают группу внутри группы, а не растворение прежней.
    /// </summary>
    /// <remarks>
    /// Пометка — путь от внешней группы к внутренней, поэтому прежний идентификатор
    /// остаётся хвостом. Плоская модель на этом месте переписывала участникам
    /// идентификатор целиком, и внутренняя группа исчезала.
    /// </remarks>
    [AvaloniaFact]
    public void Grouping_A_Group_With_A_Neighbour_Nests_It()
    {
        var harness = CreateWithGroup();
        var inner = PathOf(harness, "A")!;

        // Клик по участнику выбирает всю группу, Shift добавляет соседа.
        Select(harness, "A", "C");
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        var outer = PathOf(harness, "C")!;
        Assert.NotEqual(inner, outer);
        Assert.Equal(outer + "/" + inner, PathOf(harness, "A"));
        Assert.Equal(outer + "/" + inner, PathOf(harness, "B"));
        Assert.Null(PathOf(harness, "D"));
    }

    /// <summary>
    /// Выбранная группа и сосед рисуются двумя рамками.
    /// </summary>
    /// <remarks>
    /// Кластер — это то, что пользователь видит одной рамкой: группа целиком либо
    /// одиночный контрол. Плоская модель на этом месте показывала три рамки, то есть
    /// обещала три независимых элемента там, где их два.
    /// </remarks>
    [AvaloniaFact]
    public void A_Selected_Group_And_A_Neighbour_Draw_Two_Frames()
    {
        var harness = CreateWithGroup();

        Select(harness, "A", "C");

        var adorners = harness.Editor.SecondarySelectionAdorners;
        Assert.Equal(2, adorners.Count);

        var groupFrame = Assert.Single(adorners, a => a.Role == SelectionAdornerRole.Group);
        var expected = DesignBoundsOf(harness, "A").Union(DesignBoundsOf(harness, "B"));
        Assert.Equal(expected, groupFrame.Bounds);

        var loneFrame = Assert.Single(adorners, a => a.Role != SelectionAdornerRole.Group);
        Assert.Same(Cell(harness, "C"), loneFrame.Target);
    }

    private static Rect DesignBoundsOf(EditorHarness harness, string name)
    {
        Assert.True(harness.Editor.TryGetDesignBounds(Cell(harness, name), out var bounds));
        return bounds;
    }

    /// <summary>Собирает внешнюю группу из группы A+B и соседа C.</summary>
    private static EditorHarness CreateNested(out string outer, out string inner)
    {
        var harness = CreateWithGroup();
        Select(harness, "A", "C");
        Assert.True(harness.Editor.GroupSelection());
        harness.RunLayout();

        outer = PathOf(harness, "C")!;
        inner = PathOf(harness, "A")!;
        return harness;
    }

    // ---- Чтение и операции ------------------------------------------------------

    [AvaloniaFact]
    public void Nested_Members_Read_Back_As_A_Tree()
    {
        var harness = CreateNested(out var outer, out var inner);
        var container = harness.Container(0);

        var root = Assert.Single(harness.Editor.GetGroups(container));
        Assert.Equal(outer, root.Path);
        Assert.Equal(outer, root.Id);
        Assert.Equal(new Control[] { Cell(harness, "C") }, root.Members);

        var nested = Assert.Single(root.Groups);
        Assert.Equal(inner, nested.Path);
        Assert.Equal(new Control[] { Cell(harness, "A"), Cell(harness, "B") }, nested.Members);

        // Весь состав отдаёт GetGroupMembers, включая вложенные уровни.
        Assert.Equal(
            new Control[] { Cell(harness, "A"), Cell(harness, "B"), Cell(harness, "C") },
            harness.Editor.GetGroupMembers(container, outer));
    }

    /// <summary>
    /// Роспуск снимает один внешний уровень.
    /// </summary>
    /// <remarks>
    /// Вложенная группа переживает его и поднимается на уровень выше: дробить заодно
    /// и её значило бы разрушить структуру, которую собирали отдельным действием.
    /// </remarks>
    [AvaloniaFact]
    public void Ungrouping_Removes_Only_The_Outer_Level()
    {
        var harness = CreateNested(out _, out var inner);
        var leaf = inner.Substring(inner.IndexOf('/') + 1);

        Click(harness, "A");
        Assert.True(harness.Editor.UngroupSelection());
        harness.RunLayout();

        Assert.Equal(leaf, PathOf(harness, "A"));
        Assert.Equal(leaf, PathOf(harness, "B"));
        Assert.Null(PathOf(harness, "C"));
    }

    [AvaloniaFact]
    public void Renaming_Renames_One_Segment()
    {
        var harness = CreateNested(out var outer, out var inner);

        Assert.True(harness.Editor.RenameGroup(harness.Container(0), inner, "left"));
        harness.RunLayout();

        Assert.Equal(outer + "/left", PathOf(harness, "A"));
        Assert.Equal(outer + "/left", PathOf(harness, "B"));
        Assert.Equal(outer, PathOf(harness, "C"));
    }

    /// <summary>
    /// Переезд на имя, занятое братом, отклоняется.
    /// </summary>
    /// <remarks>
    /// Личность группы — путь целиком, поэтому одинаковые имена на разных ветках
    /// не сталкиваются, а под одним родителем — сливают две группы в одну.
    /// </remarks>
    [AvaloniaFact]
    public void A_Sibling_Segment_Is_Refused()
    {
        var harness = CreateNested(out var outer, out var inner);
        DesignGroup.SetId(Cell(harness, "D"), outer + "/left");

        Assert.False(harness.Editor.RenameGroup(harness.Container(0), inner, "left"));
        Assert.Equal(inner, PathOf(harness, "A"));
    }

    // ---- Выделение --------------------------------------------------------------

    /// <summary>
    /// Повторный клик по тому же месту спускается ровно на один уровень.
    /// </summary>
    /// <remarks>
    /// Первый клик берёт самую внешнюю группу, второй входит в неё и берёт вложенную
    /// целиком, третий — сам контрол. Проваливаться сразу до контрола нельзя: тогда
    /// промежуточные уровни указателем не выбрать, а вход в группу ради работы внутри
    /// неё и заводился.
    /// </remarks>
    [AvaloniaFact]
    public void Clicking_Again_Descends_One_Level()
    {
        var harness = CreateNested(out _, out _);

        Click(harness, "A");
        Assert.Equal(3, harness.Editor.SelectedDesignTargetsCount);

        Click(harness, "A");
        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);

        Click(harness, "A");
        Assert.Equal(1, harness.Editor.SelectedDesignTargetsCount);
    }

    /// <summary>
    /// Наполовину выбранная группа кластером не становится.
    /// </summary>
    /// <remarks>
    /// Рамка группы обещает жест над всем её составом. Выбрать часть указателем нельзя,
    /// но можно публичным <c>SelectDesignTarget</c> — и тогда рамка соврала бы.
    /// </remarks>
    [AvaloniaFact]
    public void A_Partly_Selected_Group_Is_Not_A_Cluster()
    {
        var harness = CreateWithGroup();

        harness.Editor.SelectDesignTarget(Cell(harness, "A"), additive: false);
        harness.Editor.SelectDesignTarget(Cell(harness, "C"), additive: true);
        harness.RunLayout();

        var adorners = harness.Editor.SecondarySelectionAdorners;
        Assert.Equal(2, adorners.Count);
        Assert.DoesNotContain(adorners, a => a.Role == SelectionAdornerRole.Group);
    }

    // ---- Жест -------------------------------------------------------------------

    /// <summary>
    /// Ручки рамки кластера масштабируют только его состав.
    /// </summary>
    /// <remarks>
    /// Группа и внутри жеста остаётся одним элементом: сосед, выбранный рядом, своей
    /// рамкой и своими ручками распоряжается сам.
    /// </remarks>
    [AvaloniaFact]
    public void Resizing_A_Group_Cluster_Scales_Only_Its_Members()
    {
        var harness = CreateWithGroup();
        Select(harness, "A", "C");

        var before = DesignBoundsOf(harness, "C");
        var frame = DesignBoundsOf(harness, "A").Union(DesignBoundsOf(harness, "B"));

        var adorner = harness.Editor.GetVisualDescendants()
            .OfType<SelectionAdornerLayer>()
            .SelectMany(layer => layer.GetVisualChildren().OfType<SelectionAdorner>())
            .Single(a => a.Role == SelectionAdornerRole.Group);

        var delta = new Vector(30, 0);
        adorner.RaiseEvent(new ResizeStartedEventArgs(default, ResizeDirection.Right, SelectionAdorner.ResizeStartedEvent));
        adorner.RaiseEvent(new ResizeDeltaEventArgs(delta, ResizeDirection.Right, SelectionAdorner.ResizeDeltaEvent));
        adorner.RaiseEvent(new VectorEventArgs { RoutedEvent = SelectionAdorner.ResizeCompletedEvent, Vector = delta });
        harness.RunLayout();

        Assert.Equal(before, DesignBoundsOf(harness, "C"));
        Assert.True(DesignBoundsOf(harness, "A").Union(DesignBoundsOf(harness, "B")).Width > frame.Width);
    }
}
