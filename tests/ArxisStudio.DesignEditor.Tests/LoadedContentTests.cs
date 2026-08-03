using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ArxisStudio.Controls;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Контейнер как хост формы, загруженной из <c>.axaml</c>.
/// </summary>
/// <remarks>
/// Разметку никто не размечал designer-метаданными, и она интерактивна:
/// это два условия, которых нет у шаблонов, написанных вместе с приложением.
/// </remarks>
public class LoadedContentTests
{
    private const string Markup = """
        <UserControl xmlns='https://github.com/avaloniaui'
                     xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
          <StackPanel x:Name='Root' Spacing='10'>
            <TextBlock x:Name='Title' Text='Loaded Form' Height='24' />
            <TextBox x:Name='Field' Height='30' />
            <Button x:Name='Action' Content='Save' Height='30' />
          </StackPanel>
        </UserControl>
        """;

    private static readonly Point CardLocation = new(100, 100);
    private static readonly Size CardSize = new(300, 240);

    private static (EditorHarness Harness, DesignEditorItem Container) Create(DesignContentMode mode)
    {
        var nodes = new[] { new TestNode("loaded") };

        var editor = new DesignEditor
        {
            ItemsSource = nodes,
            SelectionMode = SelectionMode.Multiple,
            ItemTemplate = new FuncDataTemplate<TestNode>(
                (_, _) => (Control)AvaloniaRuntimeXamlLoader.Parse(Markup),
                supportsRecycling: false)
        };

        editor.InteractionOptions.IsSnapToGridEnabled = false;

        var window = new Window { Width = 800, Height = 600, Content = editor };
        window.Show();

        var harness = EditorHarness.Adopt(window, editor, nodes);
        harness.RunLayout();

        var container = harness.Container(0);
        container.ContentMode = mode;
        harness.PlaceContainer(0, CardLocation, CardSize);

        return (harness, container);
    }

    private static T Find<T>(EditorHarness harness, string name) where T : Control
        => harness.Container(0).GetVisualDescendants().OfType<T>().First(c => c.Name == name);

    [AvaloniaFact]
    public void Live_Content_Swallows_The_Press_Without_The_Loaded_Mode()
    {
        var (harness, _) = Create(DesignContentMode.Annotated);
        var action = Find<Button>(harness, "Action");

        var clicked = false;
        action.Click += (_, _) => clicked = true;

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        // Ради этих двух строк и появился режим Loaded: загруженная форма
        // интерактивна, кнопка обрабатывает нажатие сама, до контейнера оно
        // не доходит — и выделения не возникает вовсе.
        Assert.True(clicked);
        Assert.Null(harness.Editor.PrimarySelectionTarget);
    }

    [AvaloniaFact]
    public void Loaded_Mode_Selects_The_Element_Under_The_Pointer()
    {
        var (harness, _) = Create(DesignContentMode.Loaded);
        var action = Find<Button>(harness, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        Assert.Same(action, harness.Editor.PrimarySelectionTarget!.Target);
        Assert.Equal(DesignSelectionScope.NestedTarget, harness.Editor.PrimarySelectionTarget.Scope);
    }

    [AvaloniaFact]
    public void Loaded_Mode_Does_Not_Expose_Control_Internals()
    {
        var (harness, _) = Create(DesignContentMode.Loaded);
        var action = Find<Button>(harness, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        // Выбирается сама кнопка, а не её внутренний ContentPresenter:
        // у частей шаблона задан TemplatedParent.
        var selected = harness.Editor.PrimarySelectionTarget!.Target;
        Assert.Null(selected.TemplatedParent);
        Assert.IsType<Button>(selected);
    }

    [AvaloniaFact]
    public void Loaded_Content_Does_Not_React_To_Input()
    {
        var (harness, _) = Create(DesignContentMode.Loaded);
        var action = Find<Button>(harness, "Action");

        var clicked = false;
        action.Click += (_, _) => clicked = true;

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        // Форма под редактированием не должна срабатывать: иначе редактор
        // нажимал бы кнопки вместо того, чтобы их выделять.
        Assert.False(clicked);
        Assert.False(action.IsFocused);
    }

    [AvaloniaFact]
    public void Loaded_Mode_Reports_The_Layout_Of_The_Selected_Element()
    {
        var (harness, _) = Create(DesignContentMode.Loaded);
        var action = Find<Button>(harness, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        // Стратегии размещения работают с загруженной формой без изменений:
        // корень разметки — StackPanel, значит перестановка, а не координаты.
        Assert.Equal("Stack", harness.Editor.PrimarySelectionPlacement);
        Assert.Equal(ArxisStudio.Attached.MovePolicy.None, harness.Editor.PrimarySelectionMovePolicy);
    }

    [AvaloniaFact]
    public void Loaded_Element_Can_Be_Resized()
    {
        var (harness, container) = Create(DesignContentMode.Loaded);
        var action = Find<Button>(harness, "Action");

        var centre = harness.CentreOf(action);
        harness.Window.MouseDown(centre, MouseButton.Left);
        harness.Window.MouseUp(centre, MouseButton.Left);
        harness.RunLayout();

        var before = harness.Editor.GetDesignSize(action).Height;

        var state = new ArxisStudio.States.ItemResizingState(container, action, ResizeDirection.Bottom);
        container.PushState(state);
        state.OnResizeDelta(new ResizeDeltaEventArgs(
            new Vector(0, 40), ResizeDirection.Bottom, DesignEditorItem.ResizeDeltaEvent));
        harness.RunLayout();

        // Размер honours любая панель, поэтому загруженная форма редактируется
        // по размеру сразу, без разметки.
        Assert.Equal(before + 40, harness.Editor.GetDesignSize(action).Height, 1);
    }
}
