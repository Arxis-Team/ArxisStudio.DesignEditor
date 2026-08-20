using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using DesignGroupAttached = ArxisStudio.Attached.DesignGroup;

namespace ArxisStudio;

// Design-time группы: пометка на контролах, а не узел дерева.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
    /// <summary>
    /// Группа, внутрь которой вошли двойным кликом.
    /// </summary>
    /// <remarks>
    /// Пока группа «открыта», клик выбирает её участника поодиночке, а не всю группу.
    /// Состояние снимается само, как только выбор уходит за пределы этой группы: держать
    /// его до явного выхода значило бы завести режим, из которого пользователь не знает,
    /// как выйти.
    /// </remarks>
    private string? _enteredGroupId;

    /// <summary>
    /// Шов записи принадлежности к группе.
    /// </summary>
    /// <remarks>
    /// Третий шов рядом с <c>SetDesignPosition</c>/<c>SetDesignSize</c> и
    /// <c>SetDesignZIndex</c>, и заведён по той же причине: единственная точка записи —
    /// единственное место, где изменение попадает в контракт.
    /// </remarks>
    private void SetDesignGroup(Control target, string? id)
    {
        if (!_suppressEditRecording)
            _activeEdit?.RecordGroup(this, target, id);

        DesignGroupAttached.SetId(target, id);
    }

    /// <summary>
    /// Задает принадлежность к группе, не создавая новой единицы редактирования.
    /// </summary>
    /// <param name="target">Контрол.</param>
    /// <param name="id">Идентификатор группы или <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="target"/> равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Пара к <see cref="ApplyGeometry"/> и <see cref="ApplyOrder"/>: этим методом отмена
    /// и повтор применяют <see cref="DesignGroupChange"/>, не дописывая стек.
    /// </remarks>
    public void ApplyGroup(Control target, string? id)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        var previous = _suppressEditRecording;
        _suppressEditRecording = true;
        try
        {
            SetDesignGroup(target, id);
        }
        finally
        {
            _suppressEditRecording = previous;
        }

        UpdateSelectionOverlayState();
    }

    /// <summary>
    /// Объединяет выбранные контролы в одну группу.
    /// </summary>
    /// <returns><see langword="true"/>, если группа создана.</returns>
    /// <remarks>
    /// Требуется не меньше двух вложенных target'ов <b>внутри одной формы</b>. Группа
    /// поперёк форм не собирается намеренно: выделение двухслойно, и такая группа
    /// разошлась бы с индексным слоем — рамка обещала бы одно, а жест применялся
    /// к другому.
    /// <para>
    /// Уже сгруппированный участник втягивает в новую группу всю свою прежнюю: иначе
    /// половина старой группы осталась бы со старой пометкой, и на экране появились бы
    /// две рамки там, где пользователь собрал одну.
    /// </para>
    /// </remarks>
    public bool GroupSelection()
    {
        if (!TryCollectGroupCandidates(out var host, out var members) || members.Count < 2)
            return false;

        var id = NextGroupId(host!);

        BeginEdit(DesignEditKind.Group);
        foreach (var member in members)
            SetDesignGroup(member, id);

        CommitEdit();

        _enteredGroupId = null;
        SelectGroupMembers(members);
        return true;
    }

    /// <summary>
    /// Распускает группы, которым принадлежат выбранные контролы.
    /// </summary>
    /// <returns><see langword="true"/>, если хотя бы одна группа была распущена.</returns>
    /// <remarks>
    /// Распускается группа целиком, а не выбранная её часть: группа — это отношение
    /// между участниками, и «разгруппировать половину» означало бы оставить остальных
    /// в группе из одного, то есть ни в какой.
    /// </remarks>
    public bool UngroupSelection()
    {
        var members = CollectGroupedMembersOfSelection();
        if (members.Count == 0)
            return false;

        BeginEdit(DesignEditKind.Group);
        foreach (var member in members)
            SetDesignGroup(member, null);

        CommitEdit();

        _enteredGroupId = null;
        UpdateSelectionOverlayState();
        return true;
    }

    /// <summary>
    /// Возвращает признак того, что выделение можно объединить в группу.
    /// </summary>
    public bool CanGroupSelection() =>
        TryCollectGroupCandidates(out _, out var members) && members.Count >= 2;

    /// <summary>
    /// Возвращает признак того, что в выделении есть что распускать.
    /// </summary>
    public bool CanUngroupSelection() => CollectGroupedMembersOfSelection().Count > 0;

    /// <summary>
    /// Собирает участников будущей группы: только вложенные target'ы одной формы.
    /// </summary>
    private bool TryCollectGroupCandidates(out DesignEditorItem? host, out List<Control> members)
    {
        host = null;
        members = new List<Control>();

        var targets = SelectedDesignTargets;
        if (targets.Count < 2)
            return false;

        foreach (var selected in targets)
        {
            var target = selected.Target;

            // Контейнер формы группировать нечем: он и так рисуется одной рамкой,
            // а его принадлежность к форме задаёт ItemsSource, а не пометка.
            if (target is DesignEditorItem)
                return false;

            var owner = FindDesignHost(target);
            if (owner == null)
                return false;

            if (host == null)
                host = owner;
            else if (!ReferenceEquals(host, owner))
                return false;

            AddDistinct(members, target);
        }

        if (host == null)
            return false;

        // Прежние группы участников входят целиком.
        foreach (var target in members.ToList())
        {
            if (DesignGroupAttached.GetId(target) is not { } existing)
                continue;

            foreach (var mate in EnumerateGroupMembers(host, existing))
                AddDistinct(members, mate);
        }

        return true;
    }

    private List<Control> CollectGroupedMembersOfSelection()
    {
        var members = new List<Control>();

        foreach (var selected in SelectedDesignTargets)
        {
            var target = selected.Target;
            if (DesignGroupAttached.GetId(target) is not { } id)
                continue;

            if (FindDesignHost(target) is not { } host)
                continue;

            foreach (var mate in EnumerateGroupMembers(host, id))
                AddDistinct(members, mate);
        }

        return members;
    }

    private static void AddDistinct(List<Control> members, Control target)
    {
        if (!members.Contains(target))
            members.Add(target);
    }

    /// <summary>
    /// Возвращает группы формы вместе с их составом.
    /// </summary>
    /// <param name="container">Форма, группы которой перечисляются.</param>
    /// <returns>Группы в порядке первого появления участника в разметке.</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="container"/> равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Состав считается по дереву в момент вызова. Кэшировать его редактору нечем:
    /// деревом владеет хост, пометку он вправе поставить в разметке или через
    /// <see cref="Attached.DesignGroup.SetId"/>, и узнать об этом редактору неоткуда —
    /// сохранённый снимок молча устарел бы. По той же причине нет и события об
    /// изменении групп: о своих правках редактор сообщает через
    /// <see cref="EditCompleted"/>, а о чужих сообщить не может.
    /// <para>
    /// Все группы снимаются за один обход: два раздельных запроса дали бы список
    /// идентификаторов и состав, снятые в разные моменты.
    /// </para>
    /// </remarks>
    public IReadOnlyList<DesignGroupInfo> GetGroups(DesignEditorItem container)
    {
        if (container == null)
            throw new ArgumentNullException(nameof(container));

        var order = new List<string>();
        var members = new Dictionary<string, List<Control>>(StringComparer.Ordinal);

        foreach (var candidate in EnumerateGroupCandidates(container))
        {
            if (DesignGroupAttached.GetId(candidate) is not { } id)
                continue;

            if (!members.TryGetValue(id, out var group))
            {
                group = new List<Control>();
                members[id] = group;
                order.Add(id);
            }

            group.Add(candidate);
        }

        var result = new List<DesignGroupInfo>(order.Count);
        foreach (var id in order)
            result.Add(new DesignGroupInfo(container, id, members[id]));

        return result;
    }

    /// <summary>
    /// Возвращает участников одной группы формы.
    /// </summary>
    /// <param name="container">Форма, которой принадлежит группа.</param>
    /// <param name="id">Идентификатор группы.</param>
    /// <returns>Участники в порядке обхода разметки; пустой список, если такой группы нет.</returns>
    /// <exception cref="ArgumentNullException">Выбрасывается, если любой из аргументов равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Неизвестный идентификатор — это ответ, а не ошибка: состав меняется под хостом,
    /// и группа могла быть распущена между двумя его запросами.
    /// </remarks>
    public IReadOnlyList<Control> GetGroupMembers(DesignEditorItem container, string id)
    {
        if (container == null)
            throw new ArgumentNullException(nameof(container));

        if (id == null)
            throw new ArgumentNullException(nameof(id));

        return EnumerateGroupMembers(container, id).ToList();
    }

    /// <summary>
    /// Перечисляет участников группы внутри одной формы.
    /// </summary>
    /// <remarks>
    /// Кандидатов даёт тот же обход, что и выделение: группа не может состоять
    /// из того, что редактор не считает отдельным элементом.
    /// </remarks>
    internal static IEnumerable<Control> EnumerateGroupMembers(DesignEditorItem host, string id)
    {
        foreach (var candidate in EnumerateGroupCandidates(host))
        {
            if (string.Equals(DesignGroupAttached.GetId(candidate), id, StringComparison.Ordinal))
                yield return candidate;
        }
    }

    /// <summary>
    /// Перечисляет то, что вообще может оказаться участником группы.
    /// </summary>
    /// <remarks>
    /// Отбор здесь один на всех потребителей — раскрытие по клику, роспуск, публичное
    /// чтение, — иначе они разошлись бы в понимании состава. Без <c>IsSelectableTarget</c>
    /// участником становился бы контрол, помеченный хостом, но не имеющий designer-метаданных:
    /// клик по соседу раскрывал бы выделение на то, что указатель выбрать не может.
    /// </remarks>
    private static IEnumerable<Control> EnumerateGroupCandidates(DesignEditorItem host)
    {
        foreach (var candidate in EnumerateSelectionCandidates(host))
        {
            if (IsSelectableTarget(candidate, host))
                yield return candidate;
        }
    }

    /// <summary>
    /// Подбирает идентификатор, которого в этой форме ещё нет.
    /// </summary>
    /// <remarks>
    /// Обход здесь <b>не фильтруется</b>, в отличие от состава группы: пометка,
    /// которую редактор не считает элементом, занимает идентификатор ничуть не меньше
    /// видимой. Новая группа, вставшая на занятый номер, слилась бы с ней при первом же
    /// сохранении — и разошлась бы у хоста с тем, что показывает редактор.
    /// </remarks>
    private static string NextGroupId(DesignEditorItem host)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in EnumerateSelectionCandidates(host))
        {
            if (DesignGroupAttached.GetId(candidate) is { } id)
                used.Add(id);
        }

        for (var i = 1; ; i++)
        {
            var id = "group-" + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (used.Add(id))
                return id;
        }
    }

    /// <summary>
    /// Раскрывает target до всех участников его группы.
    /// </summary>
    /// <remarks>
    /// Возвращает сам target, если он не сгруппирован или его группа открыта двойным
    /// кликом. Точка одна: раскрытие обязано совпадать у указателя и у оверлея, иначе
    /// рамка снова обещала бы не то, что применится.
    /// </remarks>
    private IReadOnlyList<Control> ExpandToGroup(DesignEditorItem host, Control target)
    {
        if (DesignGroupAttached.GetId(target) is not { } id)
            return new[] { target };

        if (string.Equals(id, _enteredGroupId, StringComparison.Ordinal))
            return new[] { target };

        var members = EnumerateGroupMembers(host, id).ToList();
        return members.Count > 0 ? members : new List<Control> { target };
    }

    /// <summary>
    /// Открывает группу двойным кликом и закрывает её кликом мимо.
    /// </summary>
    /// <remarks>
    /// Открыть можно только группу, по участнику которой кликнули: двойной клик по
    /// негруппированному контролу ничего не открывает, иначе «внутри» оказывалась бы
    /// группа, которую пользователь в этот момент не трогал.
    /// </remarks>
    private void UpdateEnteredGroup(Control target, int clickCount)
    {
        var id = DesignGroupAttached.GetId(target);

        if (clickCount >= 2 && id != null)
        {
            _enteredGroupId = id;
            return;
        }

        if (!string.Equals(id, _enteredGroupId, StringComparison.Ordinal))
            _enteredGroupId = null;
    }

    /// <summary>
    /// Закрывает открытую группу, если выбор из неё ушёл.
    /// </summary>
    /// <remarks>
    /// Точка одна и стоит на пересборке оверлея: закрывать группу в каждом месте, где
    /// меняется выбор, значило бы перечислить их все — а их два десятка, и клик по
    /// пустому холсту в этом списке уже терялся.
    /// </remarks>
    private void SyncEnteredGroup()
    {
        if (_enteredGroupId == null)
            return;

        foreach (var target in _selectedTargets)
        {
            if (string.Equals(DesignGroupAttached.GetId(target), _enteredGroupId, StringComparison.Ordinal))
                return;
        }

        _enteredGroupId = null;
    }

    /// <summary>
    /// Признак того, что контрол принадлежит открытой группе.
    /// </summary>
    private bool IsInsideEnteredGroup(Control target) =>
        _enteredGroupId != null
        && string.Equals(DesignGroupAttached.GetId(target), _enteredGroupId, StringComparison.Ordinal);

    /// <summary>
    /// Признак того, что выделение — это ровно одна группа целиком.
    /// </summary>
    private bool IsWholeGroupSelected(IReadOnlyList<Control> targets)
    {
        if (targets.Count < 2)
            return false;

        string? id = null;
        foreach (var target in targets)
        {
            if (DesignGroupAttached.GetId(target) is not { } current)
                return false;

            if (id == null)
                id = current;
            else if (!string.Equals(id, current, StringComparison.Ordinal))
                return false;
        }

        return !string.Equals(id, _enteredGroupId, StringComparison.Ordinal);
    }

    private void SelectGroupMembers(IReadOnlyList<Control> members)
    {
        if (members.Count == 0)
            return;

        ApplySelection(members, SelectionIntent.Replace);
    }
}
