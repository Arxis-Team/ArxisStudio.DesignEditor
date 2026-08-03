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
/// Деревом контролов редактор не владеет: он распознаёт жест, показывает точку
/// вставки и поднимает <c>ReorderRequested</c>. Саму перестановку выполняет
/// приложение — здесь его роль играет обработчик в тесте.
/// </remarks>
public class ReorderTests
{
    private static readonly Point CardLocation = new(100, 100);
    private static readonly Size CardSize = new(340, 400);

    private static readonly Vector DragDelta = new(60, 40);

    private sealed record Request(Control Target, int OldIndex, int NewIndex);

    /// <summary>
    /// Собирает стенд и подключает обработчик, который выполняет перестановку.
    /// </summary>
    private static (EditorHarness Harness, List<Request> Requests) Create(bool handle = true)
    {
        var harness = EditorHarness.CreateStackHosted();
        harness.PlaceContainer(0, CardLocation, CardSize);

        var requests = new List<Request>();
        harness.Editor.ReorderRequested += (_, e) =>
        {
            requests.Add(new Request(e.Target, e.OldIndex, e.NewIndex));

            if (!handle)
                return;

            // Роль библиотеки разметки: структурную правку делает она.
            if (e.Target.GetVisualParent() is Panel panel)
            {
                panel.Children.Move(e.OldIndex, e.NewIndex);
                e.Handled = true;
            }
        };

        return (harness, requests);
    }

    private static Panel PanelOf(Control child) => (Panel)child.GetVisualParent()!;

    private static void Drag(EditorHarness harness, Point from, Vector delta)
    {
        harness.Window.MouseDown(from, MouseButton.Left);
        harness.Window.MouseMove(from + new Vector(4, 6));
        harness.Window.MouseMove(from + delta);
        harness.Window.MouseUp(from + delta, MouseButton.Left);
        harness.RunLayout();
    }

    [AvaloniaFact]
    public void Gesture_Asks_To_Reorder_And_Reports_Both_Indices()
    {
        var (harness, requests) = Create();
        var action = harness.Find<Button>(0, "Action");

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        var request = Assert.Single(requests);
        Assert.Same(action, request.Target);
        Assert.Equal(1, request.OldIndex);
        Assert.Equal(0, request.NewIndex);
    }

    [AvaloniaFact]
    public void Handled_Request_Changes_The_Order()
    {
        var (harness, _) = Create();
        var action = harness.Find<Button>(0, "Action");
        var field = harness.Find<TextBox>(0, "Field");
        var panel = PanelOf(action);

        Assert.Equal(0, panel.Children.IndexOf(field));
        Assert.Equal(1, panel.Children.IndexOf(action));

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        Assert.Equal(0, panel.Children.IndexOf(action));
        Assert.Equal(1, panel.Children.IndexOf(field));
    }

    [AvaloniaFact]
    public void Unhandled_Request_Leaves_The_Tree_Alone()
    {
        var (harness, requests) = Create(handle: false);
        var action = harness.Find<Button>(0, "Action");
        var panel = PanelOf(action);

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        // Редактор сообщил намерение, но сам ничего не тронул.
        Assert.Single(requests);
        Assert.Equal(1, panel.Children.IndexOf(action));
    }

    [AvaloniaFact]
    public void Reorder_Stays_Out_Of_The_Edit_Contract()
    {
        var (harness, _) = Create();
        var action = harness.Find<Button>(0, "Action");

        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        // Структурная правка не принадлежит редактору, поэтому и в его поток
        // изменений не попадает: там живут геометрия и порядок перекрытия.
        Assert.Empty(edits);
    }

    [AvaloniaFact]
    public void Gesture_Returning_To_The_Original_Slot_Asks_For_Nothing()
    {
        var (harness, requests) = Create();
        var action = harness.Find<Button>(0, "Action");

        // Смещение меньше половины соседа: точка вставки не меняется.
        Drag(harness, harness.CentreOf(action), new Vector(0, -4));

        Assert.Empty(requests);
    }

    [AvaloniaFact]
    public void User_Lock_Blocks_Reorder()
    {
        var (harness, requests) = Create();
        var action = harness.Find<Button>(0, "Action");
        DesignInteraction.SetMovePolicy(action, MovePolicy.None);

        Drag(harness, harness.CentreOf(action), new Vector(0, -60));

        // Запрет пользователя сильнее любой раскладки, включая перестановку.
        Assert.Empty(requests);
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

        Drag(harness, harness.CentreOf(nested), DragDelta);

        // Раскладка с абсолютным позиционированием не превращается в перестановку,
        // и перемещение остаётся зоной редактора.
        var edit = Assert.Single(edits);
        Assert.Equal(DesignEditKind.Move, edit.Kind);
        Assert.IsType<DesignGeometryChange>(Assert.Single(edit.Changes));
    }
}
