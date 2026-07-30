using System.Collections.Generic;
using ArxisStudio;

namespace DesignEditor.Demo;

/// <summary>
/// Стек отмены поверх контракта изменений редактора.
/// </summary>
/// <remarks>
/// Намеренно живёт в демо, а не в библиотеке: редактор отдаёт поток изменений,
/// а как их хранить и когда объединять — решает приложение. Здесь показан
/// минимальный вариант: две стопки и применение геометрии через
/// <see cref="ArxisStudio.DesignEditor.ApplyGeometry"/>.
/// </remarks>
public sealed class EditHistory
{
    private readonly ArxisStudio.DesignEditor _editor;
    private readonly Stack<DesignEditCompletedEventArgs> _undo = new();
    private readonly Stack<DesignEditCompletedEventArgs> _redo = new();

    public EditHistory(ArxisStudio.DesignEditor editor)
    {
        _editor = editor;
        _editor.EditCompleted += OnEditCompleted;
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    /// <summary>
    /// Возникает при изменении доступности отмены и повтора.
    /// </summary>
    public event System.EventHandler? Changed;

    public void Undo()
    {
        if (_undo.Count == 0)
            return;

        var edit = _undo.Pop();
        foreach (var change in edit.Changes)
            _editor.ApplyGeometry(change.Target, change.OldBounds);

        _redo.Push(edit);
        Changed?.Invoke(this, System.EventArgs.Empty);
    }

    public void Redo()
    {
        if (_redo.Count == 0)
            return;

        var edit = _redo.Pop();
        foreach (var change in edit.Changes)
            _editor.ApplyGeometry(change.Target, change.NewBounds);

        _undo.Push(edit);
        Changed?.Invoke(this, System.EventArgs.Empty);
    }

    private void OnEditCompleted(object? sender, DesignEditCompletedEventArgs e)
    {
        // ApplyGeometry не поднимает EditCompleted, поэтому отмена сюда не возвращается
        // и очистка redo безопасна.
        _undo.Push(e);
        _redo.Clear();
        Changed?.Invoke(this, System.EventArgs.Empty);
    }
}
