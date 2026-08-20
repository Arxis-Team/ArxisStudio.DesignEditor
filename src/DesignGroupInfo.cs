using System;
using System.Collections.Generic;
using Avalonia.Controls;

namespace ArxisStudio;

/// <summary>
/// Представляет design-time группу и её состав внутри одной формы.
/// </summary>
/// <remarks>
/// Снимок, снятый в момент запроса: группа — это пометка на контролах
/// (<see cref="Attached.DesignGroup"/>), а деревом владеет хост, поэтому редактор
/// не может ни закэшировать состав, ни узнать о правке, которой не делал.
/// Отсюда и способ получения — запрос <see cref="DesignEditor.GetGroups"/>, а не
/// свойство со снимком, как у выделения.
/// <para>
/// Идентификатор уникален <b>в пределах контейнера</b>: <c>group-1</c> в двух разных
/// формах — это две разные группы. Поэтому ключом служит пара
/// <see cref="Container"/> + <see cref="Id"/>, и <see cref="Container"/> входит в
/// сам объект, а не подразумевается.
/// </para>
/// </remarks>
public sealed class DesignGroupInfo
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignGroupInfo"/>.
    /// </summary>
    /// <param name="container">Форма, которой принадлежит группа.</param>
    /// <param name="id">Идентификатор группы.</param>
    /// <param name="members">Участники группы в порядке обхода разметки.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если любой из аргументов равен <see langword="null"/>.</exception>
    public DesignGroupInfo(DesignEditorItem container, string id, IReadOnlyList<Control> members)
    {
        Container = container ?? throw new ArgumentNullException(nameof(container));
        Id = id ?? throw new ArgumentNullException(nameof(id));
        Members = members ?? throw new ArgumentNullException(nameof(members));
    }

    /// <summary>
    /// Получает форму, которой принадлежит группа.
    /// </summary>
    public DesignEditorItem Container { get; }

    /// <summary>
    /// Получает идентификатор группы.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Получает участников группы в порядке обхода разметки.
    /// </summary>
    /// <remarks>
    /// Порядок — тот же, которым редактор перечисляет кандидатов на выбор, то есть
    /// порядок разметки, а не порядок, в котором участников выделили. Список слоёв
    /// у хоста не должен переставляться от вызова к вызову.
    /// </remarks>
    public IReadOnlyList<Control> Members { get; }
}
