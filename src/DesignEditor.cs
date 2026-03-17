using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArxisStudio.States;

namespace ArxisStudio;

/// <summary>
/// Представляет поверхность визуального редактора с поддержкой панорамирования,
/// масштабирования, множественного выделения, перетаскивания и изменения размеров элементов.
/// </summary>
/// <remarks>
/// Контрол наследуется от <see cref="SelectingItemsControl"/> и использует
/// <see cref="DesignEditorItem"/> в качестве контейнера для элементов коллекции.
/// <para>
/// Для корректной работы визуальных стилей необходимо подключить словари ресурсов
/// из каталога <c>Themes/Styles</c> библиотеки.
/// </para>
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <design:DesignEditor ItemsSource="{Binding Nodes}"
///                      SelectedItems="{Binding SelectedNodes}"
///                      SelectionMode="Multiple"
///                      ViewportZoom="{Binding Zoom, Mode=TwoWay}" />
/// ]]></code>
/// </example>
public class DesignEditor : SelectingItemsControl
{
    #region Constants
    private const double ZoomFactor = 1.1;
    private const double ZoomTolerance = 0.0001;
    #endregion

    #region State Machine

    private readonly Stack<EditorState> _states = new();

    /// <summary>
    /// Получает текущее активное состояние редактора.
    /// </summary>
    public EditorState CurrentState => _states.Count > 0 ? _states.Peek() : null!;

    /// <summary>
    /// Помещает новое состояние в стек и вызывает его инициализацию.
    /// </summary>
    /// <param name="state">Состояние, которое должно стать активным.</param>
    public void PushState(EditorState state)
    {
        var previous = _states.Count > 0 ? _states.Peek() : null;
        _states.Push(state);
        state.Enter(previous);
    }

    /// <summary>
    /// Завершает текущее состояние и возвращается к предыдущему, если стек содержит более одного состояния.
    /// </summary>
    public void PopState()
    {
        if (_states.Count > 1)
        {
            var current = _states.Pop();
            current.Exit();
        }
    }

    #endregion

    #region Re-exposed Properties

    /// <summary>
    /// Идентификатор свойства модели выделения, повторно экспортированный из базового класса.
    /// </summary>
    public new static readonly DirectProperty<SelectingItemsControl, ISelectionModel> SelectionProperty =
        SelectingItemsControl.SelectionProperty;

    /// <summary>
    /// Идентификатор свойства коллекции выбранных элементов, повторно экспортированный из базового класса.
    /// </summary>
    public new static readonly DirectProperty<SelectingItemsControl, IList?> SelectedItemsProperty =
        SelectingItemsControl.SelectedItemsProperty;

    /// <summary>
    /// Идентификатор свойства режима выделения.
    /// </summary>
    public new static readonly StyledProperty<SelectionMode> SelectionModeProperty =
        SelectingItemsControl.SelectionModeProperty.AddOwner<DesignEditor>();

    /// <summary>
    /// Получает или задает модель выделения редактора.
    /// </summary>
    public new ISelectionModel Selection
    {
        get => base.Selection;
        set => base.Selection = value;
    }

    /// <summary>
    /// Получает или задает внешнюю коллекцию выбранных элементов.
    /// </summary>
    public new IList? SelectedItems
    {
        get => base.SelectedItems;
        set => base.SelectedItems = value;
    }

    /// <summary>
    /// Получает или задает режим выделения элементов.
    /// </summary>
    public new SelectionMode SelectionMode
    {
        get => base.SelectionMode;
        set => base.SelectionMode = value;
    }

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Идентификатор свойства позиции viewport в мировых координатах.
    /// </summary>
    public static readonly StyledProperty<Point> ViewportLocationProperty =
        AvaloniaProperty.Register<DesignEditor, Point>(nameof(ViewportLocation));

    /// <summary>
    /// Идентификатор свойства текущего масштаба viewport.
    /// </summary>
    public static readonly StyledProperty<double> ViewportZoomProperty =
        AvaloniaProperty.Register<DesignEditor, double>(nameof(ViewportZoom), 1.0);

    /// <summary>
    /// Идентификатор свойства минимального допустимого масштаба.
    /// </summary>
    public static readonly StyledProperty<double> MinZoomProperty =
        AvaloniaProperty.Register<DesignEditor, double>(nameof(MinZoom), 0.1);

    /// <summary>
    /// Идентификатор свойства максимального допустимого масштаба.
    /// </summary>
    public static readonly StyledProperty<double> MaxZoomProperty =
        AvaloniaProperty.Register<DesignEditor, double>(nameof(MaxZoom), 5.0);

    /// <summary>
    /// Идентификатор трансформации viewport в логических координатах.
    /// </summary>
    public static readonly StyledProperty<Transform> ViewportTransformProperty =
        AvaloniaProperty.Register<DesignEditor, Transform>(nameof(ViewportTransform), new TransformGroup());

    /// <summary>
    /// Идентификатор трансформации viewport с учетом текущего DPI.
    /// </summary>
    public static readonly StyledProperty<Transform> DpiScaledViewportTransformProperty =
        AvaloniaProperty.Register<DesignEditor, Transform>(nameof(DpiScaledViewportTransform), new TransformGroup());

    /// <summary>
    /// Идентификатор темы для прямоугольника выделения.
    /// </summary>
    public static readonly StyledProperty<ControlTheme> SelectionRectangleStyleProperty =
        AvaloniaProperty.Register<DesignEditor, ControlTheme>(nameof(SelectionRectangleStyle));

    /// <summary>
    /// Идентификатор свойства, показывающего активен ли marquee-selection.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> IsSelectingProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(IsSelecting), o => o.IsSelecting, (o, v) => o.IsSelecting = v);

    /// <summary>
    /// Идентификатор свойства прямоугольника выделения в мировых координатах.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, Rect> SelectedAreaProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, Rect>(nameof(SelectedArea), o => o.SelectedArea, (o, v) => o.SelectedArea = v);

    /// <summary>
    /// Идентификатор свойства прямоугольника, охватывающего все размещенные элементы.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, Rect> ItemsExtentProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, Rect>(nameof(ItemsExtent), o => o.ItemsExtent, (o, v) => o.ItemsExtent = v);

    #endregion

    #region Wrappers

    /// <summary>
    /// Получает или задает положение viewport в мировых координатах.
    /// </summary>
    /// <remarks>
    /// Значение задает левый верхний угол видимой области в координатах содержимого.
    /// Обычно изменяется автоматически во время панорамирования или программно для перехода к нужной области.
    /// </remarks>
    public Point ViewportLocation
    {
        get => GetValue(ViewportLocationProperty);
        set => SetValue(ViewportLocationProperty, value);
    }

    /// <summary>
    /// Получает или задает текущий коэффициент масштабирования viewport.
    /// </summary>
    /// <remarks>
    /// Значение ограничивается диапазоном между <see cref="MinZoom"/> и <see cref="MaxZoom"/>.
    /// </remarks>
    public double ViewportZoom
    {
        get => GetValue(ViewportZoomProperty);
        set => SetValue(ViewportZoomProperty, value);
    }

    /// <summary>
    /// Получает или задает минимальное значение <see cref="ViewportZoom"/>.
    /// </summary>
    public double MinZoom
    {
        get => GetValue(MinZoomProperty);
        set => SetValue(MinZoomProperty, value);
    }

    /// <summary>
    /// Получает или задает максимальное значение <see cref="ViewportZoom"/>.
    /// </summary>
    public double MaxZoom
    {
        get => GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }

    /// <summary>
    /// Получает или задает трансформацию, применяемую к содержимому viewport.
    /// </summary>
    public Transform ViewportTransform
    {
        get => GetValue(ViewportTransformProperty);
        set => SetValue(ViewportTransformProperty, value);
    }

    /// <summary>
    /// Получает или задает DPI-aware трансформацию viewport.
    /// </summary>
    public Transform DpiScaledViewportTransform
    {
        get => GetValue(DpiScaledViewportTransformProperty);
        set => SetValue(DpiScaledViewportTransformProperty, value);
    }

    /// <summary>
    /// Получает или задает тему визуализации рамки выделения.
    /// </summary>
    public ControlTheme SelectionRectangleStyle
    {
        get => GetValue(SelectionRectangleStyleProperty);
        set => SetValue(SelectionRectangleStyleProperty, value);
    }

    private bool _isSelecting;
    /// <summary>
    /// Получает или задает признак активного прямоугольного выделения.
    /// </summary>
    public bool IsSelecting
    {
        get => _isSelecting;
        set => SetAndRaise(IsSelectingProperty, ref _isSelecting, value);
    }

    private Rect _selectedArea;
    /// <summary>
    /// Получает или задает текущий прямоугольник выделения в мировых координатах.
    /// </summary>
    public Rect SelectedArea
    {
        get => _selectedArea;
        set => SetAndRaise(SelectedAreaProperty, ref _selectedArea, value);
    }

    private Rect _itemsExtent;
    /// <summary>
    /// Получает или задает прямоугольник, охватывающий все дочерние элементы редактора.
    /// </summary>
    public Rect ItemsExtent
    {
        get => _itemsExtent;
        set => SetAndRaise(ItemsExtentProperty, ref _itemsExtent, value);
    }

    #endregion

    #region Internal Helpers

    private Point _lastMousePosition;
    internal KeyModifiers LastInputModifiers { get; private set; }

    private readonly TranslateTransform _translateTransform = new TranslateTransform();
    private readonly ScaleTransform _scaleTransform = new ScaleTransform();
    private readonly TranslateTransform _dpiTranslateTransform = new TranslateTransform();
    #endregion

    static DesignEditor()
    {
        FocusableProperty.OverrideDefaultValue<DesignEditor>(true);
        ViewportLocationProperty.Changed.AddClassHandler<DesignEditor>((x, e) => x.UpdateTransforms());
        ViewportZoomProperty.Changed.AddClassHandler<DesignEditor>((x, e) => x.UpdateTransforms());

        DesignEditorItem.DragStartedEvent.AddClassHandler<DesignEditor>((x, e) => x.OnItemsDragStarted(e));
        DesignEditorItem.DragDeltaEvent.AddClassHandler<DesignEditor>((x, e) => x.OnItemsDragDelta(e));
        DesignEditorItem.DragCompletedEvent.AddClassHandler<DesignEditor>((x, e) => x.OnItemsDragCompleted(e));
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditor"/>.
    /// </summary>
    public DesignEditor()
    {
        SelectionMode = SelectionMode.Multiple;

        var contentGroup = new TransformGroup();
        contentGroup.Children.Add(_scaleTransform);
        contentGroup.Children.Add(_translateTransform);
        SetCurrentValue(ViewportTransformProperty, contentGroup);

        var dpiGroup = new TransformGroup();
        dpiGroup.Children.Add(_scaleTransform);
        dpiGroup.Children.Add(_dpiTranslateTransform);
        SetCurrentValue(DpiScaledViewportTransformProperty, dpiGroup);

        _states.Push(new EditorIdleState(this));
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        return NeedsContainer<DesignEditorItem>(item, out recycleKey);
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return new DesignEditorItem();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (e.Root is TopLevel topLevel) topLevel.ScalingChanged += OnScreenScalingChanged;
        UpdateTransforms();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (e.Root is TopLevel topLevel) topLevel.ScalingChanged -= OnScreenScalingChanged;
    }

    private void OnScreenScalingChanged(object? sender, EventArgs e) => UpdateTransforms();

    private void UpdateTransforms()
    {
        _scaleTransform.ScaleX = ViewportZoom;
        _scaleTransform.ScaleY = ViewportZoom;

        double x = -ViewportLocation.X * ViewportZoom;
        double y = -ViewportLocation.Y * ViewportZoom;

        _translateTransform.X = x;
        _translateTransform.Y = y;

        var root = this.GetVisualRoot();
        double renderScaling = root?.RenderScaling ?? 1.0;

        _dpiTranslateTransform.X = Math.Round(x * renderScaling) / renderScaling;
        _dpiTranslateTransform.Y = Math.Round(y * renderScaling) / renderScaling;

        var vg = new TransformGroup(); vg.Children.Add(_scaleTransform); vg.Children.Add(_translateTransform);
        SetCurrentValue(ViewportTransformProperty, vg);

        var dg = new TransformGroup(); dg.Children.Add(_scaleTransform); dg.Children.Add(_dpiTranslateTransform);
        SetCurrentValue(DpiScaledViewportTransformProperty, dg);
    }

    /// <summary>
    /// Преобразует экранную точку в мировые координаты холста.
    /// </summary>
    /// <param name="screenPoint">Точка в координатах контрола.</param>
    /// <returns>Точка в координатах содержимого редактора.</returns>
    /// <example>
    /// Это полезно, когда нужно разместить новый элемент в позиции курсора с учетом текущего зума и панорамирования.
    /// </example>
    public Point GetWorldPosition(Point screenPoint)
        => (screenPoint / ViewportZoom) + ViewportLocation;

    /// <summary>
    /// Возвращает последнюю известную позицию указателя для текущего ввода.
    /// </summary>
    /// <param name="relativeTo">Параметр сохранен для совместимости с будущими реализациями.</param>
    /// <returns>Последняя позиция указателя в координатах редактора.</returns>
    public Point GetPositionForInput(Visual relativeTo)
        => _lastMousePosition;

    /// <summary>
    /// Выполняет масштабирование относительно текущей позиции курсора.
    /// </summary>
    /// <param name="e">Аргументы колесика мыши.</param>
    public void HandleZoom(PointerWheelEventArgs e)
    {
        double prevZoom = ViewportZoom;
        double newZoom = e.Delta.Y > 0 ? prevZoom * ZoomFactor : prevZoom / ZoomFactor;
        newZoom = Math.Max(GetValue(MinZoomProperty), Math.Min(GetValue(MaxZoomProperty), newZoom));

        if (Math.Abs(newZoom - prevZoom) > ZoomTolerance)
        {
            Point mousePos = e.GetPosition(this);
            Vector correction = (Vector)mousePos / prevZoom - (Vector)mousePos / newZoom;
            ViewportZoom = newZoom;
            ViewportLocation += correction;
        }
    }

    internal void CommitSelection(Rect bounds, bool isCtrlPressed)
    {
        if (Presenter?.Panel == null) return;

        using (Selection.BatchUpdate())
        {
            if (!isCtrlPressed) Selection.Clear();

            foreach (var child in Presenter.Panel.Children)
            {
                if (child is DesignEditorItem container)
                {
                    if (bounds.Intersects(new Rect(container.Location, container.Bounds.Size)))
                        Selection.Select(IndexFromContainer(container));
                }
            }
        }
    }

    // --- Input Handling ---

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _lastMousePosition = e.GetPosition(this);
        LastInputModifiers = e.KeyModifiers;

        CurrentState.OnPointerPressed(e);

        if (!e.Handled) base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        _lastMousePosition = e.GetPosition(this);
        CurrentState.OnPointerMoved(e);
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        CurrentState.OnPointerReleased(e);
        base.OnPointerReleased(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.Handled) return;
        CurrentState.OnPointerWheelChanged(e);
        e.Handled = true;
    }

    // --- Drag & Drop ---

    private void OnItemsDragStarted(DragStartedEventArgs e) => e.Handled = true;

    private void OnItemsDragDelta(DragDeltaEventArgs e)
    {
        if (IsSelecting || CurrentState is EditorPanningState) return;

        var items = SelectedItems;
        if (items == null || items.Count == 0) return;

        var delta = new Vector(e.HorizontalChange, e.VerticalChange);
        var sourceContainer = e.Source as DesignEditorItem;

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container != null && container.IsDraggable && !ReferenceEquals(container, sourceContainer))
            {
                container.Location += delta;
            }
        }
        e.Handled = true;
    }

    private void OnItemsDragCompleted(DragCompletedEventArgs e) => e.Handled = true;
}
