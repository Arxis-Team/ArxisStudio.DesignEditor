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
using DesignLayout = ArxisStudio.Attached.Layout;
using ArxisStudio.Controls;
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
    private const double FitToViewPadding = 32.0;
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
    /// Идентификатор темы рамки одиночного выделения.
    /// </summary>
    public static readonly StyledProperty<ControlTheme> SelectionOutlineStyleProperty =
        AvaloniaProperty.Register<DesignEditor, ControlTheme>(nameof(SelectionOutlineStyle));

    /// <summary>
    /// Идентификатор темы рамки группового выделения.
    /// </summary>
    public static readonly StyledProperty<ControlTheme> GroupSelectionOutlineStyleProperty =
        AvaloniaProperty.Register<DesignEditor, ControlTheme>(nameof(GroupSelectionOutlineStyle));

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

    /// <summary>
    /// Идентификатор свойства прямоугольника, охватывающего текущее выделение.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, Rect> SelectionBoundsProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, Rect>(nameof(SelectionBounds), o => o.SelectionBounds, (o, v) => o.SelectionBounds = v);

    /// <summary>
    /// Идентификатор свойства, указывающего наличие ровно одного выбранного элемента.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> HasSingleSelectionProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(HasSingleSelection), o => o.HasSingleSelection, (o, v) => o.HasSingleSelection = v);

    /// <summary>
    /// Идентификатор свойства, указывающего наличие множественного выделения.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> HasMultipleSelectionProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(HasMultipleSelection), o => o.HasMultipleSelection, (o, v) => o.HasMultipleSelection = v);

    /// <summary>
    /// Идентификатор свойства описания текущего primary selection target.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, string?> PrimarySelectionTargetDescriptionProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, string?>(nameof(PrimarySelectionTargetDescription), o => o.PrimarySelectionTargetDescription);

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

    /// <summary>
    /// Получает или задает тему рамки одиночного выделения.
    /// </summary>
    public ControlTheme SelectionOutlineStyle
    {
        get => GetValue(SelectionOutlineStyleProperty);
        set => SetValue(SelectionOutlineStyleProperty, value);
    }

    /// <summary>
    /// Получает или задает тему рамки группового выделения.
    /// </summary>
    public ControlTheme GroupSelectionOutlineStyle
    {
        get => GetValue(GroupSelectionOutlineStyleProperty);
        set => SetValue(GroupSelectionOutlineStyleProperty, value);
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

    private Rect _selectionBounds;
    /// <summary>
    /// Получает или задает прямоугольник, охватывающий текущее выделение.
    /// </summary>
    public Rect SelectionBounds
    {
        get => _selectionBounds;
        private set => SetAndRaise(SelectionBoundsProperty, ref _selectionBounds, value);
    }

    private bool _hasSingleSelection;
    /// <summary>
    /// Получает значение, указывающее, что в редакторе выбран ровно один элемент.
    /// </summary>
    public bool HasSingleSelection
    {
        get => _hasSingleSelection;
        private set => SetAndRaise(HasSingleSelectionProperty, ref _hasSingleSelection, value);
    }

    private bool _hasMultipleSelection;
    /// <summary>
    /// Получает значение, указывающее, что в редакторе выбрано более одного элемента.
    /// </summary>
    public bool HasMultipleSelection
    {
        get => _hasMultipleSelection;
        private set => SetAndRaise(HasMultipleSelectionProperty, ref _hasMultipleSelection, value);
    }

    private string? _primarySelectionTargetDescription;
    /// <summary>
    /// Получает описание текущего primary selection target для отладки и диагностического UI.
    /// </summary>
    public string? PrimarySelectionTargetDescription
    {
        get => _primarySelectionTargetDescription;
        private set => SetAndRaise(PrimarySelectionTargetDescriptionProperty, ref _primarySelectionTargetDescription, value);
    }

    #endregion

    #region Internal Helpers

    private Point _lastMousePosition;
    internal KeyModifiers LastInputModifiers { get; private set; }

    private ResizeAdorner? _selectionResizeAdorner;
    private DesignEditorItem? _primarySelectionItem;
    private Control? _primarySelectionControl;
    private readonly Dictionary<DesignEditorItem, Control> _selectionTargets = new();

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
        DesignEditorItem.ResizeDeltaEvent.AddClassHandler<DesignEditor>((x, e) => x.OnItemsResizeDelta(e));
        DesignEditorItem.IsSelectedProperty.Changed.AddClassHandler<DesignEditorItem>((item, _) =>
        {
            if (item.FindAncestorOfType<DesignEditor>() is { } editor)
                editor.UpdateSelectionOverlayState();
        });
        DesignEditorItem.LocationProperty.Changed.AddClassHandler<DesignEditorItem>((item, _) =>
        {
            if (item.IsSelected && item.FindAncestorOfType<DesignEditor>() is { } editor)
                editor.UpdateSelectionOverlayState();
        });
        DesignLayout.DesignXProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (control.FindAncestorOfType<DesignEditor>() is { } editor && editor.IsSelectionOverlayControl(control))
                editor.UpdateSelectionOverlayState();
        });
        DesignLayout.DesignYProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (control.FindAncestorOfType<DesignEditor>() is { } editor && editor.IsSelectionOverlayControl(control))
                editor.UpdateSelectionOverlayState();
        });
        BoundsProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (control.FindAncestorOfType<DesignEditor>() is { } editor && editor.IsSelectionOverlayControl(control))
                editor.UpdateSelectionOverlayState();
        });
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
        UpdateSelectionOverlayState();
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        return NeedsContainer<DesignEditorItem>(item, out recycleKey);
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return new DesignEditorItem();
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_selectionResizeAdorner != null)
        {
            _selectionResizeAdorner.ResizeStarted -= OnSelectionResizeStarted;
            _selectionResizeAdorner.ResizeDelta -= OnSelectionResizeDelta;
            _selectionResizeAdorner.ResizeCompleted -= OnSelectionResizeCompleted;
        }

        _selectionResizeAdorner = e.NameScope.Find<ResizeAdorner>("PART_SelectionResizer");

        if (_selectionResizeAdorner != null)
        {
            _selectionResizeAdorner.ResizeStarted += OnSelectionResizeStarted;
            _selectionResizeAdorner.ResizeDelta += OnSelectionResizeDelta;
            _selectionResizeAdorner.ResizeCompleted += OnSelectionResizeCompleted;
        }
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

    /// <summary>
    /// Смещает viewport так, чтобы указанная мировая точка оказалась в центре видимой области редактора.
    /// </summary>
    /// <param name="worldPoint">Точка в координатах содержимого редактора.</param>
    /// <remarks>
    /// Метод не изменяет <see cref="ViewportZoom"/> и пересчитывает только <see cref="ViewportLocation"/>.
    /// </remarks>
    public void CenterOn(Point worldPoint)
    {
        var visibleWorldSize = new Size(Bounds.Width / ViewportZoom, Bounds.Height / ViewportZoom);
        ViewportLocation = new Point(
            worldPoint.X - (visibleWorldSize.Width / 2),
            worldPoint.Y - (visibleWorldSize.Height / 2));
    }

    /// <summary>
    /// Смещает viewport так, чтобы центр указанной области оказался в центре видимой области редактора.
    /// </summary>
    /// <param name="bounds">Прямоугольная область в мировых координатах.</param>
    /// <remarks>
    /// Метод не изменяет <see cref="ViewportZoom"/> и использует геометрический центр переданного прямоугольника.
    /// </remarks>
    public void CenterOn(Rect bounds)
    {
        CenterOn(bounds.Center);
    }

    /// <summary>
    /// Смещает viewport так, чтобы указанный элемент оказался в центре видимой области редактора.
    /// </summary>
    /// <param name="item">Элемент, который необходимо центрировать в области просмотра.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="item"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Выбрасывается, если <paramref name="item"/> не принадлежит текущему экземпляру <see cref="DesignEditor"/>.
    /// </exception>
    /// <remarks>
    /// Метод изменяет только <see cref="ViewportLocation"/> и не изменяет <see cref="ViewportZoom"/>.
    /// <para>
    /// Если размер элемента превышает размер видимой области, элемент не масштабируется и не вписывается целиком:
    /// в центр видимой области помещается только геометрический центр элемента.
    /// </para>
    /// <para>
    /// Метод использует текущие <see cref="DesignEditorItem.Location"/> и <see cref="Visual.Bounds"/> элемента.
    /// Для корректного результата элемент должен принадлежать текущему редактору и иметь актуальный layout.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.CenterOnItem(container);
    /// ]]></code>
    /// </example>
    public void CenterOnItem(DesignEditorItem item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        if (!ReferenceEquals(item.FindAncestorOfType<DesignEditor>(), this))
            throw new InvalidOperationException("The specified item does not belong to this DesignEditor.");

        if (TryGetDesignBounds(item, out var bounds))
        {
            CenterOn(bounds.Center);
            return;
        }

        var fallbackCenter = new Point(
            item.Location.X + (item.Bounds.Width / 2),
            item.Location.Y + (item.Bounds.Height / 2));

        CenterOn(fallbackCenter);
    }

    /// <summary>
    /// Изменяет положение и масштаб viewport так, чтобы указанная область целиком поместилась в видимой области редактора.
    /// </summary>
    /// <param name="bounds">Прямоугольная область в мировых координатах, которую необходимо вписать в окно.</param>
    /// <remarks>
    /// Метод изменяет <see cref="ViewportLocation"/> и <see cref="ViewportZoom"/>.
    /// <para>
    /// Для более аккуратного отображения вокруг области добавляется внутренний отступ.
    /// Итоговый масштаб ограничивается значениями <see cref="MinZoom"/> и <see cref="MaxZoom"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.FitToView(new Rect(100, 100, 640, 360));
    /// ]]></code>
    /// </example>
    public void FitToView(Rect bounds)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var paddedBounds = bounds.Inflate(FitToViewPadding);
        var targetWidth = Math.Max(1.0, paddedBounds.Width);
        var targetHeight = Math.Max(1.0, paddedBounds.Height);

        var zoomX = Bounds.Width / targetWidth;
        var zoomY = Bounds.Height / targetHeight;
        var newZoom = Math.Min(zoomX, zoomY);
        newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));

        ViewportZoom = newZoom;
        CenterOn(paddedBounds.Center);
    }

    /// <summary>
    /// Изменяет положение и масштаб viewport так, чтобы указанный элемент целиком поместился в видимой области редактора.
    /// </summary>
    /// <param name="item">Элемент, который необходимо вписать в окно редактора.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="item"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Выбрасывается, если <paramref name="item"/> не принадлежит текущему экземпляру <see cref="DesignEditor"/>.
    /// </exception>
    /// <remarks>
    /// Метод использует текущие <see cref="DesignEditorItem.Location"/> и <see cref="Visual.Bounds"/> элемента
    /// и делегирует расчет геометрии перегрузке <see cref="FitToView(Rect)"/>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.FitToView(container);
    /// ]]></code>
    /// </example>
    public void FitToView(DesignEditorItem item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        if (!ReferenceEquals(item.FindAncestorOfType<DesignEditor>(), this))
            throw new InvalidOperationException("The specified item does not belong to this DesignEditor.");

        if (TryGetDesignBounds(item, out var bounds))
        {
            FitToView(bounds);
            return;
        }

        FitToView(new Rect(item.Location, item.Bounds.Size));
    }

    /// <summary>
    /// Смещает viewport так, чтобы центр области, охватывающей все выбранные элементы, оказался в центре видимой области редактора.
    /// </summary>
    /// <remarks>
    /// Метод не изменяет <see cref="ViewportZoom"/>. Если в редакторе нет выбранных элементов, вызов игнорируется.
    /// </remarks>
    public void CenterOnSelection()
    {
        if (TryGetSelectedDesignBounds(out var bounds, out _, out _, out _))
            CenterOn(bounds);
    }

    /// <summary>
    /// Изменяет положение и масштаб viewport так, чтобы все выбранные элементы целиком поместились в видимой области редактора.
    /// </summary>
    /// <remarks>
    /// Если в редакторе нет выбранных элементов, вызов игнорируется.
    /// </remarks>
    public void FitSelectionToView()
    {
        if (TryGetSelectedDesignBounds(out var bounds, out _, out _, out _))
            FitToView(bounds);
    }

    private bool TryGetSelectedDesignBounds(
        out Rect bounds,
        out int selectedCount,
        out DesignEditorItem? primaryItem,
        out Control? primaryControl)
    {
        bounds = default;
        selectedCount = 0;
        primaryItem = null;
        primaryControl = null;

        var items = SelectedItems;
        if (items == null || items.Count == 0)
            return false;

        var hasBounds = false;
        double left = 0;
        double top = 0;
        double right = 0;
        double bottom = 0;

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null)
                continue;

            var selectionTarget = ResolveSelectionTarget(container);

            if (!TryGetDesignBounds(selectionTarget, out var itemBounds))
                continue;

            selectedCount++;
            primaryItem ??= container;
            primaryControl ??= selectionTarget;

            if (!hasBounds)
            {
                left = itemBounds.Left;
                top = itemBounds.Top;
                right = itemBounds.Right;
                bottom = itemBounds.Bottom;
                hasBounds = true;
                continue;
            }

            left = Math.Min(left, itemBounds.Left);
            top = Math.Min(top, itemBounds.Top);
            right = Math.Max(right, itemBounds.Right);
            bottom = Math.Max(bottom, itemBounds.Bottom);
        }

        if (!hasBounds)
            return false;

        bounds = new Rect(left, top, right - left, bottom - top);
        return true;
    }

    internal void UpdateSelectionOverlayState()
    {
        if (TryGetSelectedDesignBounds(out var bounds, out var selectedCount, out var primaryItem, out var primaryControl))
        {
            CleanupSelectionTargets();
            SelectionBounds = bounds;
            HasSingleSelection = selectedCount == 1;
            HasMultipleSelection = selectedCount > 1;
            _primarySelectionItem = primaryItem;
            _primarySelectionControl = primaryControl;
            PrimarySelectionTargetDescription = DescribeSelectionTarget(primaryControl);
            return;
        }

        _selectionTargets.Clear();
        SelectionBounds = default;
        HasSingleSelection = false;
        HasMultipleSelection = false;
        _primarySelectionItem = null;
        _primarySelectionControl = null;
        PrimarySelectionTargetDescription = null;
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
                    if (TryGetDesignBounds(container, out var itemBounds) && bounds.Intersects(itemBounds))
                        Selection.Select(IndexFromContainer(container));
                }
            }
        }

        UpdateSelectionOverlayState();
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
        UpdateSelectionOverlayState();
    }

    private void OnItemsDragCompleted(DragCompletedEventArgs e) => e.Handled = true;

    private void OnItemsResizeDelta(ResizeDeltaEventArgs e)
    {
        UpdateSelectionOverlayState();
        e.Handled = false;
    }

    private void OnSelectionResizeStarted(object? sender, ResizeStartedEventArgs e)
    {
        if (_primarySelectionItem == null || _primarySelectionControl == null || !HasSingleSelection)
            return;

        _primarySelectionItem.PushState(new ItemResizingState(_primarySelectionItem, e.Direction));
        _primarySelectionItem.OnResizeStarted(e.Vector);
        e.Handled = true;
    }

    private void OnSelectionResizeDelta(object? sender, ResizeDeltaEventArgs e)
    {
        if (_primarySelectionItem == null || _primarySelectionControl == null || _primarySelectionItem.CurrentState is not ItemResizingState)
            return;

        _primarySelectionItem.CurrentState.OnResizeDelta(e);
        _primarySelectionItem.OnResizeDelta(new ResizeDeltaEventArgs(e.Delta, e.Direction, DesignEditorItem.ResizeDeltaEvent));
        UpdateSelectionOverlayState();
        e.Handled = true;
    }

    private void OnSelectionResizeCompleted(object? sender, VectorEventArgs e)
    {
        if (_primarySelectionItem == null || _primarySelectionControl == null || _primarySelectionItem.CurrentState is not ItemResizingState)
            return;

        _primarySelectionItem.PopState();
        _primarySelectionItem.OnResizeCompleted(e.Vector);
        UpdateSelectionOverlayState();
        e.Handled = true;
    }

    internal void UpdateSelectionTargetFromSource(DesignEditorItem container, Visual? source)
    {
        var target = ResolveSelectionTarget(container, source);

        if (ReferenceEquals(target, container))
            _selectionTargets.Remove(container);
        else
            _selectionTargets[container] = target;

        UpdateSelectionOverlayState();
    }

    internal void UpdateSelectionTargetFromPoint(DesignEditorItem container, Point screenPoint)
    {
        var worldPoint = GetWorldPosition(screenPoint);
        var target = ResolveSelectionTargetAtPoint(container, worldPoint);

        if (ReferenceEquals(target, container))
            _selectionTargets.Remove(container);
        else
            _selectionTargets[container] = target;

        UpdateSelectionOverlayState();
    }

    private bool TryGetDesignBounds(DesignEditorItem item, out Rect bounds)
    {
        if (item == null)
        {
            bounds = default;
            return false;
        }

        return TryGetDesignBounds(ResolveSelectionTarget(item), out bounds);
    }

    private bool TryGetDesignBounds(Control control, out Rect bounds)
    {
        if (!ReferenceEquals(control.FindAncestorOfType<DesignEditor>(), this))
        {
            bounds = default;
            return false;
        }

        if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
        {
            bounds = default;
            return false;
        }

        EnsureTracked(control);

        Visual? reference = control.FindAncestorOfType<DesignSurface>()
                            ?? control.FindAncestorOfType<DesignEditor>() as Visual;

        var position = reference != null
            ? control.TranslatePoint(new Point(0, 0), reference)
            : null;

        double x;
        double y;

        if (position.HasValue)
        {
            x = position.Value.X;
            y = position.Value.Y;
        }
        else
        {
            x = DesignLayout.GetDesignX(control);
            y = DesignLayout.GetDesignY(control);

            if (double.IsNaN(x) || double.IsNaN(y))
            {
                bounds = default;
                return false;
            }
        }

        bounds = new Rect(new Point(x, y), control.Bounds.Size);
        return true;
    }

    private static Control ResolveSelectionTarget(DesignEditorItem item)
    {
        if (item.FindAncestorOfType<DesignEditor>() is { } editor &&
            editor._selectionTargets.TryGetValue(item, out var explicitTarget) &&
            IsOwnedByContainer(explicitTarget, item))
        {
            return explicitTarget;
        }

        foreach (var control in EnumerateSelectionCandidates(item))
        {
            if (HasDesignerLayoutMetadata(control))
                return control;
        }

        return item;
    }

    private static Control ResolveSelectionTarget(DesignEditorItem item, Visual? source)
    {
        var current = source;
        while (current != null && !ReferenceEquals(current, item))
        {
            if (current is Control control &&
                IsOwnedByContainer(control, item) &&
                HasDesignerLayoutMetadata(control))
            {
                return control;
            }

            current = current.GetVisualParent();
        }

        return ResolveSelectionTarget(item);
    }

    private Control ResolveSelectionTargetAtPoint(DesignEditorItem item, Point worldPoint)
    {
        var fallback = ResolveSelectionTarget(item);
        Control? bestMatch = null;
        Rect bestBounds = default;
        var bestDepth = -1;

        foreach (var control in EnumerateSelectionCandidates(item))
        {
            if (!HasDesignerLayoutMetadata(control))
                continue;

            if (!TryGetDesignBounds(control, out var bounds) || !bounds.Contains(worldPoint))
                continue;

            var depth = GetVisualDepth(control, item);
            if (bestMatch == null ||
                depth > bestDepth ||
                (depth == bestDepth && bounds.Width * bounds.Height < bestBounds.Width * bestBounds.Height))
            {
                bestMatch = control;
                bestBounds = bounds;
                bestDepth = depth;
            }
        }

        return bestMatch ?? fallback;
    }

    private static Control ResolveSelectionTarget(Control root)
    {
        if (HasDesignerLayoutMetadata(root))
            return root;

        foreach (var descendant in root.GetVisualDescendants())
        {
            if (descendant is Control control && HasDesignerLayoutMetadata(control))
                return control;
        }

        return root;
    }

    private static bool HasDesignerLayoutMetadata(Control control)
    {
        return DesignLayout.GetIsTracked(control)
            || !double.IsNaN(DesignLayout.GetX(control))
            || !double.IsNaN(DesignLayout.GetY(control));
    }

    private static void EnsureTracked(Control control)
    {
        if (!DesignLayout.GetIsTracked(control))
            DesignLayout.Track(control);
    }

    private static int GetVisualDepth(Control control, Visual root)
    {
        var depth = 0;
        var current = control as Visual;
        while (current != null && !ReferenceEquals(current, root))
        {
            depth++;
            current = current.GetVisualParent();
        }

        return depth;
    }

    private bool IsSelectionOverlayControl(Control control)
    {
        if (_primarySelectionControl != null && ReferenceEquals(_primarySelectionControl, control))
            return true;

        var items = SelectedItems;
        if (items == null || items.Count == 0)
            return false;

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container != null && ReferenceEquals(ResolveSelectionTarget(container), control))
                return true;
        }

        return false;
    }

    private void CleanupSelectionTargets()
    {
        if (_selectionTargets.Count == 0)
            return;

        var selectedContainers = new HashSet<DesignEditorItem>();
        var items = SelectedItems;
        if (items != null)
        {
            foreach (var item in items)
            {
                var container = ContainerFromItem(item) as DesignEditorItem;
                if (container == null && item is DesignEditorItem directItem)
                    container = directItem;

                if (container != null)
                    selectedContainers.Add(container);
            }
        }

        var staleContainers = new List<DesignEditorItem>();
        foreach (var pair in _selectionTargets)
        {
            if (!selectedContainers.Contains(pair.Key) ||
                !IsOwnedByContainer(pair.Value, pair.Key))
            {
                staleContainers.Add(pair.Key);
            }
        }

        foreach (var container in staleContainers)
            _selectionTargets.Remove(container);
    }

    private static string DescribeSelectionTarget(Control? control)
    {
        if (control == null)
            return string.Empty;

        var typeName = control.GetType().Name;
        return !string.IsNullOrWhiteSpace(control.Name)
            ? $"{typeName} ({control.Name})"
            : typeName;
    }

    private static bool IsOwnedByContainer(Visual visual, DesignEditorItem container)
    {
        var current = visual;
        while (current != null)
        {
            if (ReferenceEquals(current, container))
                return true;

            current = current.GetVisualParent();
        }

        return false;
    }

    private static IEnumerable<Control> EnumerateSelectionCandidates(DesignEditorItem item)
    {
        foreach (var descendant in item.GetVisualDescendants())
        {
            if (descendant is Control control &&
                !ReferenceEquals(control, item) &&
                IsOwnedByContainer(control, item))
            {
                yield return control;
            }
        }
    }
}
