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

    public override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(Editor).Properties;
        var modifiers = e.KeyModifiers;

        // 1. Панорамирование (Средняя кнопка ИЛИ Alt + Левая)
        if (props.IsMiddleButtonPressed || (props.IsLeftButtonPressed && modifiers.HasFlag(KeyModifiers.Alt)))
        {
            Editor.PushState(new EditorPanningState(Editor));
            return;
        }

        // 2. Выделение (Левая кнопка по пустому месту)
        // Проверяем, что клик не пришелся на дочерний элемент (DesignEditorItem)
        var source = e.Source as Visual;
        var itemContainer = source?.FindAncestorOfType<DesignEditorItem>();

        if (props.IsLeftButtonPressed && itemContainer == null)
        {
            Editor.PushState(new EditorSelectingState(Editor));
            return;
        }

        // Если кликнули по элементу DesignEditorItem, событие уйдет к нему (в его ItemIdleState),
        // так как DesignEditorItem находится выше в визуальном дереве и обработает Bubble событие,
        // либо мы не ставим e.Handled, и оно дойдет сюда, но мы его игнорируем.
    }

    public override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        // Зум работает всегда, даже в Idle
        Editor.HandleZoom(e);
    }
}
