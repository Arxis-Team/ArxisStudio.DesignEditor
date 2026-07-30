using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio;

/// <summary>
/// Определяет вид завершённого изменения в редакторе.
/// </summary>
public enum DesignEditKind
{
    /// <summary>
    /// Перемещение одного или нескольких targets.
    /// </summary>
    Move,

    /// <summary>
    /// Изменение размера одного или нескольких targets.
    /// </summary>
    Resize
}

/// <summary>
/// Аргументы запроса на удаление выделения.
/// </summary>
/// <remarks>
/// Редактор не владеет коллекцией элементов — она приходит через <c>ItemsSource</c>,
/// поэтому удалять он не может и не должен. Клавиша Delete превращается в запрос,
/// который выполняет приложение. Пока запрос не помечен <see cref="Handled"/>,
/// нажатие считается необработанным и продолжает всплывать.
/// </remarks>
public sealed class DesignEditorDeleteRequestedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditorDeleteRequestedEventArgs"/>.
    /// </summary>
    /// <param name="targets">Выделенные targets на момент запроса.</param>
    public DesignEditorDeleteRequestedEventArgs(IReadOnlyList<DesignSelectionTarget> targets)
    {
        Targets = targets ?? throw new ArgumentNullException(nameof(targets));
    }

    /// <summary>
    /// Получает выделенные targets на момент запроса.
    /// </summary>
    public IReadOnlyList<DesignSelectionTarget> Targets { get; }

    /// <summary>
    /// Получает или задает признак того, что удаление выполнено приложением.
    /// </summary>
    public bool Handled { get; set; }
}

/// <summary>
/// Описывает изменение геометрии одного design target.
/// </summary>
/// <remarks>
/// Границы заданы в design-координатах: тех же, в которых работают
/// <c>Layout.DesignX</c>/<c>DesignY</c> и <see cref="DesignEditor.SelectionBounds"/>.
/// Их достаточно, чтобы вернуть target в прежнее состояние через
/// <see cref="DesignEditor.ApplyGeometry"/>.
/// </remarks>
public sealed class DesignGeometryChange
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignGeometryChange"/>.
    /// </summary>
    /// <param name="target">Изменённый контрол.</param>
    /// <param name="oldBounds">Геометрия до изменения.</param>
    /// <param name="newBounds">Геометрия после изменения.</param>
    public DesignGeometryChange(Control target, Rect oldBounds, Rect newBounds)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        OldBounds = oldBounds;
        NewBounds = newBounds;
    }

    /// <summary>
    /// Получает изменённый контрол.
    /// </summary>
    public Control Target { get; }

    /// <summary>
    /// Получает геометрию до изменения.
    /// </summary>
    public Rect OldBounds { get; }

    /// <summary>
    /// Получает геометрию после изменения.
    /// </summary>
    public Rect NewBounds { get; }
}

/// <summary>
/// Аргументы завершённой единицы редактирования.
/// </summary>
/// <remarks>
/// Событие возникает один раз на жест целиком: перетаскивание пяти элементов
/// даёт одну запись с пятью изменениями, а не пять записей и не по одной на кадр.
/// Именно эта гранулярность нужна стеку undo.
/// <para>
/// Библиотека стек не ведёт — это состояние приложения. Она отвечает за то,
/// чтобы поток изменений был полным и правильно сгруппированным.
/// </para>
/// </remarks>
public sealed class DesignEditCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditCompletedEventArgs"/>.
    /// </summary>
    /// <param name="kind">Вид изменения.</param>
    /// <param name="changes">Изменения геометрии, вошедшие в единицу редактирования.</param>
    public DesignEditCompletedEventArgs(DesignEditKind kind, IReadOnlyList<DesignGeometryChange> changes)
    {
        Kind = kind;
        Changes = changes ?? throw new ArgumentNullException(nameof(changes));
    }

    /// <summary>
    /// Получает вид изменения.
    /// </summary>
    public DesignEditKind Kind { get; }

    /// <summary>
    /// Получает изменения геометрии, вошедшие в единицу редактирования.
    /// </summary>
    /// <remarks>
    /// Содержит только те targets, геометрия которых действительно изменилась:
    /// жест, вернувший элемент на исходное место, записи не создаёт.
    /// </remarks>
    public IReadOnlyList<DesignGeometryChange> Changes { get; }
}
