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
internal abstract class DesignEditorItemState
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
internal class ItemIdleState : DesignEditorItemState
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
            var owningEditor = Container.FindAncestorOfType<DesignEditor>();

            // Жест по пустой области может принадлежать рамке выделения.
            // Тогда контейнер не захватывает указатель и не помечает событие
            // обработанным — нажатие всплывает до редактора, который сам решит.
            if (owningEditor != null &&
                owningEditor.ShouldDeferPressToMarquee(Container, e.GetPosition(owningEditor), e.KeyModifiers))
            {
                return;
            }

            e.Pointer.Capture(Container);
            e.Handled = true;
            _isPressed = true;

            var editor = owningEditor;
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

        var editor = Container.FindAncestorOfType<DesignEditor>();
        if (editor != null)
        {
            // Спрашиваем не про родителя контейнера, а про фактическую цель жеста:
            // двигаться будет именно она, и решает её собственная раскладка.
            // Прежний гейт проверял не тот уровень и поэтому пропускал жесты,
            // которые затем молча ничего не делали.
            var moveTarget = editor.ResolveInteractionTarget(Container);
            if (editor.GetEffectiveMovePolicy(moveTarget) == ArxisStudio.Attached.MovePolicy.None)
                return;
        }
        else if (parent is not AbsolutePanel)
        {
            // Без редактора остаётся только fallback-ветка, а она пишет
            // Container.Location, которую читает лишь AbsolutePanel.
            return;
        }

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

        // Контейнер может быть вложен в другой контейнер. Индексный выбор работает
        // только с item'ами верхнего уровня, поэтому selection адресуется владельцу,
        // а сам Container участвует дальше как design target.
        var owner = editor.ResolveOwningItem(Container);
        if (owner == null) return;

        editor.SetLastInputModifiers(e.KeyModifiers);
        bool isAdditive = editor.ShouldUseAdditiveSelection(e.KeyModifiers);
        bool isContainerInteraction = editor.ShouldUseContainerInteraction(e.KeyModifiers);
        if (isAdditive && !isContainerInteraction && !editor.CanAddNestedTargetToContainer(owner))
        {
            _shouldSkipSelectionToggle = true;
            return;
        }

        if (!owner.IsSelected)
        {
            if (!isAdditive) editor.Selection.Clear();
            editor.Selection.Select(editor.IndexFromContainer(owner));
            _shouldSkipSelectionToggle = true;
        } else _shouldSkipSelectionToggle = false;

        editor.UpdateSelectionTargetFromPoint(owner, e.GetPosition(editor), e.KeyModifiers);
    }

    private void HandleSelectionOnRelease(PointerReleasedEventArgs e)
    {
        var editor = Container.FindAncestorOfType<DesignEditor>();
        if (editor == null) return;

        var owner = editor.ResolveOwningItem(Container);
        if (owner == null) return;

        bool isAdditive = editor.ShouldUseAdditiveSelection(e.KeyModifiers);
        bool isContainerInteraction = editor.ShouldUseContainerInteraction(e.KeyModifiers);
        var index = editor.IndexFromContainer(owner);
        if (isAdditive && !isContainerInteraction)
        {
            // Additive click should never remove selection from already selected item.
            // This keeps primary target/adorner stable when retargeting nested controls.
            if (!_shouldSkipSelectionToggle && !owner.IsSelected)
                editor.Selection.Select(index);
        }
        else if (!isAdditive && owner.IsSelected && editor.Selection.Count > 1)
        {
            editor.Selection.Clear();
            editor.Selection.Select(index);
        }
    }
}

/// <summary>
/// Состояние перетаскивания элемента.
/// </summary>
internal class ItemDraggingState : DesignEditorItemState
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

            // Привязывается результат, а не дельта: иначе элемент сохранил бы
            // исходное смещение относительно сетки и на узел бы не встал.
            var snapped = editor.SnapPosition(_elementStartLocation + appliedTotalDelta, e.KeyModifiers);

            // Дальше по группе идёт уже привязанное смещение, поэтому соседи
            // двигаются ровно на столько же и взаимное расположение сохраняется.
            var effectiveDelta = snapped - _elementStartLocation;

            editor.SetDesignPosition(_dragTarget, snapped);
            var frameDelta = effectiveDelta - _previousAppliedDelta;
            Container.RaiseEvent(new DragDeltaEventArgs(frameDelta.X, frameDelta.Y) { RoutedEvent = DesignEditorItem.DragDeltaEvent });
            _previousAppliedDelta = effectiveDelta;
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
internal class ItemResizingState : DesignEditorItemState
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

        // Привязывается двигающийся край, а не размер: иначе у элемента,
        // стоящего мимо сетки, край так и остался бы вне узла.
        // Модификатор читается на момент нажатия — в аргументах resize его нет.
        if (editor != null && editor.ShouldSnap(editor.LastInputModifiers))
        {
            if (_direction is ResizeDirection.Right or ResizeDirection.TopRight or ResizeDirection.BottomRight)
                newW = editor.SnapCoordinate(newX + newW) - newX;
            else if (_direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
                newW = initialRight - editor.SnapCoordinate(initialRight - newW);

            if (_direction is ResizeDirection.Bottom or ResizeDirection.BottomLeft or ResizeDirection.BottomRight)
                newH = editor.SnapCoordinate(newY + newH) - newY;
            else if (_direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
                newH = initialBottom - editor.SnapCoordinate(initialBottom - newH);
        }

        // Приведение к Min/Max делается здесь, а не только внутри SetDesignSize:
        // неподвижный край считается из итогового размера, иначе он уплывал бы
        // на разницу между запрошенным и разрешённым.
        if (editor != null)
        {
            // Ограничение по форме применяется до Min/Max: минимум обязан
            // побеждать, иначе контрол схлопнулся бы в ноль у края формы.
            if (editor.TryGetContainmentBounds(_target, out var limit))
            {
                if (ChangesWidth(_direction))
                {
                    var maxW = _direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft
                        ? initialRight - limit.Left
                        : limit.Right - newX;

                    if (maxW > 0)
                        newW = Math.Min(newW, maxW);
                }

                if (ChangesHeight(_direction))
                {
                    var maxH = _direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight
                        ? initialBottom - limit.Top
                        : limit.Bottom - newY;

                    if (maxH > 0)
                        newH = Math.Min(newH, maxH);
                }
            }

            var coerced = editor.CoerceDesignSize(_target, new Size(newW, newH));
            newW = coerced.Width;
            newH = coerced.Height;
        }
        else
        {
            newW = Math.Max(minWidth, newW);
            newH = Math.Max(minHeight, newH);
        }

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

    /// <summary>
    /// Определяет, меняет ли направление ширину.
    /// </summary>
    /// <remarks>
    /// Ограничение применяется только к той оси, которую тянут: иначе оно задним
    /// числом ужимало бы уже вылезший контрол при перетаскивании соседнего края.
    /// </remarks>
    private static bool ChangesWidth(ResizeDirection direction)
        => direction is ResizeDirection.Left or ResizeDirection.Right
            or ResizeDirection.TopLeft or ResizeDirection.TopRight
            or ResizeDirection.BottomLeft or ResizeDirection.BottomRight;

    /// <summary>
    /// Определяет, меняет ли направление высоту.
    /// </summary>
    private static bool ChangesHeight(ResizeDirection direction)
        => direction is ResizeDirection.Top or ResizeDirection.Bottom
            or ResizeDirection.TopLeft or ResizeDirection.TopRight
            or ResizeDirection.BottomLeft or ResizeDirection.BottomRight;
}
