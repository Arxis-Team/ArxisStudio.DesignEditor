using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Mixins;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ArxisStudio.States;
using ArxisStudio.Controls;
using ArxisStudio.Attached;

namespace ArxisStudio;

[TemplatePart("PART_Border", typeof(Border))]
[TemplatePart("PART_Resizer", typeof(ResizeAdorner))]
[PseudoClasses(":selected", ":dragging", ":resizing")]
public class DesignEditorItem : ContentControl, ISelectable, IDesignEditorItem
{
    #region Fields
    private ResizeAdorner? _resizeAdorner;
    private readonly Stack<DesignEditorItemState> _states = new();
    private bool _isUpdatingLocation;
    #endregion

    #region Standard Properties

    public static readonly StyledProperty<bool> IsSelectedProperty =
        SelectingItemsControl.IsSelectedProperty.AddOwner<DesignEditorItem>();

    public bool IsSelected
    {
        get => GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }

    public static readonly StyledProperty<Point> LocationProperty =
        AvaloniaProperty.Register<DesignEditorItem, Point>(nameof(Location));

    public Point Location
    {
        get => GetValue(LocationProperty);
        set => SetValue(LocationProperty, value);
    }

    public static readonly StyledProperty<bool> IsDraggableProperty =
        AvaloniaProperty.Register<DesignEditorItem, bool>(nameof(IsDraggable), true);

    public bool IsDraggable
    {
        get => GetValue(IsDraggableProperty);
        set => SetValue(IsDraggableProperty, value);
    }

    #endregion

    #region Visual Properties

    public static readonly StyledProperty<IBrush> SelectedBrushProperty =
        AvaloniaProperty.Register<DesignEditorItem, IBrush>(nameof(SelectedBrush), Brushes.Orange);

    public IBrush SelectedBrush
    {
        get => GetValue(SelectedBrushProperty);
        set => SetValue(SelectedBrushProperty, value);
    }

    public static readonly StyledProperty<Thickness> SelectedBorderThicknessProperty =
        AvaloniaProperty.Register<DesignEditorItem, Thickness>(nameof(SelectedBorderThickness), new Thickness(2));

    public Thickness SelectedBorderThickness
    {
        get => GetValue(SelectedBorderThicknessProperty);
        set => SetValue(SelectedBorderThicknessProperty, value);
    }

    public static readonly DirectProperty<DesignEditorItem, Thickness> SelectedMarginProperty =
        AvaloniaProperty.RegisterDirect<DesignEditorItem, Thickness>(
            nameof(SelectedMargin),
            o => o.SelectedMargin);

    /// <summary>
    /// Возвращает отрицательный отступ.
    /// Это критически важно: когда BorderThickness увеличивается, контент сжимается внутрь.
    /// Отрицательный Margin смещает весь контейнер влево/вверх, компенсируя это сжатие визуально.
    /// </summary>
    public Thickness SelectedMargin => new Thickness(
        -SelectedBorderThickness.Left,
        -SelectedBorderThickness.Top,
        -SelectedBorderThickness.Right,
        -SelectedBorderThickness.Bottom);

    #endregion

    #region Routed Events

    public static readonly RoutedEvent<DragStartedEventArgs> DragStartedEvent =
        RoutedEvent.Register<DragStartedEventArgs>(nameof(DragStarted), RoutingStrategies.Bubble, typeof(DesignEditorItem));
    public static readonly RoutedEvent<DragDeltaEventArgs> DragDeltaEvent =
        RoutedEvent.Register<DragDeltaEventArgs>(nameof(DragDelta), RoutingStrategies.Bubble, typeof(DesignEditorItem));
    public static readonly RoutedEvent<DragCompletedEventArgs> DragCompletedEvent =
        RoutedEvent.Register<DragCompletedEventArgs>(nameof(DragCompleted), RoutingStrategies.Bubble, typeof(DesignEditorItem));

    public static readonly RoutedEvent<ResizeDeltaEventArgs> ResizeDeltaEvent =
        RoutedEvent.Register<ResizeDeltaEventArgs>(nameof(ResizeDelta), RoutingStrategies.Bubble, typeof(DesignEditorItem));
    public static readonly RoutedEvent<VectorEventArgs> ResizeStartedEvent =
        RoutedEvent.Register<VectorEventArgs>(nameof(ResizeStarted), RoutingStrategies.Bubble, typeof(DesignEditorItem));
    public static readonly RoutedEvent<VectorEventArgs> ResizeCompletedEvent =
        RoutedEvent.Register<VectorEventArgs>(nameof(ResizeCompleted), RoutingStrategies.Bubble, typeof(DesignEditorItem));

    public event EventHandler<DragStartedEventArgs> DragStarted { add => AddHandler(DragStartedEvent, value); remove => RemoveHandler(DragStartedEvent, value); }
    public event EventHandler<DragDeltaEventArgs> DragDelta { add => AddHandler(DragDeltaEvent, value); remove => RemoveHandler(DragDeltaEvent, value); }
    public event EventHandler<DragCompletedEventArgs> DragCompleted { add => AddHandler(DragCompletedEvent, value); remove => RemoveHandler(DragCompletedEvent, value); }
    public event EventHandler<ResizeDeltaEventArgs> ResizeDelta { add => AddHandler(ResizeDeltaEvent, value); remove => RemoveHandler(ResizeDeltaEvent, value); }
    public event EventHandler<VectorEventArgs> ResizeStarted { add => AddHandler(ResizeStartedEvent, value); remove => RemoveHandler(ResizeStartedEvent, value); }
    public event EventHandler<VectorEventArgs> ResizeCompleted { add => AddHandler(ResizeCompletedEvent, value); remove => RemoveHandler(ResizeCompletedEvent, value); }

    #endregion

    public DesignEditorItemState CurrentState => _states.Count > 0 ? _states.Peek() : null!;

    static DesignEditorItem()
    {
        SelectableMixin.Attach<DesignEditorItem>(IsSelectedProperty);
        FocusableProperty.OverrideDefaultValue<DesignEditorItem>(true);
        Layout.XProperty.Changed.AddClassHandler<DesignEditorItem>((item, _) => item.SyncLocationFromLayout());
        Layout.YProperty.Changed.AddClassHandler<DesignEditorItem>((item, _) => item.SyncLocationFromLayout());
    }

    public DesignEditorItem()
    {
        _states.Push(new ItemIdleState(this));
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_resizeAdorner != null)
        {
            _resizeAdorner.ResizeDelta -= OnAdornerResizeDelta;
            _resizeAdorner.ResizeStarted -= OnAdornerResizeStarted;
            _resizeAdorner.ResizeCompleted -= OnAdornerResizeCompleted;
        }

        _resizeAdorner = e.NameScope.Find<ResizeAdorner>("PART_Resizer");

        if (_resizeAdorner != null)
        {
            _resizeAdorner.ResizeDelta += OnAdornerResizeDelta;
            _resizeAdorner.ResizeStarted += OnAdornerResizeStarted;
            _resizeAdorner.ResizeCompleted += OnAdornerResizeCompleted;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        // ВАЖНО: Мы больше не следим за BorderThickness, чтобы избежать цикла.
        // Пересчитываем SelectedMargin только если меняется настройка SelectedBorderThickness.
        if (change.Property == SelectedBorderThicknessProperty)
        {
            RaisePropertyChanged(SelectedMarginProperty, default, SelectedMargin);
        }
        else if (change.Property == IsSelectedProperty)
        {
            UpdatePseudoClasses();
        }
        else if (change.Property == LocationProperty)
        {
            if (_isUpdatingLocation)
                return;

            try
            {
                _isUpdatingLocation = true;
                Layout.SetX(this, Location.X);
                Layout.SetY(this, Location.Y);
            }
            finally
            {
                _isUpdatingLocation = false;
            }
        }
    }

    private void UpdatePseudoClasses() => PseudoClasses.Set(":selected", IsSelected);

    private void SyncLocationFromLayout()
    {
        if (_isUpdatingLocation)
            return;

        var x = Layout.GetX(this);
        var y = Layout.GetY(this);

        if (double.IsNaN(x) && double.IsNaN(y))
            return;

        var nextLocation = new Point(
            double.IsNaN(x) ? Location.X : x,
            double.IsNaN(y) ? Location.Y : y);

        if (Math.Abs(nextLocation.X - Location.X) < 0.01 &&
            Math.Abs(nextLocation.Y - Location.Y) < 0.01)
            return;

        try
        {
            _isUpdatingLocation = true;
            SetCurrentValue(LocationProperty, nextLocation);
        }
        finally
        {
            _isUpdatingLocation = false;
        }
    }

    #region State Machine Management

    public void PushState(DesignEditorItemState state)
    {
        var previous = CurrentState;
        _states.Push(state);
        state.Enter(previous);
        UpdatePseudoClassesState(state);
    }

    public void PopState()
    {
        if (_states.Count > 1)
        {
            var current = _states.Pop();
            current.Exit();
            CurrentState.ReEnter(current);
            UpdatePseudoClassesState(CurrentState);
        }
    }

    private void UpdatePseudoClassesState(DesignEditorItemState state)
    {
        PseudoClasses.Set(":dragging", state is ItemDraggingState);
        PseudoClasses.Set(":resizing", state is ItemResizingState);
    }

    #endregion

    protected override void OnPointerPressed(PointerPressedEventArgs e) { base.OnPointerPressed(e); if (!e.Handled) CurrentState.OnPointerPressed(e); }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); CurrentState.OnPointerMoved(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { base.OnPointerReleased(e); CurrentState.OnPointerReleased(e); }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { base.OnPointerCaptureLost(e); while (_states.Count > 1) PopState(); }

    private void OnAdornerResizeStarted(object? sender, ResizeStartedEventArgs e) { PushState(new ItemResizingState(this, e.Direction)); RaiseEvent(new VectorEventArgs { RoutedEvent = ResizeStartedEvent, Vector = e.Vector }); e.Handled = true; }
    private void OnAdornerResizeDelta(object? sender, ResizeDeltaEventArgs e) { CurrentState.OnResizeDelta(e); e.Handled = true; }
    private void OnAdornerResizeCompleted(object? sender, VectorEventArgs e) { PopState(); RaiseEvent(new VectorEventArgs { RoutedEvent = ResizeCompletedEvent, Vector = e.Vector }); e.Handled = true; }

    internal void OnDragStarted(DragStartedEventArgs e) => RaiseEvent(e);
    internal void OnDragDelta(DragDeltaEventArgs e) => RaiseEvent(e);
    internal void OnDragCompleted(DragCompletedEventArgs e) => RaiseEvent(e);
}
