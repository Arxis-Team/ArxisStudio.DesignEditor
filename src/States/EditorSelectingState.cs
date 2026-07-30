using System;
using Avalonia;
using Avalonia.Input;

namespace ArxisStudio.States;

/// <summary>
/// Состояние прямоугольного выделения элементов редактора.
/// </summary>
public class EditorSelectingState : EditorState
{
    private Point _startLocationWorld;

    /// <summary>
    /// Режим рамки, зафиксированный в момент нажатия.
    /// </summary>
    /// <remarks>
    /// Модификатор читается один раз: жест не должен менять смысл, если пользователь
    /// отпустит или нажмёт клавишу посреди протяжки.
    /// </remarks>
    private bool _useContainerSelection;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="EditorSelectingState"/>.
    /// </summary>
    /// <param name="editor">Редактор, которому принадлежит состояние.</param>
    public EditorSelectingState(DesignEditor editor) : base(editor) { }

    /// <inheritdoc />
    public override void Enter(EditorState? from)
    {
        // 1. Включаем режим отрисовки рамки в Editor
        Editor.IsSelecting = true;
        _useContainerSelection = Editor.ShouldUseContainerInteraction(Editor.LastInputModifiers);

        // 2. Запоминаем точку старта в МИРОВЫХ координатах (с учетом зума и пана)
        _startLocationWorld = Editor.GetWorldPosition(Editor.GetPositionForInput(Editor));

        // 3. Сбрасываем текущее выделение, если не активен additive selection.
        var modifiers = Editor.LastInputModifiers;
        if (!Editor.ShouldUseAdditiveSelection(modifiers))
        {
            Editor.SelectedItem = null; // Сброс одиночного выделения
            Editor.Selection.Clear();   // Сброс множественного
        }

        Editor.SelectedArea = new Rect(_startLocationWorld, new Size(0, 0));
        Editor.UpdateMarqueeScope(Editor.SelectedArea, _useContainerSelection);
    }

    /// <inheritdoc />
    public override void Exit()
    {
        Editor.IsSelecting = false;
        Editor.SelectedArea = new Rect(0, 0, 0, 0);
        Editor.ClearMarqueeScope();
    }

    /// <inheritdoc />
    public override void OnPointerMoved(PointerEventArgs e)
    {
        Point currentMousePosWorld = Editor.GetWorldPosition(e.GetPosition(Editor));

        double x = Math.Min(_startLocationWorld.X, currentMousePosWorld.X);
        double y = Math.Min(_startLocationWorld.Y, currentMousePosWorld.Y);
        double w = Math.Abs(_startLocationWorld.X - currentMousePosWorld.X);
        double h = Math.Abs(_startLocationWorld.Y - currentMousePosWorld.Y);

        Editor.SelectedArea = new Rect(x, y, w, h);

        // Область действия следует за прямоугольником, а не за точкой нажатия:
        // по MarqueeScope можно показывать целевой контейнер прямо во время жеста.
        Editor.UpdateMarqueeScope(Editor.SelectedArea, _useContainerSelection);
    }

    /// <inheritdoc />
    public override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // Применяем выделение. Режим берём зафиксированный, а не текущий.
        Editor.CommitSelection(
            Editor.SelectedArea,
            Editor.ShouldUseAdditiveSelection(e.KeyModifiers),
            _useContainerSelection);

        // Возвращаемся в Idle
        Editor.PopState();
    }
}
