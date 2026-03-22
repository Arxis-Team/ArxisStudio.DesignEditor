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

        for (var i = 0; i < _targets.Count; i++)
        {
            var target = _targets[i];
            var initialTargetBounds = target.InitialBounds;
            var newX = nextBounds.X + ((initialTargetBounds.X - _initialBounds.X) * scaleX);
            var newY = nextBounds.Y + ((initialTargetBounds.Y - _initialBounds.Y) * scaleY);
            var newWidth = Math.Max(_minSize, initialTargetBounds.Width * scaleX);
            var newHeight = Math.Max(_minSize, initialTargetBounds.Height * scaleY);

            editor.SetDesignSize(target.Target, new Size(newWidth, newHeight));
            editor.SetDesignPosition(target.Target, new Point(newX, newY));
        }
    }

    public void Complete(DesignEditor editor)
    {
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
