using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using ArxisStudio.Controls;

namespace ArxisStudio.States;

/// <summary>
/// Базовый класс состояния элемента дизайнера.
/// </summary>
public abstract class DesignEditorItemState
{
    protected DesignEditorItem Container { get; }
    protected DesignEditorItemState(DesignEditorItem container) => Container = container;

    public virtual void Enter(DesignEditorItemState from) { }
    public virtual void Exit() { }
    public virtual void ReEnter(DesignEditorItemState from) { }
    public virtual void OnPointerPressed(PointerPressedEventArgs e) { }
    public virtual void OnPointerMoved(PointerEventArgs e) { }
    public virtual void OnPointerReleased(PointerReleasedEventArgs e) { }
    public virtual void OnResizeDelta(ResizeDeltaEventArgs e) { }
}

/// <summary>
/// Состояние покоя. Ожидает выделения или начала перетаскивания.
/// </summary>
public class ItemIdleState : DesignEditorItemState
{
    private Point _startPoint;
    private bool _isPressed;
    private bool _shouldSkipSelectionToggle;

    public ItemIdleState(DesignEditorItem container) : base(container) { }

    public override void ReEnter(DesignEditorItemState from)
    {
        _isPressed = false;
        _shouldSkipSelectionToggle = false;
    }

    public override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(Container).Properties;
        if (props.IsLeftButtonPressed)
        {
            e.Pointer.Capture(Container);
            e.Handled = true;
            _isPressed = true;

            var parent = Container.GetVisualParent();
            if (parent != null) _startPoint = e.GetPosition((Visual)parent);
            HandleSelectionOnPress(e);
        }
    }

    public override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_isPressed || !Container.IsDraggable) return;

        var parent = Container.GetVisualParent();
        if (parent == null) return;

        // ВАЖНО: Разрешаем драг, если родитель - AbsolutePanel (в т.ч. DesignSurface) или Canvas.
        bool isAbsolute = parent is AbsolutePanel || parent is Canvas;
        if (!isAbsolute) return;

        var currentPoint = e.GetPosition((Visual)parent);
        if (Vector.Distance(_startPoint, currentPoint) > 3)
            Container.PushState(new ItemDraggingState(Container, _startPoint));
    }

    public override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_isPressed)
        {
            HandleSelectionOnRelease(e);
            _isPressed = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void HandleSelectionOnPress(PointerPressedEventArgs e)
    {
        var editor = Container.FindAncestorOfType<DesignEditor>();
        if (editor == null) return;
        bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        if (!Container.IsSelected)
        {
            if (!isCtrl) editor.Selection.Clear();
            editor.Selection.Select(editor.IndexFromContainer(Container));
            _shouldSkipSelectionToggle = true;
        } else _shouldSkipSelectionToggle = false;
    }

    private void HandleSelectionOnRelease(PointerReleasedEventArgs e)
    {
        var editor = Container.FindAncestorOfType<DesignEditor>();
        if (editor == null) return;
        bool isCtrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var index = editor.IndexFromContainer(Container);
        if (isCtrl) {
            if (!_shouldSkipSelectionToggle) {
                if (Container.IsSelected) editor.Selection.Deselect(index);
                else editor.Selection.Select(index);
            }
        } else if (Container.IsSelected && editor.Selection.Count > 1) {
            editor.Selection.Clear();
            editor.Selection.Select(index);
        }
    }
}

/// <summary>
/// Состояние перетаскивания элемента.
/// </summary>
public class ItemDraggingState : DesignEditorItemState
{
    private Point _previousPosition;
    private readonly Point _initialPosition;
    private Point _elementStartLocation;

    public ItemDraggingState(DesignEditorItem container, Point initialPosition) : base(container)
    {
        _initialPosition = initialPosition;
        _previousPosition = initialPosition;
    }

    public override void Enter(DesignEditorItemState from)
    {
        _elementStartLocation = Container.Location;
        Container.RaiseEvent(new DragStartedEventArgs(_initialPosition.X, _initialPosition.Y) { RoutedEvent = DesignEditorItem.DragStartedEvent });
    }

    public override void Exit()
    {
        var total = _previousPosition - _initialPosition;
        Container.RaiseEvent(new DragCompletedEventArgs(total.X, total.Y, false) { RoutedEvent = DesignEditorItem.DragCompletedEvent });
    }

    public override void OnPointerMoved(PointerEventArgs e)
    {
        var parent = Container.GetVisualParent();
        if (parent == null) return;

        var currentPosition = e.GetPosition((Visual)parent);

        // 1. Считаем полный вектор смещения от точки нажатия
        var totalDelta = currentPosition - _initialPosition;

        // 2. Новая позиция = Старт элемента + Полный вектор
        double newX = Math.Round(_elementStartLocation.X + totalDelta.X);
        double newY = Math.Round(_elementStartLocation.Y + totalDelta.Y);

        Container.Location = new Point(newX, newY);

        var frameDelta = currentPosition - _previousPosition;
        Container.RaiseEvent(new DragDeltaEventArgs(frameDelta.X, frameDelta.Y) { RoutedEvent = DesignEditorItem.DragDeltaEvent });
        _previousPosition = currentPosition;
        e.Handled = true;
    }

    public override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        Container.PopState();
        e.Pointer.Capture(null);
        e.Handled = true;
    }
}

/// <summary>
/// Состояние изменения размера элемента.
/// </summary>
public class ItemResizingState : DesignEditorItemState
{
    private readonly ResizeDirection _direction;
    private Point _initialLocation;
    private Size _initialSize;
    private Vector _accumulatedDelta;

    public ItemResizingState(DesignEditorItem container, ResizeDirection direction) : base(container) => _direction = direction;

    public override void Enter(DesignEditorItemState from)
    {
        _initialLocation = Container.Location;
        _accumulatedDelta = Vector.Zero;

        // Фиксируем размеры перед ресайзом, чтобы избежать схлопывания при Auto
        double w = double.IsNaN(Container.Width) ? Container.Bounds.Width : Container.Width;
        double h = double.IsNaN(Container.Height) ? Container.Bounds.Height : Container.Height;
        Container.Width = w;
        Container.Height = h;
        _initialSize = new Size(w, h);
    }

    public override void OnResizeDelta(ResizeDeltaEventArgs e)
    {
        _accumulatedDelta += e.Delta;

        double newW = _initialSize.Width;
        double newH = _initialSize.Height;
        double newX = _initialLocation.X;
        double newY = _initialLocation.Y;
        double minSize = 10;
        double initialRight = _initialLocation.X + _initialSize.Width;
        double initialBottom = _initialLocation.Y + _initialSize.Height;

        double dx = _accumulatedDelta.X;
        double dy = _accumulatedDelta.Y;

        switch (_direction)
        {
            case ResizeDirection.Right: newW += dx; break;
            case ResizeDirection.Bottom: newH += dy; break;
            case ResizeDirection.Left: newW -= dx; newX += dx; break;
            case ResizeDirection.Top: newH -= dy; newY += dy; break;
            case ResizeDirection.BottomRight: newW += dx; newH += dy; break;
            case ResizeDirection.BottomLeft: newW -= dx; newX += dx; newH += dy; break;
            case ResizeDirection.TopRight: newW += dx; newH -= dy; newY += dy; break;
            case ResizeDirection.TopLeft: newW -= dx; newX += dx; newH -= dy; newY += dy; break;
        }

        newW = Math.Max(minSize, newW);
        newH = Math.Max(minSize, newH);

        if (_direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
            newX = initialRight - newW;

        if (_direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
            newY = initialBottom - newH;

        Container.Width = Math.Round(newW);
        Container.Height = Math.Round(newH);

        // Обновляем позицию только если она изменилась (при ресайзе слева/сверху)
        if (Math.Abs(newX - _initialLocation.X) > 0.1 || Math.Abs(newY - _initialLocation.Y) > 0.1)
        {
            Container.Location = new Point(Math.Round(newX), Math.Round(newY));
        }

        Container.RaiseEvent(new ResizeDeltaEventArgs(e.Delta, _direction, DesignEditorItem.ResizeDeltaEvent));
    }
}
