using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;

namespace ArxisStudio.Controls;

/// <summary>
/// Представляет lightweight-layer для отрисовки нескольких <see cref="SelectionAdorner"/>
/// поверх selection targets редактора.
/// </summary>
public class SelectionAdornerLayer : Panel
{
    private readonly Dictionary<Control, SelectionAdorner> _adornersByTarget = new();

    /// <summary>
    /// Идентификатор routed event начала resize дочернего adorner'а.
    /// </summary>
    public static readonly RoutedEvent<SelectionAdornerResizeStartedEventArgs> AdornerResizeStartedEvent =
        RoutedEvent.Register<SelectionAdornerResizeStartedEventArgs>(
            nameof(AdornerResizeStarted),
            RoutingStrategies.Bubble,
            typeof(SelectionAdornerLayer));

    /// <summary>
    /// Идентификатор routed event шага resize дочернего adorner'а.
    /// </summary>
    public static readonly RoutedEvent<SelectionAdornerResizeDeltaEventArgs> AdornerResizeDeltaEvent =
        RoutedEvent.Register<SelectionAdornerResizeDeltaEventArgs>(
            nameof(AdornerResizeDelta),
            RoutingStrategies.Bubble,
            typeof(SelectionAdornerLayer));

    /// <summary>
    /// Идентификатор routed event завершения resize дочернего adorner'а.
    /// </summary>
    public static readonly RoutedEvent<SelectionAdornerResizeCompletedEventArgs> AdornerResizeCompletedEvent =
        RoutedEvent.Register<SelectionAdornerResizeCompletedEventArgs>(
            nameof(AdornerResizeCompleted),
            RoutingStrategies.Bubble,
            typeof(SelectionAdornerLayer));

    /// <summary>
    /// Идентификатор коллекции геометрии secondary/group adorner'ов.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<SelectionAdornerInfo>> ItemsProperty =
        AvaloniaProperty.Register<SelectionAdornerLayer, IReadOnlyList<SelectionAdornerInfo>>(
            nameof(Items),
            Array.Empty<SelectionAdornerInfo>());

    /// <summary>
    /// Идентификатор темы, применяемой к создаваемым <see cref="SelectionAdorner"/>.
    /// </summary>
    public static readonly StyledProperty<ControlTheme?> AdornerThemeProperty =
        AvaloniaProperty.Register<SelectionAdornerLayer, ControlTheme?>(nameof(AdornerTheme));

    /// <summary>
    /// Идентификатор текущего zoom viewport.
    /// </summary>
    public static readonly StyledProperty<double> ViewportZoomProperty =
        AvaloniaProperty.Register<SelectionAdornerLayer, double>(nameof(ViewportZoom), 1.0);

    static SelectionAdornerLayer()
    {
        ItemsProperty.Changed.AddClassHandler<SelectionAdornerLayer>((layer, _) => layer.SyncChildren());
        AdornerThemeProperty.Changed.AddClassHandler<SelectionAdornerLayer>((layer, _) => layer.SyncChildren());
        ViewportZoomProperty.Changed.AddClassHandler<SelectionAdornerLayer>((layer, _) =>
        {
            layer.UpdateChildTransforms();
            layer.InvalidateMeasure();
            layer.InvalidateArrange();
        });
    }

    /// <summary>
    /// Получает или задает коллекцию secondary/group adorner'ов.
    /// </summary>
    public IReadOnlyList<SelectionAdornerInfo> Items
    {
        get => GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>
    /// Получает или задает тему для дочерних <see cref="SelectionAdorner"/>.
    /// </summary>
    public ControlTheme? AdornerTheme
    {
        get => GetValue(AdornerThemeProperty);
        set => SetValue(AdornerThemeProperty, value);
    }

    /// <summary>
    /// Получает или задает текущий zoom viewport.
    /// </summary>
    public double ViewportZoom
    {
        get => GetValue(ViewportZoomProperty);
        set => SetValue(ViewportZoomProperty, value);
    }

    /// <summary>
    /// Возникает при начале resize любого дочернего adorner'а.
    /// </summary>
    public event EventHandler<SelectionAdornerResizeStartedEventArgs> AdornerResizeStarted
    {
        add => AddHandler(AdornerResizeStartedEvent, value);
        remove => RemoveHandler(AdornerResizeStartedEvent, value);
    }

    /// <summary>
    /// Возникает при изменении размера любого дочернего adorner'а.
    /// </summary>
    public event EventHandler<SelectionAdornerResizeDeltaEventArgs> AdornerResizeDelta
    {
        add => AddHandler(AdornerResizeDeltaEvent, value);
        remove => RemoveHandler(AdornerResizeDeltaEvent, value);
    }

    /// <summary>
    /// Возникает после завершения resize любого дочернего adorner'а.
    /// </summary>
    public event EventHandler<SelectionAdornerResizeCompletedEventArgs> AdornerResizeCompleted
    {
        add => AddHandler(AdornerResizeCompletedEvent, value);
        remove => RemoveHandler(AdornerResizeCompletedEvent, value);
    }

    /// <summary>
    /// Измеряет требуемый размер overlay-слоя по геометрии всех дочерних adorner'ов.
    /// </summary>
    /// <param name="availableSize">Доступный размер, предоставленный системой layout.</param>
    /// <returns>Фактический размер слоя, необходимый для размещения всех adorner'ов.</returns>
    protected override Size MeasureOverride(Size availableSize)
    {
        SyncChildren();

        var zoom = Math.Max(0.0001, ViewportZoom);
        double maxRight = 0;
        double maxBottom = 0;

        for (var i = 0; i < Children.Count && i < Items.Count; i++)
        {
            var child = (SelectionAdorner)Children[i];
            var bounds = Items[i].Bounds;
            var childSize = new Size(bounds.Width * zoom, bounds.Height * zoom);
            child.Measure(childSize);
            maxRight = Math.Max(maxRight, bounds.X + childSize.Width);
            maxBottom = Math.Max(maxBottom, bounds.Y + childSize.Height);
        }

        return new Size(maxRight, maxBottom);
    }

    /// <summary>
    /// Размещает дочерние adorner'ы в координатах редактора с учетом текущего масштаба viewport.
    /// </summary>
    /// <param name="finalSize">Итоговый размер, выделенный слою системой layout.</param>
    /// <returns>Фактический использованный размер слоя.</returns>
    protected override Size ArrangeOverride(Size finalSize)
    {
        SyncChildren();

        var zoom = Math.Max(0.0001, ViewportZoom);
        for (var i = 0; i < Children.Count && i < Items.Count; i++)
        {
            var child = (SelectionAdorner)Children[i];
            var bounds = Items[i].Bounds;
            child.Width = bounds.Width * zoom;
            child.Height = bounds.Height * zoom;
            child.Arrange(new Rect(bounds.X, bounds.Y, child.Width, child.Height));
        }

        return finalSize;
    }

    private void SyncChildren()
    {
        var items = Items ?? Array.Empty<SelectionAdornerInfo>();
        var desiredChildren = new List<SelectionAdorner>(items.Count);
        var usedTargets = new HashSet<Control>();

        for (var i = 0; i < items.Count; i++)
        {
            var info = items[i];
            SelectionAdorner child;

            if (info.Target != null)
            {
                usedTargets.Add(info.Target);
                if (!_adornersByTarget.TryGetValue(info.Target, out child!))
                {
                    child = CreateChildAdorner();
                    _adornersByTarget[info.Target] = child;
                }
            }
            else
            {
                child = CreateChildAdorner();
            }

            child.Theme = AdornerTheme;
            child.Role = info.Role;
            child.ShowHandles = info.ShowHandles;
            child.IsInteractive = info.IsInteractive;
            child.ResizePolicy = info.ResizePolicy;
            child.MovePolicy = info.MovePolicy;
            child.IsHitTestVisible = info.ShowHandles &&
                                     info.IsInteractive &&
                                     info.ResizePolicy != ArxisStudio.Attached.ResizePolicy.None;
            desiredChildren.Add(child);
        }

        var staleTargets = new List<Control>();
        foreach (var pair in _adornersByTarget)
        {
            if (usedTargets.Contains(pair.Key))
                continue;

            var child = pair.Value;
            child.ResizeStarted -= OnChildResizeStarted;
            child.ResizeDelta -= OnChildResizeDelta;
            child.ResizeCompleted -= OnChildResizeCompleted;
            staleTargets.Add(pair.Key);
        }

        foreach (var target in staleTargets)
            _adornersByTarget.Remove(target);

        if (Children.Count != desiredChildren.Count || !Children.SequenceEqual(desiredChildren))
        {
            Children.Clear();
            foreach (var child in desiredChildren)
                Children.Add(child);
        }

        UpdateChildTransforms();
        InvalidateMeasure();
        InvalidateArrange();
    }

    private SelectionAdorner CreateChildAdorner()
    {
        var adorner = new SelectionAdorner
        {
            RenderTransformOrigin = RelativePoint.TopLeft
        };

        adorner.ResizeStarted += OnChildResizeStarted;
        adorner.ResizeDelta += OnChildResizeDelta;
        adorner.ResizeCompleted += OnChildResizeCompleted;
        return adorner;
    }

    private void UpdateChildTransforms()
    {
        var zoom = Math.Max(0.0001, ViewportZoom);
        var inverseScale = new ScaleTransform(1 / zoom, 1 / zoom);

        foreach (var child in Children)
        {
            if (child is SelectionAdorner adorner)
                adorner.RenderTransform = inverseScale;
        }
    }

    private void OnChildResizeStarted(object? sender, ResizeStartedEventArgs e)
    {
        if (sender is not SelectionAdorner adorner || TryGetAdornerInfo(adorner, out var info) == false)
            return;

        RaiseEvent(new SelectionAdornerResizeStartedEventArgs(info, e.Vector, e.Direction, AdornerResizeStartedEvent));
        e.Handled = true;
    }

    private void OnChildResizeDelta(object? sender, ResizeDeltaEventArgs e)
    {
        if (sender is not SelectionAdorner adorner || TryGetAdornerInfo(adorner, out var info) == false)
            return;

        RaiseEvent(new SelectionAdornerResizeDeltaEventArgs(info, e.Delta, e.Direction, AdornerResizeDeltaEvent));
        e.Handled = true;
    }

    private void OnChildResizeCompleted(object? sender, VectorEventArgs e)
    {
        if (sender is not SelectionAdorner adorner || TryGetAdornerInfo(adorner, out var info) == false)
            return;

        RaiseEvent(new SelectionAdornerResizeCompletedEventArgs(info, e.Vector, AdornerResizeCompletedEvent));
        e.Handled = true;
    }

    private bool TryGetAdornerInfo(SelectionAdorner adorner, out SelectionAdornerInfo info)
    {
        var index = Children.IndexOf(adorner);
        if (index < 0 || index >= Items.Count)
        {
            info = null!;
            return false;
        }

        info = Items[index];
        return true;
    }
}

/// <summary>
/// Базовый класс routed event args для resize событий дочернего adorner'а.
/// </summary>
public abstract class SelectionAdornerResizeEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SelectionAdornerResizeEventArgs"/>.
    /// </summary>
    protected SelectionAdornerResizeEventArgs(SelectionAdornerInfo adornerInfo, RoutedEvent routedEvent)
        : base(routedEvent)
    {
        AdornerInfo = adornerInfo;
    }

    /// <summary>
    /// Получает описание adorner'а, инициировавшего событие.
    /// </summary>
    public SelectionAdornerInfo AdornerInfo { get; }
}

/// <summary>
/// Аргументы начала resize дочернего adorner'а.
/// </summary>
public sealed class SelectionAdornerResizeStartedEventArgs : SelectionAdornerResizeEventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SelectionAdornerResizeStartedEventArgs"/>.
    /// </summary>
    public SelectionAdornerResizeStartedEventArgs(
        SelectionAdornerInfo adornerInfo,
        Vector vector,
        ResizeDirection direction,
        RoutedEvent routedEvent)
        : base(adornerInfo, routedEvent)
    {
        Vector = vector;
        Direction = direction;
    }

    /// <summary>
    /// Получает стартовый вектор resize.
    /// </summary>
    public Vector Vector { get; }

    /// <summary>
    /// Получает направление активной ручки.
    /// </summary>
    public ResizeDirection Direction { get; }
}

/// <summary>
/// Аргументы шага resize дочернего adorner'а.
/// </summary>
public sealed class SelectionAdornerResizeDeltaEventArgs : SelectionAdornerResizeEventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SelectionAdornerResizeDeltaEventArgs"/>.
    /// </summary>
    public SelectionAdornerResizeDeltaEventArgs(
        SelectionAdornerInfo adornerInfo,
        Vector delta,
        ResizeDirection direction,
        RoutedEvent routedEvent)
        : base(adornerInfo, routedEvent)
    {
        Delta = delta;
        Direction = direction;
    }

    /// <summary>
    /// Получает вектор resize.
    /// </summary>
    public Vector Delta { get; }

    /// <summary>
    /// Получает направление активной ручки.
    /// </summary>
    public ResizeDirection Direction { get; }
}

/// <summary>
/// Аргументы завершения resize дочернего adorner'а.
/// </summary>
public sealed class SelectionAdornerResizeCompletedEventArgs : SelectionAdornerResizeEventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="SelectionAdornerResizeCompletedEventArgs"/>.
    /// </summary>
    public SelectionAdornerResizeCompletedEventArgs(
        SelectionAdornerInfo adornerInfo,
        Vector vector,
        RoutedEvent routedEvent)
        : base(adornerInfo, routedEvent)
    {
        Vector = vector;
    }

    /// <summary>
    /// Получает итоговый вектор resize.
    /// </summary>
    public Vector Vector { get; }
}
