using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Controls;

namespace ArxisStudio;

internal sealed class GroupResizeOperation
    : IInteractionOperation
{
    private readonly ResizeDirection _direction;
    private readonly Rect _initialBounds;
    private readonly IReadOnlyList<GroupResizeTarget> _targets;
    private readonly double _minSize;
    private Vector _accumulatedDelta;

    public GroupResizeOperation(ResizeDirection direction, Rect initialBounds, IReadOnlyList<GroupResizeTarget> targets, double minSize)
    {
        _direction = direction;
        _initialBounds = initialBounds;
        _targets = targets;
        _minSize = minSize;
        _accumulatedDelta = Vector.Zero;
    }

    public void Update(DesignEditor editor, Vector worldDelta)
    {
        _accumulatedDelta += worldDelta;
        var nextBounds = CalculateResizedBounds(_initialBounds, _direction, _accumulatedDelta, _minSize);
        var scaleX = _initialBounds.Width > 0 ? nextBounds.Width / _initialBounds.Width : 1.0;
        var scaleY = _initialBounds.Height > 0 ? nextBounds.Height / _initialBounds.Height : 1.0;

        // Масштаб группы ограничивается самым «зажатым» target'ом.
        // Иначе его размер упирается в минимум и перестаёт уменьшаться,
        // а позиция продолжает считаться от неограниченного масштаба —
        // target'ы наезжают друг на друга и вылезают за рамку выделения.
        scaleX = Math.Max(scaleX, GetMinimumScale(_targets, horizontal: true, _minSize));
        scaleY = Math.Max(scaleY, GetMinimumScale(_targets, horizontal: false, _minSize));

        for (var i = 0; i < _targets.Count; i++)
        {
            var target = _targets[i];
            var initialTargetBounds = target.InitialBounds;
            var newX = nextBounds.X + ((initialTargetBounds.X - _initialBounds.X) * scaleX);
            var newY = nextBounds.Y + ((initialTargetBounds.Y - _initialBounds.Y) * scaleY);
            var newWidth = initialTargetBounds.Width * scaleX;
            var newHeight = initialTargetBounds.Height * scaleY;

            editor.SetDesignSize(target.Target, new Size(newWidth, newHeight));
            editor.SetDesignPosition(target.Target, new Point(newX, newY));
        }
    }

    public void Complete(DesignEditor editor)
    {
    }

    /// <summary>
    /// Возвращает минимальный масштаб, при котором ни один target не опускается
    /// ниже своего минимального размера.
    /// </summary>
    private static double GetMinimumScale(IReadOnlyList<GroupResizeTarget> targets, bool horizontal, double minSize)
    {
        var minScale = 0.0;

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var initial = horizontal ? target.InitialBounds.Width : target.InitialBounds.Height;
            if (initial <= 0)
                continue;

            var limit = horizontal
                ? Math.Max(minSize, target.Target.MinWidth)
                : Math.Max(minSize, target.Target.MinHeight);

            minScale = Math.Max(minScale, limit / initial);
        }

        return minScale;
    }

    private static Rect CalculateResizedBounds(Rect initialBounds, ResizeDirection direction, Vector delta, double minSize)
    {
        var newX = initialBounds.X;
        var newY = initialBounds.Y;
        var newWidth = initialBounds.Width;
        var newHeight = initialBounds.Height;

        switch (direction)
        {
            case ResizeDirection.Right:
                newWidth += delta.X;
                break;
            case ResizeDirection.Bottom:
                newHeight += delta.Y;
                break;
            case ResizeDirection.Left:
                newWidth -= delta.X;
                newX += delta.X;
                break;
            case ResizeDirection.Top:
                newHeight -= delta.Y;
                newY += delta.Y;
                break;
            case ResizeDirection.BottomRight:
                newWidth += delta.X;
                newHeight += delta.Y;
                break;
            case ResizeDirection.BottomLeft:
                newWidth -= delta.X;
                newX += delta.X;
                newHeight += delta.Y;
                break;
            case ResizeDirection.TopRight:
                newWidth += delta.X;
                newHeight -= delta.Y;
                newY += delta.Y;
                break;
            case ResizeDirection.TopLeft:
                newWidth -= delta.X;
                newX += delta.X;
                newHeight -= delta.Y;
                newY += delta.Y;
                break;
        }

        var initialRight = initialBounds.Right;
        var initialBottom = initialBounds.Bottom;

        newWidth = Math.Max(minSize, newWidth);
        newHeight = Math.Max(minSize, newHeight);

        if (direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
            newX = initialRight - newWidth;

        if (direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
            newY = initialBottom - newHeight;

        return new Rect(newX, newY, newWidth, newHeight);
    }
}

internal readonly struct GroupResizeTarget
{
    public GroupResizeTarget(Control target, Rect initialBounds)
    {
        Target = target;
        InitialBounds = initialBounds;
    }

    public Control Target { get; }
    public Rect InitialBounds { get; }
}
