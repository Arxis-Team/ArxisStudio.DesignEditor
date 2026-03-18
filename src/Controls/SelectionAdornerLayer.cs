using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;

namespace ArxisStudio.Controls;

/// <summary>
/// Представляет lightweight-layer для отрисовки нескольких <see cref="SelectionAdorner"/>
/// поверх selection targets редактора.
/// </summary>
public class SelectionAdornerLayer : Panel
{
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

        while (Children.Count > items.Count)
            Children.RemoveAt(Children.Count - 1);

        while (Children.Count < items.Count)
        {
            Children.Add(new SelectionAdorner
            {
                IsHitTestVisible = false,
                RenderTransformOrigin = RelativePoint.TopLeft
            });
        }

        for (var i = 0; i < items.Count; i++)
        {
            var child = (SelectionAdorner)Children[i];
            child.Theme = AdornerTheme;
            child.Role = items[i].Role;
            child.ShowHandles = false;
            child.IsInteractive = false;
        }

        UpdateChildTransforms();
        InvalidateMeasure();
        InvalidateArrange();
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
}
