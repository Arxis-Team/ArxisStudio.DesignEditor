using Avalonia;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System.Collections.Generic;

namespace ArxisStudio.Controls;

/// <summary>
/// Перечисление направлений изменения размера.
/// </summary>
public enum ResizeDirection
{
    Top, Bottom, Left, Right,
    TopLeft, TopRight, BottomLeft, BottomRight
}

/// <summary>
/// Аргументы события процесса изменения размера.
/// </summary>
public class ResizeDeltaEventArgs : RoutedEventArgs
{
    public Vector Delta { get; }
    public ResizeDirection Direction { get; }

    public ResizeDeltaEventArgs(Vector delta, ResizeDirection direction, RoutedEvent routedEvent)
        : base(routedEvent)
    {
        Delta = delta;
        Direction = direction;
    }
}

/// <summary>
/// Аргументы события начала изменения размера.
/// </summary>
public class ResizeStartedEventArgs : RoutedEventArgs
{
    public ResizeDirection Direction { get; }
    public Vector Vector { get; }

    public ResizeStartedEventArgs(Vector vector, ResizeDirection direction, RoutedEvent routedEvent)
        : base(routedEvent)
    {
        Vector = vector;
        Direction = direction;
    }
}

/// <summary>
/// Визуальный контрол (рамка с ручками) для изменения размеров элементов.
/// </summary>
[TemplatePart("PART_TopLeft", typeof(Thumb))]
[TemplatePart("PART_Top", typeof(Thumb))]
[TemplatePart("PART_TopRight", typeof(Thumb))]
[TemplatePart("PART_Right", typeof(Thumb))]
[TemplatePart("PART_BottomRight", typeof(Thumb))]
[TemplatePart("PART_Bottom", typeof(Thumb))]
[TemplatePart("PART_BottomLeft", typeof(Thumb))]
[TemplatePart("PART_Left", typeof(Thumb))]
public class ResizeAdorner : TemplatedControl
{
    private readonly Dictionary<Thumb, ResizeDirection> _thumbDirections = new();

    #region Styled Properties

    public static readonly StyledProperty<IBrush> AdornerBrushProperty =
        AvaloniaProperty.Register<ResizeAdorner, IBrush>(nameof(AdornerBrush), Brushes.DodgerBlue);

    /// <summary>
    /// Кисть для отрисовки рамки и границ ручек.
    /// </summary>
    public IBrush AdornerBrush
    {
        get => GetValue(AdornerBrushProperty);
        set => SetValue(AdornerBrushProperty, value);
    }

    public static readonly StyledProperty<double> HandleSizeProperty =
        AvaloniaProperty.Register<ResizeAdorner, double>(nameof(HandleSize), 8.0);

    /// <summary>
    /// Размер ручек (Thumb) в пикселях.
    /// </summary>
    public double HandleSize
    {
        get => GetValue(HandleSizeProperty);
        set => SetValue(HandleSizeProperty, value);
    }

    #endregion

    #region Events

    public static readonly RoutedEvent<ResizeDeltaEventArgs> ResizeDeltaEvent =
        RoutedEvent.Register<ResizeDeltaEventArgs>(nameof(ResizeDelta), RoutingStrategies.Bubble, typeof(ResizeAdorner));

    public static readonly RoutedEvent<ResizeStartedEventArgs> ResizeStartedEvent =
        RoutedEvent.Register<ResizeStartedEventArgs>(nameof(ResizeStarted), RoutingStrategies.Bubble, typeof(ResizeAdorner));

    public static readonly RoutedEvent<VectorEventArgs> ResizeCompletedEvent =
        RoutedEvent.Register<VectorEventArgs>(nameof(ResizeCompleted), RoutingStrategies.Bubble, typeof(ResizeAdorner));

    public event EventHandler<ResizeDeltaEventArgs> ResizeDelta
    {
        add => AddHandler(ResizeDeltaEvent, value);
        remove => RemoveHandler(ResizeDeltaEvent, value);
    }

    public event EventHandler<ResizeStartedEventArgs> ResizeStarted
    {
        add => AddHandler(ResizeStartedEvent, value);
        remove => RemoveHandler(ResizeStartedEvent, value);
    }

    public event EventHandler<VectorEventArgs> ResizeCompleted
    {
        add => AddHandler(ResizeCompletedEvent, value);
        remove => RemoveHandler(ResizeCompletedEvent, value);
    }

    #endregion

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        UnbindThumbs();

        BindThumb(e, "PART_TopLeft", ResizeDirection.TopLeft);
        BindThumb(e, "PART_Top", ResizeDirection.Top);
        BindThumb(e, "PART_TopRight", ResizeDirection.TopRight);
        BindThumb(e, "PART_Right", ResizeDirection.Right);
        BindThumb(e, "PART_BottomRight", ResizeDirection.BottomRight);
        BindThumb(e, "PART_Bottom", ResizeDirection.Bottom);
        BindThumb(e, "PART_BottomLeft", ResizeDirection.BottomLeft);
        BindThumb(e, "PART_Left", ResizeDirection.Left);
    }

    private void BindThumb(TemplateAppliedEventArgs e, string name, ResizeDirection direction)
    {
        if (e.NameScope.Find(name) is Thumb thumb)
        {
            thumb.DragDelta -= OnThumbDragDelta;
            thumb.DragStarted -= OnThumbDragStarted;
            thumb.DragCompleted -= OnThumbDragCompleted;

            thumb.DragDelta += OnThumbDragDelta;
            thumb.DragStarted += OnThumbDragStarted;
            thumb.DragCompleted += OnThumbDragCompleted;
            _thumbDirections[thumb] = direction;
        }
    }

    private void UnbindThumbs()
    {
        foreach (var thumb in _thumbDirections.Keys)
        {
            thumb.DragDelta -= OnThumbDragDelta;
            thumb.DragStarted -= OnThumbDragStarted;
            thumb.DragCompleted -= OnThumbDragCompleted;
        }

        _thumbDirections.Clear();
    }

    private void OnThumbDragDelta(object? sender, VectorEventArgs args)
    {
        if (sender is Thumb thumb && _thumbDirections.TryGetValue(thumb, out var direction))
            RaiseEvent(new ResizeDeltaEventArgs(args.Vector, direction, ResizeDeltaEvent));
    }

    private void OnThumbDragStarted(object? sender, VectorEventArgs args)
    {
        if (sender is Thumb thumb && _thumbDirections.TryGetValue(thumb, out var direction))
            RaiseEvent(new ResizeStartedEventArgs(args.Vector, direction, ResizeStartedEvent));
    }

    private void OnThumbDragCompleted(object? sender, VectorEventArgs args)
    {
        RaiseEvent(new VectorEventArgs
        {
            RoutedEvent = ResizeCompletedEvent,
            Vector = args.Vector
        });
    }
}
