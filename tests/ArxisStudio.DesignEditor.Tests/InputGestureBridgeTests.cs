using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Плоские свойства модификаторов и набор жестов под ними.
/// </summary>
/// <remarks>
/// <see cref="DesignEditor.ContainerInteractionModifiers"/> и
/// <see cref="DesignEditor.AdditiveSelectionModifiers"/> оставлены ради совместимости
/// и дублируют <see cref="DesignEditorInputGestures"/>. Дублирование живёт на ручной
/// синхронизации, а ручная синхронизация без тестов расходится — здесь она и закреплена.
/// <para>
/// Разошлась она ровно один раз и именно так, как расходится дублирование: чтение шло
/// у набора и было верным всегда, а уведомления при записи в набор не было вовсе.
/// Привязка к плоскому свойству показывала прежнее значение — при верном значении внутри.
/// </para>
/// </remarks>
public class InputGestureBridgeTests
{
    private static readonly Point ContainerLocation = new(100, 100);
    private static readonly Size ContainerSize = new(200, 150);

    private static Point NestedCentre => new(
        ContainerLocation.X + EditorHarness.NestedOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static Point SiblingCentre => new(
        ContainerLocation.X + EditorHarness.SiblingOffset + (EditorHarness.NestedWidth / 2),
        ContainerLocation.Y + EditorHarness.NestedOffset + (EditorHarness.NestedHeight / 2));

    private static EditorHarness Create()
    {
        var harness = EditorHarness.Create();
        harness.PlaceContainer(0, ContainerLocation, ContainerSize);
        return harness;
    }

    private static int CountRaises(DesignEditor editor, AvaloniaProperty property, Action change)
    {
        var raised = 0;
        void Handler(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == property)
                raised++;
        }

        editor.PropertyChanged += Handler;
        try
        {
            change();
        }
        finally
        {
            editor.PropertyChanged -= Handler;
        }

        return raised;
    }

    // ---- Согласованность двух сторон -------------------------------------------

    [AvaloniaFact]
    public void Writing_The_Flat_Property_Reaches_The_Set()
    {
        var harness = Create();

        harness.Editor.AdditiveSelectionModifiers = KeyModifiers.Alt;

        Assert.Equal(KeyModifiers.Alt, harness.Editor.InputGestures.AdditiveSelectionModifiers);
    }

    [AvaloniaFact]
    public void Writing_The_Set_Reaches_The_Flat_Property()
    {
        var harness = Create();

        harness.Editor.InputGestures.ContainerInteractionModifiers = KeyModifiers.Alt;

        Assert.Equal(KeyModifiers.Alt, harness.Editor.ContainerInteractionModifiers);
    }

    [AvaloniaFact]
    public void Replacing_The_Set_Reaches_The_Flat_Property()
    {
        var harness = Create();

        harness.Editor.InputGestures = new DesignEditorInputGestures
        {
            AdditiveSelectionModifiers = KeyModifiers.Meta
        };

        Assert.Equal(KeyModifiers.Meta, harness.Editor.AdditiveSelectionModifiers);
    }

    // ---- Уведомления ------------------------------------------------------------

    /// <summary>
    /// Запись в набор поднимает уведомление плоского свойства.
    /// </summary>
    /// <remarks>
    /// Это и был дефект: настройка через набор — путь, рекомендованный документацией, —
    /// уведомления не поднимала, и привязка к плоскому свойству оставалась со старым
    /// значением. Заметить это по значению нельзя: геттер читает у набора и врать не может.
    /// </remarks>
    [AvaloniaFact]
    public void Writing_The_Set_Raises_The_Flat_Property()
    {
        var harness = Create();

        var raised = CountRaises(
            harness.Editor,
            DesignEditor.AdditiveSelectionModifiersProperty,
            () => harness.Editor.InputGestures.AdditiveSelectionModifiers = KeyModifiers.Alt);

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// Запись через плоское свойство поднимает уведомление ровно один раз.
    /// </summary>
    /// <remarks>
    /// Такая запись проходит по обеим сторонам, и наивная ретрансляция дала бы два
    /// события на одно присваивание. Гасит второе не проверка в обработчике, а то,
    /// что <c>SetAndRaise</c> с уже записанным значением ничего не поднимает.
    /// </remarks>
    [AvaloniaFact]
    public void Writing_The_Flat_Property_Raises_Once()
    {
        var harness = Create();

        var raised = CountRaises(
            harness.Editor,
            DesignEditor.AdditiveSelectionModifiersProperty,
            () => harness.Editor.AdditiveSelectionModifiers = KeyModifiers.Alt);

        Assert.Equal(1, raised);
    }

    /// <summary>
    /// Заменённый набор редактор больше не слушает.
    /// </summary>
    /// <remarks>
    /// Иначе брошенный набор продолжал бы править редактор, которому он уже не принадлежит,
    /// и держал бы его живым через подписку.
    /// </remarks>
    [AvaloniaFact]
    public void A_Replaced_Set_No_Longer_Reaches_The_Editor()
    {
        var harness = Create();
        var abandoned = harness.Editor.InputGestures;

        harness.Editor.InputGestures = new DesignEditorInputGestures();

        var raised = CountRaises(
            harness.Editor,
            DesignEditor.AdditiveSelectionModifiersProperty,
            () => abandoned.AdditiveSelectionModifiers = KeyModifiers.Alt);

        Assert.Equal(0, raised);
        Assert.Equal(KeyModifiers.Shift, harness.Editor.AdditiveSelectionModifiers);
    }

    // ---- Настройка действует ----------------------------------------------------

    /// <summary>
    /// Заданный модификатор добавляет к выделению.
    /// </summary>
    [AvaloniaFact]
    public void The_Configured_Additive_Modifier_Adds()
    {
        var harness = Create();
        harness.Editor.InputGestures.AdditiveSelectionModifiers = KeyModifiers.Alt;

        Click(harness, NestedCentre, RawInputModifiers.None);
        Click(harness, SiblingCentre, RawInputModifiers.Alt);

        Assert.Equal(2, harness.Editor.SelectedDesignTargetsCount);
    }

    /// <summary>
    /// Прежний модификатор после этого добавлять перестаёт.
    /// </summary>
    /// <remarks>
    /// Половина без этой проверки прошла бы и на редакторе, который добавляет к выделению
    /// по любому нажатию: настройка считается работающей, только если она и включает,
    /// и выключает.
    /// </remarks>
    [AvaloniaFact]
    public void The_Previous_Additive_Modifier_Stops_Adding()
    {
        var harness = Create();
        harness.Editor.InputGestures.AdditiveSelectionModifiers = KeyModifiers.Alt;

        Click(harness, NestedCentre, RawInputModifiers.None);
        Click(harness, SiblingCentre, RawInputModifiers.Shift);

        Assert.Equal(1, harness.Editor.SelectedDesignTargetsCount);
        Assert.Equal("Sibling", harness.Editor.PrimarySelectionTarget?.Target.Name);
    }

    private static void Click(EditorHarness harness, Point point, RawInputModifiers modifiers)
    {
        harness.Window.MouseDown(point, MouseButton.Left, modifiers);
        harness.Window.MouseUp(point, MouseButton.Left, modifiers);
        harness.RunLayout();
    }
}
