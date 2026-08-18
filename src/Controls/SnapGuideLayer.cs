using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ArxisStudio.Guides;

namespace ArxisStudio.Controls;

/// <summary>
/// Слой направляющих, отрисованных с точностью до физического пикселя.
/// </summary>
/// <remarks>
/// Собственной трансформации у контрола нет — как и у <see cref="DesignGrid"/>,
/// он получает viewport напрямую и пересчитывает мировые координаты в экранные сам.
/// Только так толщина линии остаётся пиксельной на любом масштабе: направляющая,
/// растущая вместе с зумом, перестаёт быть указателем и начинает закрывать макет.
/// <para>
/// Визуального дерева слой не строит вовсе, поэтому здесь не может повториться
/// история <see cref="SelectionAdornerLayer"/> с перестроением детей внутри
/// layout-прохода: рисование идёт целиком в <see cref="Render"/>.
/// </para>
/// </remarks>
internal class SnapGuideLayer : Control
{
    /// <summary>
    /// Идентификатор свойства набора направляющих в мировых координатах.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<DesignSnapGuide>?> GuidesProperty =
        AvaloniaProperty.Register<SnapGuideLayer, IReadOnlyList<DesignSnapGuide>?>(nameof(Guides));

    /// <summary>
    /// Идентификатор свойства пользовательских направляющих.
    /// </summary>
    public static readonly StyledProperty<IReadOnlyList<DesignGuide>?> UserGuidesProperty =
        AvaloniaProperty.Register<SnapGuideLayer, IReadOnlyList<DesignGuide>?>(nameof(UserGuides));

    /// <summary>
    /// Идентификатор свойства направляющей, показываемой во время перемещения.
    /// </summary>
    public static readonly StyledProperty<DesignGuide?> GuidePreviewProperty =
        AvaloniaProperty.Register<SnapGuideLayer, DesignGuide?>(nameof(GuidePreview));

    /// <summary>
    /// Идентификатор свойства видимости пользовательских направляющих.
    /// </summary>
    public static readonly StyledProperty<bool> ShowUserGuidesProperty =
        AvaloniaProperty.Register<SnapGuideLayer, bool>(nameof(ShowUserGuides), true);

    /// <summary>
    /// Идентификатор свойства кисти пользовательских направляющих.
    /// </summary>
    public static readonly StyledProperty<IBrush?> UserGuideBrushProperty =
        AvaloniaProperty.Register<SnapGuideLayer, IBrush?>(nameof(UserGuideBrush));

    /// <summary>
    /// Идентификатор свойства кисти линий.
    /// </summary>
    public static readonly StyledProperty<IBrush?> LineBrushProperty =
        AvaloniaProperty.Register<SnapGuideLayer, IBrush?>(nameof(LineBrush));

    /// <summary>
    /// Идентификатор свойства кисти линий выравнивания по центру.
    /// </summary>
    public static readonly StyledProperty<IBrush?> CentreLineBrushProperty =
        AvaloniaProperty.Register<SnapGuideLayer, IBrush?>(nameof(CentreLineBrush));

    /// <summary>
    /// Идентификатор свойства штриха линий выравнивания.
    /// </summary>
    public static readonly StyledProperty<IDashStyle?> LineDashStyleProperty =
        AvaloniaProperty.Register<SnapGuideLayer, IDashStyle?>(nameof(LineDashStyle));

    /// <summary>
    /// Идентификатор свойства штриха пользовательских направляющих.
    /// </summary>
    public static readonly StyledProperty<IDashStyle?> UserGuideDashStyleProperty =
        AvaloniaProperty.Register<SnapGuideLayer, IDashStyle?>(nameof(UserGuideDashStyle));

    /// <summary>
    /// Идентификатор свойства толщины линий в физических пикселях.
    /// </summary>
    public static readonly StyledProperty<double> LineThicknessProperty =
        AvaloniaProperty.Register<SnapGuideLayer, double>(nameof(LineThickness), 1.0);

    /// <summary>
    /// Идентификатор свойства положения viewport в мировых координатах.
    /// </summary>
    public static readonly StyledProperty<Point> ViewportLocationProperty =
        AvaloniaProperty.Register<SnapGuideLayer, Point>(nameof(ViewportLocation));

    /// <summary>
    /// Идентификатор свойства масштаба viewport.
    /// </summary>
    public static readonly StyledProperty<double> ViewportZoomProperty =
        AvaloniaProperty.Register<SnapGuideLayer, double>(nameof(ViewportZoom), 1.0);

    static SnapGuideLayer()
    {
        AffectsRender<SnapGuideLayer>(
            GuidesProperty,
            UserGuidesProperty,
            ShowUserGuidesProperty,
            GuidePreviewProperty,
            UserGuideBrushProperty,
            CentreLineBrushProperty,
            LineDashStyleProperty,
            UserGuideDashStyleProperty,
            LineBrushProperty,
            LineThicknessProperty,
            ViewportLocationProperty,
            ViewportZoomProperty);
    }

    /// <summary>
    /// Получает или задает набор направляющих в мировых координатах.
    /// </summary>
    public IReadOnlyList<DesignSnapGuide>? Guides
    {
        get => GetValue(GuidesProperty);
        set => SetValue(GuidesProperty, value);
    }

    /// <summary>
    /// Получает или задает пользовательские направляющие.
    /// </summary>
    public IReadOnlyList<DesignGuide>? UserGuides
    {
        get => GetValue(UserGuidesProperty);
        set => SetValue(UserGuidesProperty, value);
    }

    /// <summary>
    /// Получает или задает направляющую, показываемую во время перемещения.
    /// </summary>
    public DesignGuide? GuidePreview
    {
        get => GetValue(GuidePreviewProperty);
        set => SetValue(GuidePreviewProperty, value);
    }

    /// <summary>
    /// Получает или задает признак отображения пользовательских направляющих.
    /// </summary>
    public bool ShowUserGuides
    {
        get => GetValue(ShowUserGuidesProperty);
        set => SetValue(ShowUserGuidesProperty, value);
    }

    /// <summary>
    /// Получает или задает кисть пользовательских направляющих.
    /// </summary>
    public IBrush? UserGuideBrush
    {
        get => GetValue(UserGuideBrushProperty);
        set => SetValue(UserGuideBrushProperty, value);
    }

    /// <summary>
    /// Получает или задает кисть линий.
    /// </summary>
    public IBrush? LineBrush
    {
        get => GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <summary>
    /// Получает или задает кисть линий выравнивания по центру.
    /// </summary>
    /// <remarks>
    /// Если не задана, линии центра рисуются той же кистью, что и линии по краю:
    /// различение видов — настройка, а не обязанность темы.
    /// </remarks>
    public IBrush? CentreLineBrush
    {
        get => GetValue(CentreLineBrushProperty);
        set => SetValue(CentreLineBrushProperty, value);
    }

    /// <summary>
    /// Получает или задает штрих линий выравнивания.
    /// </summary>
    public IDashStyle? LineDashStyle
    {
        get => GetValue(LineDashStyleProperty);
        set => SetValue(LineDashStyleProperty, value);
    }

    /// <summary>
    /// Получает или задает штрих пользовательских направляющих.
    /// </summary>
    public IDashStyle? UserGuideDashStyle
    {
        get => GetValue(UserGuideDashStyleProperty);
        set => SetValue(UserGuideDashStyleProperty, value);
    }

    /// <summary>
    /// Получает или задает толщину линий в физических пикселях.
    /// </summary>
    public double LineThickness
    {
        get => GetValue(LineThicknessProperty);
        set => SetValue(LineThicknessProperty, value);
    }

    /// <summary>
    /// Получает или задает положение viewport в мировых координатах.
    /// </summary>
    public Point ViewportLocation
    {
        get => GetValue(ViewportLocationProperty);
        set => SetValue(ViewportLocationProperty, value);
    }

    /// <summary>
    /// Получает или задает масштаб viewport.
    /// </summary>
    public double ViewportZoom
    {
        get => GetValue(ViewportZoomProperty);
        set => SetValue(ViewportZoomProperty, value);
    }

    /// <summary>
    /// Отрисовывает направляющие.
    /// </summary>
    /// <param name="context">Контекст отрисовки.</param>
    public override void Render(DrawingContext context)
    {
        var zoom = ViewportZoom;
        if (!(zoom > 0))
            return;

        var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        if (!(scaling > 0))
            scaling = 1.0;

        // Толщина задана в пикселях устройства, поэтому переводим её в DIP.
        var thickness = Math.Max(0.0, LineThickness) / scaling;
        if (!(thickness > 0))
            return;

        var origin = ViewportLocation;

        // Пользовательские направляющие рисуются первыми и всегда: они не привязаны
        // к жесту и лежат под линиями выравнивания, чтобы совпадение было видно.
        RenderUserGuides(context, origin, zoom, scaling, thickness);

        var guides = Guides;
        if (guides == null || guides.Count == 0)
            return;

        if (LineBrush is not { } brush)
            return;

        var dash = LineDashStyle;
        var edgePen = new Pen(brush, thickness, dash);

        // Перо центра собирается, только когда кисть задана: иначе оба вида рисуются
        // одним пером, и это ровно прежнее поведение.
        var centrePen = CentreLineBrush is { } centreBrush
            ? new Pen(centreBrush, thickness, dash)
            : edgePen;

        for (var i = 0; i < guides.Count; i++)
        {
            var guide = guides[i];
            var pen = guide.Kind == DesignSnapGuideKind.Centre ? centrePen : edgePen;
            var vertical = guide.Orientation == DesignSnapGuideOrientation.Vertical;

            var lineOrigin = vertical ? origin.X : origin.Y;
            var spanOrigin = vertical ? origin.Y : origin.X;

            var position = DesignGrid.SnapToDevicePixel((guide.Position - lineOrigin) * zoom, scaling);
            var start = (guide.Start - spanOrigin) * zoom;
            var end = (guide.End - spanOrigin) * zoom;

            var from = vertical ? new Point(position, start) : new Point(start, position);
            var to = vertical ? new Point(position, end) : new Point(end, position);

            context.DrawLine(pen, from, to);
        }
    }

    /// <summary>
    /// Отрисовывает пользовательские направляющие через весь viewport.
    /// </summary>
    /// <remarks>
    /// В отличие от линий выравнивания, эти не натянуты между двумя элементами:
    /// направляющая задаёт координату, а не отношение, и обрывать её незачем.
    /// </remarks>
    private void RenderUserGuides(DrawingContext context, Point origin, double zoom, double scaling, double thickness)
    {
        if (!ShowUserGuides)
            return;

        var guides = UserGuides;
        if (UserGuideBrush is not { } brush)
            return;

        if ((guides == null || guides.Count == 0) && GuidePreview == null)
            return;

        var pen = new Pen(brush, thickness, UserGuideDashStyle);
        guides ??= Array.Empty<DesignGuide>();

        for (var i = 0; i < guides.Count; i++)
            DrawUserGuide(context, pen, guides[i], origin, zoom, scaling);

        // Превью рисуется поверх набора: во время протяжки видно и то, где линия
        // стоит сейчас, и то, куда она встанет, — правку применит хост, а не редактор.
        if (GuidePreview is { } preview)
            DrawUserGuide(context, pen, preview, origin, zoom, scaling);
    }

    /// <summary>
    /// Рисует одну пользовательскую направляющую через весь слой.
    /// </summary>
    private void DrawUserGuide(DrawingContext context, IPen pen, DesignGuide guide, Point origin, double zoom, double scaling)
    {
        var size = Bounds.Size;

        {
            if (guide.Orientation == DesignGuideOrientation.Vertical)
            {
                var x = DesignGrid.SnapToDevicePixel((guide.Position - origin.X) * zoom, scaling);
                if (x < 0 || x > size.Width)
                    return;

                context.DrawLine(pen, new Point(x, 0), new Point(x, size.Height));
            }
            else
            {
                var y = DesignGrid.SnapToDevicePixel((guide.Position - origin.Y) * zoom, scaling);
                if (y < 0 || y > size.Height)
                    return;

                context.DrawLine(pen, new Point(0, y), new Point(size.Width, y));
            }
        }
    }
}
