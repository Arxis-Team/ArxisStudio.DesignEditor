using Avalonia;
using Avalonia.Input;

namespace ArxisStudio.States;

/// <summary>
/// Состояние панорамирования viewport редактора.
/// </summary>
public class EditorPanningState : EditorState
{
    private Point _startMousePosition;
    private Point _startViewportLocation;

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="EditorPanningState"/>.
    /// </summary>
    /// <param name="editor">Редактор, которому принадлежит состояние.</param>
    public EditorPanningState(DesignEditor editor) : base(editor) { }

    public override void Enter(EditorState? from)
    {
        // Захватываем начальные координаты
        // Используем GetPosition(Editor), так как это экранные координаты относительно контрола
        // (до применения трансформаций зума к содержимому)
        _startMousePosition = Editor.GetPositionForInput(Editor);
        _startViewportLocation = Editor.ViewportLocation;

        Editor.Cursor = new Cursor(StandardCursorType.Hand);
        // Захват мыши происходит автоматически в Avalonia при нажатии,
        // но можно явно указать Capture, если нужно гарантировать поведение.
        // В данном контексте полагаемся на обработку PointerMoved.
    }

    public override void Exit()
    {
        Editor.Cursor = Cursor.Default;
    }

    public override void OnPointerMoved(PointerEventArgs e)
    {
        var currentMousePos = e.GetPosition(Editor);
        Vector diffScreen = _startMousePosition - currentMousePos;

        // Обновляем ViewportLocation.
        // Делим на Zoom, так как смещение мыши в экранных пикселях,
        // а ViewportLocation — в координатах холста.
        Editor.ViewportLocation = _startViewportLocation + (diffScreen / Editor.ViewportZoom);
    }

    public override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // Возвращаемся в Idle при отпускании кнопки
        Editor.PopState();
    }

    public override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // Разрешаем зум даже во время панорамирования (как в Google Maps)
        Editor.HandleZoom(e);
    }
}
