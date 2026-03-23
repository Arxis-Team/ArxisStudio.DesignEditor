using Avalonia;

namespace ArxisStudio;

/// <summary>
/// Представляет runtime-настройки взаимодействия редактора, не относящиеся к жестам ввода.
/// </summary>
/// <remarks>
/// Этот объект задает числовые параметры поведения редактора (масштабирование, пороги и ограничения).
/// Кнопки мыши и модификаторы клавиатуры настраиваются отдельно через <see cref="DesignEditorInputGestures"/>.
/// </remarks>
public class DesignEditorInteractionOptions : AvaloniaObject
{
    /// <summary>
    /// Идентификатор свойства коэффициента шага масштабирования колесом мыши.
    /// </summary>
    /// <remarks>
    /// Значение должно быть больше <c>1.0</c>.
    /// По умолчанию: <c>1.1</c>.
    /// </remarks>
    public static readonly StyledProperty<double> ZoomStepProperty =
        AvaloniaProperty.Register<DesignEditorInteractionOptions, double>(
            nameof(ZoomStep),
            1.1);

    /// <summary>
    /// Идентификатор свойства порога старта перетаскивания в пикселях.
    /// </summary>
    /// <remarks>По умолчанию: <c>3.0</c>.</remarks>
    public static readonly StyledProperty<double> DragStartThresholdProperty =
        AvaloniaProperty.Register<DesignEditorInteractionOptions, double>(
            nameof(DragStartThreshold),
            3.0);

    /// <summary>
    /// Идентификатор свойства минимального размера элемента при resize.
    /// </summary>
    /// <remarks>По умолчанию: <c>10.0</c>.</remarks>
    public static readonly StyledProperty<double> ResizeMinSizeProperty =
        AvaloniaProperty.Register<DesignEditorInteractionOptions, double>(
            nameof(ResizeMinSize),
            10.0);

    /// <summary>
    /// Получает или задает коэффициент шага масштабирования колесом мыши.
    /// </summary>
    public double ZoomStep
    {
        get => GetValue(ZoomStepProperty);
        set => SetValue(ZoomStepProperty, value);
    }

    /// <summary>
    /// Получает или задает порог старта перетаскивания в пикселях.
    /// </summary>
    public double DragStartThreshold
    {
        get => GetValue(DragStartThresholdProperty);
        set => SetValue(DragStartThresholdProperty, value);
    }

    /// <summary>
    /// Получает или задает минимальный размер элемента при resize.
    /// </summary>
    public double ResizeMinSize
    {
        get => GetValue(ResizeMinSizeProperty);
        set => SetValue(ResizeMinSizeProperty, value);
    }
}
