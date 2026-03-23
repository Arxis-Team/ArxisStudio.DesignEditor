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
    /// <summary>
    /// Получает контейнер, которому принадлежит состояние.
    /// </summary>
    protected DesignEditorItem Container { get; }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditorItemState"/>.
    /// </summary>
    /// <param name="container">Контейнер, которому принадлежит состояние.</param>
    protected DesignEditorItemState(DesignEditorItem container) => Container = container;

    /// <summary>
    /// Вызывается при входе в состояние.
    /// </summary>
    /// <param name="from">Предыдущее состояние.</param>
    public virtual void Enter(DesignEditorItemState from) { }

    /// <summary>
    /// Вызывается при выходе из состояния.
    /// </summary>
    public virtual void Exit() { }

    /// <summary>
    /// Вызывается при повторном входе в состояние после возврата из вложенного состояния.
    /// </summary>
    /// <param name="from">Состояние, из которого произошел возврат.</param>
    public virtual void ReEnter(DesignEditorItemState from) { }

    /// <summary>
    /// Обрабатывает нажатие указателя.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    public virtual void OnPointerPressed(PointerPressedEventArgs e) { }

    /// <summary>
    /// Обрабатывает перемещение указателя.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    public virtual void OnPointerMoved(PointerEventArgs e) { }

    /// <summary>
    /// Обрабатывает отпускание указателя.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    public virtual void OnPointerReleased(PointerReleasedEventArgs e) { }

    /// <summary>
    /// Обрабатывает изменение размера.
    /// </summary>
    /// <param name="e">Аргументы изменения размера.</param>
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

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ItemIdleState"/>.
    /// </summary>
    /// <param name="container">Контейнер, которому принадлежит состояние.</param>
    public ItemIdleState(DesignEditorItem container) : base(container) { }

    /// <inheritdoc />
    public override void ReEnter(DesignEditorItemState from)
    {
        _isPressed = false;
        _shouldSkipSelectionToggle = false;
    }

    /// <inheritdoc />
    public override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(Container).Properties;
        if (props.IsLeftButtonPressed)
        {
            e.Pointer.Capture(Container);
            e.Handled = true;
            _isPressed = true;

            var editor = Container.FindAncestorOfType<DesignEditor>();
            var parent = Container.GetVisualParent();
            if (editor != null)
                _startPoint = e.GetPosition(editor);
            else if (parent != null)
                _startPoint = e.GetPosition((Visual)parent);

            HandleSelectionOnPress(e);
        }
    }

    /// <inheritdoc />
    public override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_isPressed || !Container.IsDraggable) return;

        var parent = Container.GetVisualParent();
        if (parent == null) return;

        // ВАЖНО: Разрешаем драг, если родитель - AbsolutePanel (в т.ч. DesignSurface) или Canvas.
        bool isAbsolute = parent is AbsolutePanel || parent is Canvas;
        if (!isAbsolute) return;

        var editor = Container.FindAncestorOfType<DesignEditor>();
        var currentPoint = editor != null
            ? e.GetPosition(editor)
            : e.GetPosition((Visual)parent);
        var dragStartThreshold = Math.Max(0.0, editor?.InteractionOptions.DragStartThreshold ?? 3.0);
        if (Vector.Distance(_startPoint, currentPoint) > dragStartThreshold)
        {
            if (editor != null && editor.ShouldBlockNestedGroupDrag())
            {
                e.Handled = true;
                return;
            }

            Container.PushState(new ItemDraggingState(Container, _startPoint));
        }
    }

    /// <inheritdoc />
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
        editor.SetLastInputModifiers(e.KeyModifiers);
        bool isAdditive = editor.ShouldUseAdditiveSelection(e.KeyModifiers);
        bool isContainerInteraction = editor.ShouldUseContainerInteraction(e.KeyModifiers);
        if (isAdditive && !isContainerInteraction && !editor.CanAddNestedTargetToContainer(Container))
        {
            _shouldSkipSelectionToggle = true;
            return;
        }

        if (!Container.IsSelected)
        {
            if (!isAdditive) editor.Selection.Clear();
            editor.Selection.Select(editor.IndexFromContainer(Container));
            _shouldSkipSelectionToggle = true;
        } else _shouldSkipSelectionToggle = false;

        editor.UpdateSelectionTargetFromPoint(Container, e.GetPosition(editor), e.KeyModifiers);
    }

    private void HandleSelectionOnRelease(PointerReleasedEventArgs e)
    {
        var editor = Container.FindAncestorOfType<DesignEditor>();
        if (editor == null) return;
        bool isAdditive = editor.ShouldUseAdditiveSelection(e.KeyModifiers);
        bool isContainerInteraction = editor.ShouldUseContainerInteraction(e.KeyModifiers);
        var index = editor.IndexFromContainer(Container);
        if (isAdditive && !isContainerInteraction)
        {
            // Additive click should never remove selection from already selected item.
            // This keeps primary target/adorner stable when retargeting nested controls.
            if (!_shouldSkipSelectionToggle && !Container.IsSelected)
                editor.Selection.Select(index);
        }
        else if (!isAdditive && Container.IsSelected && editor.Selection.Count > 1)
        {
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
    private Point _previousPointerPosition;
    private readonly Point _initialPointerPosition;
    private Point _elementStartLocation;
    private Control _dragTarget = null!;
    private Vector _previousAppliedDelta;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ItemDraggingState"/>.
    /// </summary>
    /// <param name="container">Контейнер, который перетаскивается.</param>
    /// <param name="initialPointerPosition">Начальная позиция указателя в координатах редактора.</param>
    public ItemDraggingState(DesignEditorItem container, Point initialPointerPosition) : base(container)
    {
        _initialPointerPosition = initialPointerPosition;
        _previousPointerPosition = initialPointerPosition;
    }

    /// <inheritdoc />
    public override void Enter(DesignEditorItemState from)
    {
        var editor = Container.FindAncestorOfType<DesignEditor>();
        _dragTarget = editor?.ResolveInteractionTarget(Container) ?? Container;
        _elementStartLocation = editor?.GetDesignPosition(_dragTarget) ?? Container.Location;
        _previousAppliedDelta = Vector.Zero;
        Container.RaiseEvent(new DragStartedEventArgs(_initialPointerPosition.X, _initialPointerPosition.Y) { RoutedEvent = DesignEditorItem.DragStartedEvent });
    }

    /// <inheritdoc />
    public override void Exit()
    {
        var editor = Container.FindAncestorOfType<DesignEditor>();
        var total = editor != null
            ? new Point(_previousAppliedDelta.X, _previousAppliedDelta.Y)
            : _previousPointerPosition - _initialPointerPosition;
        Container.RaiseEvent(new DragCompletedEventArgs(total.X, total.Y, false) { RoutedEvent = DesignEditorItem.DragCompletedEvent });
    }

    /// <inheritdoc />
    public override void OnPointerMoved(PointerEventArgs e)
    {
        var editor = Container.FindAncestorOfType<DesignEditor>();
        if (editor != null)
        {
            var currentPointerPosition = e.GetPosition(editor);
            var rawTotalDelta = editor.GetWorldPosition(currentPointerPosition) - editor.GetWorldPosition(_initialPointerPosition);
            var appliedTotalDelta = editor.ApplyMovePolicy(_dragTarget, rawTotalDelta);

            double newX = Math.Round(_elementStartLocation.X + appliedTotalDelta.X);
            double newY = Math.Round(_elementStartLocation.Y + appliedTotalDelta.Y);
            editor.SetDesignPosition(_dragTarget, new Point(newX, newY));
            var frameDelta = appliedTotalDelta - _previousAppliedDelta;
            Container.RaiseEvent(new DragDeltaEventArgs(frameDelta.X, frameDelta.Y) { RoutedEvent = DesignEditorItem.DragDeltaEvent });
            _previousAppliedDelta = appliedTotalDelta;
            _previousPointerPosition = currentPointerPosition;
            e.Handled = true;
            return;
        }

        var parent = Container.GetVisualParent();
        if (parent == null) return;

        var currentPointerPositionFallback = e.GetPosition((Visual)parent);
        var totalDeltaFallback = currentPointerPositionFallback - _initialPointerPosition;
        double fallbackX = Math.Round(_elementStartLocation.X + totalDeltaFallback.X);
        double fallbackY = Math.Round(_elementStartLocation.Y + totalDeltaFallback.Y);
        Container.Location = new Point(fallbackX, fallbackY);

        var frameDeltaFallback = currentPointerPositionFallback - _previousPointerPosition;
        Container.RaiseEvent(new DragDeltaEventArgs(frameDeltaFallback.X, frameDeltaFallback.Y) { RoutedEvent = DesignEditorItem.DragDeltaEvent });
        _previousPointerPosition = currentPointerPositionFallback;
        e.Handled = true;
    }

    /// <inheritdoc />
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
    private readonly Control _target;
    private readonly ResizeDirection _direction;
    private Point _initialLocation;
    private Size _initialSize;
    private Vector _accumulatedDelta;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="ItemResizingState"/>.
    /// </summary>
    /// <param name="container">Контейнер, размер которого изменяется.</param>
    /// <param name="target">Visual target, к которому применяется изменение размера.</param>
    /// <param name="direction">Направление активной ручки изменения размера.</param>
    public ItemResizingState(DesignEditorItem container, Control target, ResizeDirection direction) : base(container)
    {
        _target = target;
        _direction = direction;
    }

    /// <inheritdoc />
    public override void Enter(DesignEditorItemState from)
    {
        var editor = Container.FindAncestorOfType<DesignEditor>();
        _initialLocation = editor?.GetDesignPosition(_target) ?? Container.Location;
        _accumulatedDelta = Vector.Zero;

        var size = editor?.GetDesignSize(_target) ?? new Size(
            double.IsNaN(Container.Width) ? Container.Bounds.Width : Container.Width,
            double.IsNaN(Container.Height) ? Container.Bounds.Height : Container.Height);

        editor?.SetDesignSize(_target, size);
        _initialSize = size;
    }

    /// <inheritdoc />
    public override void OnResizeDelta(ResizeDeltaEventArgs e)
    {
        _accumulatedDelta += e.Delta;

        double newW = _initialSize.Width;
        double newH = _initialSize.Height;
        double newX = _initialLocation.X;
        double newY = _initialLocation.Y;
        var editor = Container.FindAncestorOfType<DesignEditor>();
        double editorMinSize = Math.Max(0.0, editor?.InteractionOptions.ResizeMinSize ?? 10.0);
        double minWidth = Math.Max(editorMinSize, _target.MinWidth);
        double minHeight = Math.Max(editorMinSize, _target.MinHeight);
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

        newW = Math.Max(minWidth, newW);
        newH = Math.Max(minHeight, newH);

        if (_direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
            newX = initialRight - newW;

        if (_direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
            newY = initialBottom - newH;

        if (editor != null)
            editor.SetDesignSize(_target, new Size(newW, newH));
        else
        {
            Container.Width = newW;
            Container.Height = newH;
        }

        // Обновляем позицию только если она изменилась (при ресайзе слева/сверху)
        if (Math.Abs(newX - _initialLocation.X) > 0.1 || Math.Abs(newY - _initialLocation.Y) > 0.1)
        {
            if (editor != null)
                editor.SetDesignPosition(_target, new Point(newX, newY));
            else
                Container.Location = new Point(newX, newY);
        }

        Container.RaiseEvent(new ResizeDeltaEventArgs(e.Delta, _direction, DesignEditorItem.ResizeDeltaEvent));
    }
}
