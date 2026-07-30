using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;
using ArxisStudio.Controls;
using Xunit;
using DesignLayout = ArxisStudio.Attached.Layout;

namespace ArxisStudio.Tests;

/// <summary>
/// Характеризующие тесты вложенных контейнеров.
/// </summary>
/// <remarks>
/// Фиксируют фактическое поведение модели выделения, когда внутри
/// <see cref="DesignEditorItem"/> находится другой <see cref="DesignEditorItem"/>.
/// Задача — не защитить текущее поведение, а зафиксировать точку отсчёта
/// перед переходом на дерево любой глубины.
/// </remarks>
public class NestedContainerTests
{
    private const double OuterSize = 200;
    private const double InnerOffset = 20;
    private const double InnerSize = 80;

    private static readonly Point OuterLocation = new(100, 100);

    /// <summary>Точка внутри вложенного контейнера, в координатах редактора.</summary>
    private static Point InnerCentre => new(
        OuterLocation.X + InnerOffset + (InnerSize / 2),
        OuterLocation.Y + InnerOffset + (InnerSize / 2));

    private sealed record Node(string Name);

    private static (Window Window, DesignEditor Editor, Node Item) Create()
    {
        var item = new Node("outer");

        var editor = new DesignEditor
        {
            ItemsSource = new[] { item },
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<Node>((_, _) =>
            {
                var inner = new DesignEditorItem
                {
                    Name = "Inner",
                    Width = InnerSize,
                    Height = InnerSize,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Top,
                    Content = new Border { Background = null }
                };
                DesignLayout.SetX(inner, InnerOffset);
                DesignLayout.SetY(inner, InnerOffset);

                var panel = new AbsolutePanel();
                panel.Children.Add(inner);
                return panel;
            }, supportsRecycling: false)
        };

        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();
        RunLayout(window);

        var outer = (DesignEditorItem)editor.ContainerFromItem(item)!;
        outer.Width = OuterSize;
        outer.Height = OuterSize;
        outer.HorizontalAlignment = HorizontalAlignment.Left;
        outer.VerticalAlignment = VerticalAlignment.Top;
        outer.Location = OuterLocation;
        RunLayout(window);

        return (window, editor, item);
    }

    private static void RunLayout(Window window)
    {
        var manager = window.GetLayoutManager();
        manager?.ExecuteInitialLayoutPass();
        manager?.ExecuteLayoutPass();
    }

    private static DesignEditorItem Inner(DesignEditor editor) =>
        editor.GetVisualDescendants().OfType<DesignEditorItem>().Single(i => i.Name == "Inner");

    [AvaloniaFact]
    public void Nested_Container_Is_Realised_Inside_Outer_Container()
    {
        var (_, editor, item) = Create();

        var outer = (DesignEditorItem)editor.ContainerFromItem(item)!;
        var inner = Inner(editor);

        Assert.NotSame(outer, inner);
        Assert.Contains(outer, inner.GetVisualAncestors());
    }

    [AvaloniaFact]
    public void Nested_Container_Is_Not_An_Item_Of_The_Editor()
    {
        var (_, editor, item) = Create();

        var outer = (DesignEditorItem)editor.ContainerFromItem(item)!;
        var inner = Inner(editor);

        // Корень проблемы: индексная модель выбора Avalonia знает только
        // контейнеры собственного ItemsSource. Для вложенного индекса нет.
        Assert.Equal(0, editor.IndexFromContainer(outer));
        Assert.Equal(-1, editor.IndexFromContainer(inner));
    }

    [AvaloniaFact]
    public void Editor_Only_Realises_Top_Level_Containers_As_Items()
    {
        var (_, editor, _) = Create();

        // ItemsSource порождает ровно один контейнер: вложенный редактору не принадлежит.
        Assert.Equal(1, editor.ItemCount);
    }

    [AvaloniaFact]
    public void Click_On_Nested_Container_Selects_It_As_Nested_Target()
    {
        var (window, editor, _) = Create();
        var outer = (DesignEditorItem)editor.ContainerFromItem(editor.ItemsSource!.Cast<object>().First())!;
        var inner = Inner(editor);

        window.MouseDown(InnerCentre, MouseButton.Left);
        window.MouseUp(InnerCentre, MouseButton.Left);
        RunLayout(window);

        // Вложенный контейнер выбирается как target внутри владеющего item'а:
        // индексная модель Avalonia продолжает оперировать верхним уровнем,
        // а сам вложенный DesignEditorItem становится design target.
        var primary = editor.PrimarySelectionTarget;
        Assert.NotNull(primary);
        Assert.Same(inner, primary!.Target);
        Assert.Same(outer, primary.Container);
        Assert.True(editor.HasSingleSelection);
    }

    [AvaloniaFact]
    public void Selecting_Nested_Container_Marks_The_Owning_Item_Selected()
    {
        var (window, editor, item) = Create();
        var outer = (DesignEditorItem)editor.ContainerFromItem(item)!;

        window.MouseDown(InnerCentre, MouseButton.Left);
        window.MouseUp(InnerCentre, MouseButton.Left);
        RunLayout(window);

        // Индексная модель Avalonia продолжает работать: выбран владеющий item.
        Assert.True(outer.IsSelected);
        Assert.Equal(1, editor.Selection.Count);
    }

    [AvaloniaFact]
    public void Nested_Container_Reports_Container_Scope()
    {
        var (window, editor, _) = Create();

        window.MouseDown(InnerCentre, MouseButton.Left);
        window.MouseUp(InnerCentre, MouseButton.Left);
        RunLayout(window);

        var primary = editor.PrimarySelectionTarget!;

        // Scope определяется типом target: вложенный DesignEditorItem — контейнер,
        // хотя владеющий item верхнего уровня другой.
        Assert.Equal(DesignSelectionScope.Container, primary.Scope);
        Assert.NotSame(primary.Container, primary.Target);
    }

    [AvaloniaFact]
    public void Depth_Reflects_Position_In_The_Container_Tree()
    {
        var (_, editor, item) = Create();
        var outer = (DesignEditorItem)editor.ContainerFromItem(item)!;
        var inner = Inner(editor);

        Assert.Equal(0, new DesignSelectionTarget(outer, outer).Depth);
        Assert.Equal(1, new DesignSelectionTarget(outer, inner).Depth);
    }

    [AvaloniaFact]
    public void Nested_Container_Can_Be_Dragged()
    {
        var (window, editor, _) = Create();
        var inner = Inner(editor);
        var start = inner.Location;

        window.MouseDown(InnerCentre, MouseButton.Left);
        // Первый сдвиг переводит контейнер в состояние перетаскивания,
        // последующие — двигают его.
        window.MouseMove(InnerCentre + new Vector(10, 6));
        window.MouseMove(InnerCentre + new Vector(50, 30));
        window.MouseUp(InnerCentre + new Vector(50, 30), MouseButton.Left);
        RunLayout(window);

        Assert.Equal(start.X + 50, inner.Location.X, 1);
        Assert.Equal(start.Y + 30, inner.Location.Y, 1);
    }
}
