using Avalonia;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace ArxisStudio.States;

/// <summary>
/// Состояние ожидания, в котором редактор обрабатывает старт выделения, панорамирования и зума.
/// </summary>
public class EditorIdleState : EditorState
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="EditorIdleState"/>.
    /// </summary>
    /// <param name="editor">Редактор, которому принадлежит состояние.</param>
    public EditorIdleState(DesignEditor editor) : base(editor) { }

    /// <inheritdoc />
    public override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(Editor).Properties;
        var modifiers = e.KeyModifiers;

        if (Editor.ShouldStartPan(props, modifiers))
        {
            Editor.PushState(new EditorPanningState(Editor));
            return;
        }

        var source = e.Source as Visual;
        var itemContainer = source?.FindAncestorOfType<DesignEditorItem>();

        if (itemContainer == null && Editor.ShouldStartMarquee(props, modifiers))
        {
            Editor.PushState(new EditorSelectingState(Editor));
            return;
        }
    }

    /// <inheritdoc />
    public override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // Зум работает всегда, даже в Idle
        Editor.HandleZoom(e);
    }
}
