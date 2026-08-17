using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using ArxisStudio.Attached;
using ArxisStudio.Controls;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Линейка и вытягивание направляющих с неё.
/// </summary>
/// <remarks>
/// Линейка стоит <b>рядом</b> с редактором, а не внутри его шаблона, и это решение
/// про координаты: у редактора точка ввода совпадает с точкой viewport'а, а слой,
/// занявший место сверху или слева, сдвинул бы это соответствие и потянул бы за собой
/// все три системы координат. Отсюда и стенд: окно, в нём линейки по краям и редактор
/// в остатке, то есть ровно та композиция, которую собирает хост.
/// <para>
/// Из этого же следует смещение в тестах: точка редактора отстоит от точки окна
/// на толщину линейки.
/// </para>
/// </remarks>
public class DesignRulerTests
{
    private const double RulerSize = 20;

    private sealed record Stand(
        EditorHarness Harness,
        DesignRuler Top,
        DesignRuler Left,
        ObservableCollection<DesignGuide> Guides,
        List<DesignGuideChangeRequestedEventArgs> Requests);

    /// <summary>Переводит точку редактора в координаты окна.</summary>
    private static Point InWindow(double x, double y) => new(x + RulerSize, y + RulerSize);

    private static Stand Create(bool subscribe = true)
    {
        var nodes = new List<TestNode> { new("ruler0") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>((_, _) =>
            {
                var nested = new Border { Name = "Nested", Width = 60, Height = 40 };
                Layout.SetX(nested, 10);
                Layout.SetY(nested, 10);

                var panel = new AbsolutePanel();
                panel.Children.Add(nested);
                return panel;
            }, supportsRecycling: false)
        };

        editor.InteractionOptions.IsSnapToGridEnabled = false;
        editor.InteractionOptions.IsSnapToGuidesEnabled = false;

        var guides = new ObservableCollection<DesignGuide>();
        editor.Guides = guides;

        var top = new DesignRuler { Orientation = Orientation.Horizontal, Editor = editor, Height = RulerSize };
        var left = new DesignRuler { Orientation = Orientation.Vertical, Editor = editor, Width = RulerSize };

        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            RowDefinitions = new RowDefinitions("Auto,*")
        };

        Grid.SetColumn(top, 1);
        Grid.SetRow(left, 1);
        Grid.SetColumn(editor, 1);
        Grid.SetRow(editor, 1);

        grid.Children.Add(top);
        grid.Children.Add(left);
        grid.Children.Add(editor);

        var window = new Window { Width = 800, Height = 600, Content = grid };
        window.Show();

        var requests = new List<DesignGuideChangeRequestedEventArgs>();
        if (subscribe)
        {
            editor.GuideChangeRequested += (_, e) =>
            {
                requests.Add(e);
                guides.Add(e.Guide);
                e.Handled = true;
            };
        }

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();
        return new Stand(harness, top, left, guides, requests);
    }

    // ---- Композиция -----------------------------------------------------------

    /// <summary>
    /// Линейка не занимает места у редактора: соответствие ввода и viewport'а цело.
    /// </summary>
    /// <remarks>
    /// Это главное, ради чего линейка вынесена наружу. Если бы она стояла внутри
    /// шаблона, точка (0,0) редактора уехала бы на её толщину, и вместе с ней уехали
    /// бы hit-testing, рамка и все мировые координаты.
    /// </remarks>
    [AvaloniaFact]
    public void The_Editor_Origin_Still_Maps_To_The_Viewport_Origin()
    {
        var stand = Create();

        Assert.Equal(new Point(0, 0), stand.Harness.Editor.GetWorldPosition(new Point(0, 0)));
        Assert.Equal(RulerSize, stand.Harness.Editor.Bounds.X, 3);
        Assert.Equal(RulerSize, stand.Harness.Editor.Bounds.Y, 3);
    }

    // ---- Создание -------------------------------------------------------------

    /// <summary>
    /// Протяжка с верхней линейки создаёт горизонтальную направляющую.
    /// </summary>
    [AvaloniaFact]
    public void Dragging_From_The_Top_Ruler_Adds_A_Horizontal_Guide()
    {
        var stand = Create();

        stand.Harness.Window.MouseDown(InWindow(300, -10), MouseButton.Left);
        stand.Harness.Window.MouseMove(InWindow(300, 180));
        stand.Harness.Window.MouseUp(InWindow(300, 180), MouseButton.Left);
        stand.Harness.RunLayout();

        var request = Assert.Single(stand.Requests);
        Assert.Equal(DesignGuideChangeKind.Add, request.Kind);
        Assert.Null(request.Original);
        Assert.Equal(DesignGuide.Horizontal(180), request.Guide);
    }

    /// <summary>
    /// Протяжка с левой линейки создаёт вертикальную.
    /// </summary>
    [AvaloniaFact]
    public void Dragging_From_The_Left_Ruler_Adds_A_Vertical_Guide()
    {
        var stand = Create();

        stand.Harness.Window.MouseDown(InWindow(-10, 300), MouseButton.Left);
        stand.Harness.Window.MouseMove(InWindow(240, 300));
        stand.Harness.Window.MouseUp(InWindow(240, 300), MouseButton.Left);
        stand.Harness.RunLayout();

        Assert.Equal(DesignGuide.Vertical(240), Assert.Single(stand.Requests).Guide);
    }

    /// <summary>
    /// Отпускание мимо холста ничего не создаёт.
    /// </summary>
    /// <remarks>
    /// Тем же жестом направляющую убирают, поэтому «вытянул и передумал» обязан быть
    /// отменой, а не линией, приклеенной к краю.
    /// </remarks>
    [AvaloniaFact]
    public void Releasing_Back_On_The_Ruler_Creates_Nothing()
    {
        var stand = Create();

        stand.Harness.Window.MouseDown(InWindow(300, -10), MouseButton.Left);
        stand.Harness.Window.MouseMove(InWindow(300, 120));
        stand.Harness.Window.MouseMove(InWindow(300, -10));
        stand.Harness.Window.MouseUp(InWindow(300, -10), MouseButton.Left);
        stand.Harness.RunLayout();

        Assert.Empty(stand.Requests);
        Assert.Empty(stand.Guides);
    }

    /// <summary>
    /// Без подписчика линейка жест не начинает.
    /// </summary>
    [AvaloniaFact]
    public void Without_A_Subscriber_The_Ruler_Does_Nothing()
    {
        var stand = Create(subscribe: false);

        stand.Harness.Window.MouseDown(InWindow(300, -10), MouseButton.Left);
        stand.Harness.Window.MouseMove(InWindow(300, 180));
        stand.Harness.RunLayout();

        Assert.Null(stand.Harness.Editor.GuidePreview);

        stand.Harness.Window.MouseUp(InWindow(300, 180), MouseButton.Left);
        Assert.Empty(stand.Guides);
    }

    // ---- Превью ---------------------------------------------------------------

    [AvaloniaFact]
    public void The_Preview_Follows_The_Pointer_And_Clears_After()
    {
        var stand = Create();

        stand.Harness.Window.MouseDown(InWindow(300, -10), MouseButton.Left);
        stand.Harness.Window.MouseMove(InWindow(300, 180));
        stand.Harness.RunLayout();

        Assert.Equal(DesignGuide.Horizontal(180), stand.Harness.Editor.GuidePreview);

        stand.Harness.Window.MouseUp(InWindow(300, 180), MouseButton.Left);
        stand.Harness.RunLayout();

        Assert.Null(stand.Harness.Editor.GuidePreview);
    }

    /// <summary>
    /// Потеря захвата отменяет вытягивание и убирает превью.
    /// </summary>
    [AvaloniaFact]
    public void Losing_Capture_Cancels_The_Pull()
    {
        var stand = Create();

        IPointer? pointer = null;
        void Capture(object? sender, PointerPressedEventArgs e) => pointer ??= e.Pointer;

        stand.Top.AddHandler(InputElement.PointerPressedEvent, Capture, handledEventsToo: true);
        stand.Harness.Window.MouseDown(InWindow(300, -10), MouseButton.Left);
        stand.Top.RemoveHandler(InputElement.PointerPressedEvent, (EventHandler<PointerPressedEventArgs>)Capture);

        stand.Harness.Window.MouseMove(InWindow(300, 180));
        stand.Harness.RunLayout();
        Assert.NotNull(stand.Harness.Editor.GuidePreview);

        pointer!.Capture(null);
        stand.Harness.RunLayout();

        Assert.Null(stand.Harness.Editor.GuidePreview);
        Assert.Empty(stand.Requests);
    }

    // ---- Масштаб --------------------------------------------------------------

    /// <summary>
    /// Координата берётся мировая, а не экранная.
    /// </summary>
    /// <remarks>
    /// На двукратном увеличении 180 экранных пикселей — это 90 мировых плюс сдвиг
    /// viewport'а. Если бы линейка отдавала экранную координату, направляющая вставала
    /// бы мимо макета тем сильнее, чем ближе подъехал пользователь.
    /// </remarks>
    [AvaloniaFact]
    public void The_Guide_Lands_In_World_Coordinates()
    {
        var stand = Create();
        stand.Harness.Editor.ViewportZoom = 2.0;
        stand.Harness.Editor.ViewportLocation = new Point(0, 40);
        stand.Harness.RunLayout();

        stand.Harness.Window.MouseDown(InWindow(300, -10), MouseButton.Left);
        stand.Harness.Window.MouseMove(InWindow(300, 180));
        stand.Harness.Window.MouseUp(InWindow(300, 180), MouseButton.Left);
        stand.Harness.RunLayout();

        Assert.Equal(DesignGuide.Horizontal((180 / 2.0) + 40), Assert.Single(stand.Requests).Guide);
    }

    [AvaloniaFact]
    public void The_Pulled_Guide_Snaps_To_The_Grid()
    {
        var stand = Create();
        stand.Harness.Editor.InteractionOptions.IsSnapToGridEnabled = true;

        stand.Harness.Window.MouseDown(InWindow(300, -10), MouseButton.Left);
        stand.Harness.Window.MouseMove(InWindow(300, 173));
        stand.Harness.Window.MouseUp(InWindow(300, 173), MouseButton.Left);
        stand.Harness.RunLayout();

        // Шаг сетки 20: 173 садится на 180.
        Assert.Equal(DesignGuide.Horizontal(180), Assert.Single(stand.Requests).Guide);
    }
}
