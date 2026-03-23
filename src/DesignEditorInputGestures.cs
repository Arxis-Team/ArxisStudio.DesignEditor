using Avalonia;
using Avalonia.Input;

namespace ArxisStudio;

/// <summary>
/// Определяет кнопку указателя, используемую для запуска жестов редактора.
/// </summary>
/// <remarks>
/// Перечисление используется свойствами класса <see cref="DesignEditorInputGestures"/>
/// и описывает, какая кнопка мыши инициирует соответствующее действие.
/// </remarks>
public enum DesignEditorPointerButton
{
    /// <summary>
    /// Левая кнопка мыши.
    /// </summary>
    Left,

    /// <summary>
    /// Средняя кнопка мыши (как правило, колесо).
    /// </summary>
    Middle,

    /// <summary>
    /// Правая кнопка мыши.
    /// </summary>
    Right
}

/// <summary>
/// Представляет набор настраиваемых жестов ввода для <see cref="DesignEditor"/>.
/// </summary>
/// <remarks>
/// Объект задает политику пользовательского ввода редактора:
/// кнопки указателя и модификаторы клавиатуры для панорамирования, marquee-selection,
/// контейнерного взаимодействия и additive selection.
/// <para>
/// Экземпляр можно конфигурировать из XAML, через стили, code-behind или binding в MVVM.
/// </para>
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <design:DesignEditor.InputGestures>
///     <design:DesignEditorInputGestures PanButton="Middle"
///                                       PanModifiers="None"
///                                       MarqueeButton="Left"
///                                       MarqueeModifiers="None"
///                                       ZoomModifiers="None"
///                                       ContainerInteractionModifiers="Control"
///                                       AdditiveSelectionModifiers="Shift" />
/// </design:DesignEditor.InputGestures>
/// ]]></code>
/// </example>
public class DesignEditorInputGestures : AvaloniaObject
{
    /// <summary>
    /// Идентификатор свойства кнопки указателя для запуска панорамирования.
    /// </summary>
    /// <remarks>Значение по умолчанию: <see cref="DesignEditorPointerButton.Middle"/>.</remarks>
    public static readonly StyledProperty<DesignEditorPointerButton> PanButtonProperty =
        AvaloniaProperty.Register<DesignEditorInputGestures, DesignEditorPointerButton>(
            nameof(PanButton),
            DesignEditorPointerButton.Middle);

    /// <summary>
    /// Идентификатор свойства модификаторов клавиатуры для запуска панорамирования.
    /// </summary>
    /// <remarks>Значение по умолчанию: <see cref="KeyModifiers.None"/>.</remarks>
    public static readonly StyledProperty<KeyModifiers> PanModifiersProperty =
        AvaloniaProperty.Register<DesignEditorInputGestures, KeyModifiers>(
            nameof(PanModifiers),
            KeyModifiers.None);

    /// <summary>
    /// Идентификатор свойства кнопки указателя для запуска marquee-selection по пустой области редактора.
    /// </summary>
    /// <remarks>Значение по умолчанию: <see cref="DesignEditorPointerButton.Left"/>.</remarks>
    public static readonly StyledProperty<DesignEditorPointerButton> MarqueeButtonProperty =
        AvaloniaProperty.Register<DesignEditorInputGestures, DesignEditorPointerButton>(
            nameof(MarqueeButton),
            DesignEditorPointerButton.Left);

    /// <summary>
    /// Идентификатор свойства модификаторов клавиатуры для запуска marquee-selection.
    /// </summary>
    /// <remarks>Значение по умолчанию: <see cref="KeyModifiers.None"/>.</remarks>
    public static readonly StyledProperty<KeyModifiers> MarqueeModifiersProperty =
        AvaloniaProperty.Register<DesignEditorInputGestures, KeyModifiers>(
            nameof(MarqueeModifiers),
            KeyModifiers.None);

    /// <summary>
    /// Идентификатор свойства модификаторов клавиатуры для wheel-zoom.
    /// </summary>
    /// <remarks>Значение по умолчанию: <see cref="KeyModifiers.None"/>.</remarks>
    public static readonly StyledProperty<KeyModifiers> ZoomModifiersProperty =
        AvaloniaProperty.Register<DesignEditorInputGestures, KeyModifiers>(
            nameof(ZoomModifiers),
            KeyModifiers.None);

    /// <summary>
    /// Идентификатор свойства модификаторов, принудительно переключающих взаимодействие на уровень контейнера.
    /// </summary>
    /// <remarks>Значение по умолчанию: <see cref="KeyModifiers.Control"/>.</remarks>
    public static readonly StyledProperty<KeyModifiers> ContainerInteractionModifiersProperty =
        AvaloniaProperty.Register<DesignEditorInputGestures, KeyModifiers>(
            nameof(ContainerInteractionModifiers),
            KeyModifiers.Control);

    /// <summary>
    /// Идентификатор свойства модификаторов additive selection.
    /// </summary>
    /// <remarks>Значение по умолчанию: <see cref="KeyModifiers.Shift"/>.</remarks>
    public static readonly StyledProperty<KeyModifiers> AdditiveSelectionModifiersProperty =
        AvaloniaProperty.Register<DesignEditorInputGestures, KeyModifiers>(
            nameof(AdditiveSelectionModifiers),
            KeyModifiers.Shift);

    /// <summary>
    /// Получает или задает кнопку указателя, запускающую панорамирование.
    /// </summary>
    /// <remarks>По умолчанию используется средняя кнопка мыши.</remarks>
    public DesignEditorPointerButton PanButton
    {
        get => GetValue(PanButtonProperty);
        set => SetValue(PanButtonProperty, value);
    }

    /// <summary>
    /// Получает или задает модификаторы клавиатуры, необходимые для запуска панорамирования.
    /// </summary>
    /// <remarks>
    /// Если установлено <see cref="KeyModifiers.None"/>, панорамирование запускается только по кнопке <see cref="PanButton"/>.
    /// </remarks>
    public KeyModifiers PanModifiers
    {
        get => GetValue(PanModifiersProperty);
        set => SetValue(PanModifiersProperty, value);
    }

    /// <summary>
    /// Получает или задает кнопку указателя, запускающую marquee-selection по пустой области редактора.
    /// </summary>
    /// <remarks>По умолчанию используется левая кнопка мыши.</remarks>
    public DesignEditorPointerButton MarqueeButton
    {
        get => GetValue(MarqueeButtonProperty);
        set => SetValue(MarqueeButtonProperty, value);
    }

    /// <summary>
    /// Получает или задает модификаторы клавиатуры, необходимые для запуска marquee-selection.
    /// </summary>
    /// <remarks>
    /// Если установлено <see cref="KeyModifiers.None"/>, marquee-selection запускается только по кнопке <see cref="MarqueeButton"/>.
    /// </remarks>
    public KeyModifiers MarqueeModifiers
    {
        get => GetValue(MarqueeModifiersProperty);
        set => SetValue(MarqueeModifiersProperty, value);
    }

    /// <summary>
    /// Получает или задает модификаторы клавиатуры, необходимые для обработки wheel-zoom.
    /// </summary>
    /// <remarks>
    /// Если установлено <see cref="KeyModifiers.None"/>, масштабирование колесом доступно без модификаторов.
    /// </remarks>
    public KeyModifiers ZoomModifiers
    {
        get => GetValue(ZoomModifiersProperty);
        set => SetValue(ZoomModifiersProperty, value);
    }

    /// <summary>
    /// Получает или задает модификаторы, которые принудительно переключают selection, drag и resize
    /// на уровень <see cref="DesignEditorItem"/>.
    /// </summary>
    public KeyModifiers ContainerInteractionModifiers
    {
        get => GetValue(ContainerInteractionModifiersProperty);
        set => SetValue(ContainerInteractionModifiersProperty, value);
    }

    /// <summary>
    /// Получает или задает модификаторы, включающие additive selection.
    /// </summary>
    public KeyModifiers AdditiveSelectionModifiers
    {
        get => GetValue(AdditiveSelectionModifiersProperty);
        set => SetValue(AdditiveSelectionModifiersProperty, value);
    }
}
