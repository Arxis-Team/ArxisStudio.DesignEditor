using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ArxisStudio;
using DesignEditor.Demo.ViewModels;
using Editor = ArxisStudio.DesignEditor;

namespace DesignEditor.Demo.Views;

/// <summary>
/// Панель слоёв: формы, их группы и участники групп.
/// </summary>
/// <remarks>
/// Панель не знает ни одного внутреннего типа библиотеки: состав читается
/// <see cref="Editor.GetGroups"/>, подписи строятся публичным
/// <see cref="DesignSelectionTarget"/>, выбор задаётся
/// <see cref="Editor.SelectDesignTarget"/>, а группировка — <see cref="Editor.GroupSelection"/>
/// и <see cref="Editor.UngroupSelection"/>.
/// <para>
/// Показываются <b>группы</b>, а не всё дерево контролов, и это не упрощение: публично
/// редактор отвечает на вопрос про группы, а перечислить всё, что он считает элементом,
/// снаружи нельзя. Панель показывает ровно то, на что API отвечает.
/// </para>
/// <para>
/// Чтение групп — запрос, а не снимок: редактор не владеет деревом и не может сообщить
/// о пометке, которую поставил не он. Поэтому панель пересобирается на событиях самого
/// редактора (<see cref="Editor.DesignSelectionChanged"/> и <see cref="Editor.EditCompleted"/>),
/// а на всё остальное есть кнопка обновления — это честная цена pull-модели, а не недоделка.
/// </para>
/// </remarks>
public partial class LayersPanel : UserControl
{
    /// <summary>Идентификатор свойства редактора.</summary>
    public static readonly StyledProperty<Editor?> EditorProperty =
        AvaloniaProperty.Register<LayersPanel, Editor?>(nameof(Editor));

    /// <summary>Идентификатор свойства доступности группировки.</summary>
    public static readonly StyledProperty<bool> CanGroupProperty =
        AvaloniaProperty.Register<LayersPanel, bool>(nameof(CanGroup));

    /// <summary>Идентификатор свойства доступности роспуска.</summary>
    public static readonly StyledProperty<bool> CanUngroupProperty =
        AvaloniaProperty.Register<LayersPanel, bool>(nameof(CanUngroup));

    /// <summary>Свёрнутые узлы; переживает пересборку списка.</summary>
    private readonly HashSet<string> _collapsed = new(System.StringComparer.Ordinal);

    private Editor? _attached;
    private bool _builtOnce;

    /// <summary>Инициализирует панель.</summary>
    public LayersPanel()
    {
        // Панель — сама себе модель: строки она строит из редактора, а данных окна
        // ей не нужно. Привязка Editor приходит снаружи по имени элемента и от
        // DataContext не зависит.
        DataContext = this;
        InitializeComponent();
    }

    /// <summary>Получает или задает редактор, чьи группы показывает панель.</summary>
    public Editor? Editor
    {
        get => GetValue(EditorProperty);
        set => SetValue(EditorProperty, value);
    }

    /// <summary>Получает признак того, что выделение можно сгруппировать.</summary>
    public bool CanGroup
    {
        get => GetValue(CanGroupProperty);
        private set => SetValue(CanGroupProperty, value);
    }

    /// <summary>Получает признак того, что в выделении есть что распустить.</summary>
    public bool CanUngroup
    {
        get => GetValue(CanUngroupProperty);
        private set => SetValue(CanUngroupProperty, value);
    }

    /// <summary>Строки панели в порядке показа.</summary>
    public ObservableCollection<LayerNode> Nodes { get; } = new();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    /// <inheritdoc />
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == EditorProperty)
            Attach(change.GetNewValue<Editor?>());
    }

    private void Attach(Editor? editor)
    {
        if (_attached != null)
        {
            _attached.DesignSelectionChanged -= OnDesignSelectionChanged;
            _attached.EditCompleted -= OnEditCompleted;
            _attached.LayoutUpdated -= OnEditorLayoutUpdated;
        }

        _attached = editor;
        _builtOnce = false;

        if (_attached == null)
        {
            Nodes.Clear();
            return;
        }

        _attached.DesignSelectionChanged += OnDesignSelectionChanged;
        _attached.EditCompleted += OnEditCompleted;

        // Контейнеры существуют только после layout-прохода, поэтому первая сборка
        // ждёт его: до него ContainerFromIndex вернёт null и панель окажется пустой.
        _attached.LayoutUpdated += OnEditorLayoutUpdated;
    }

    private void OnEditorLayoutUpdated(object? sender, System.EventArgs e)
    {
        if (_builtOnce)
            return;

        _builtOnce = true;
        Rebuild();
    }

    private void OnDesignSelectionChanged(object? sender, DesignSelectionChangedEventArgs e) => Rebuild();

    private void OnEditCompleted(object? sender, DesignEditCompletedEventArgs e) => Rebuild();

    private void Refresh_OnClick(object? sender, RoutedEventArgs e) => Rebuild();

    private void Group_OnClick(object? sender, RoutedEventArgs e)
    {
        _attached?.GroupSelection();
        Rebuild();
    }

    private void Ungroup_OnClick(object? sender, RoutedEventArgs e)
    {
        _attached?.UngroupSelection();
        Rebuild();
    }

    /// <summary>
    /// Пересобирает список по текущему состоянию редактора.
    /// </summary>
    private void Rebuild()
    {
        if (_attached is not { } editor)
            return;

        var selected = new HashSet<Control>();
        foreach (var target in editor.SelectedDesignTargets)
            selected.Add(target.Target);

        Nodes.Clear();

        for (var index = 0; index < editor.ItemCount; index++)
        {
            if (editor.ContainerFromIndex(index) is not DesignEditorItem container)
                continue;

            var groups = editor.GetGroups(container);
            var formKey = "form:" + index;
            var formExpanded = !_collapsed.Contains(formKey);

            Nodes.Add(new LayerNode(LayerNodeKind.Form, formKey, TitleOf(editor, index, container), depth: 0)
            {
                Container = container,
                HasChildren = true,
                IsExpanded = formExpanded,
                IsSelected = selected.Contains(container)
            });

            if (!formExpanded)
                continue;

            if (groups.Count == 0)
            {
                Nodes.Add(new LayerNode(LayerNodeKind.Hint, formKey + ":empty", "групп нет", depth: 1));
                continue;
            }

            foreach (var group in groups)
                AddGroup(container, group, formKey, selected);
        }

        CanGroup = editor.CanGroupSelection();
        CanUngroup = editor.CanUngroupSelection();
    }

    private void AddGroup(
        DesignEditorItem container,
        DesignGroupInfo group,
        string formKey,
        HashSet<Control> selected)
    {
        var key = formKey + ":" + group.Id;
        var expanded = !_collapsed.Contains(key);

        // Группа считается выбранной, когда выбраны все её участники: рамка у неё
        // одна, и половинчатая подсветка описывала бы состояние, которого нет.
        var wholeGroupSelected = group.Members.Count > 0;
        foreach (var member in group.Members)
        {
            if (!selected.Contains(member))
            {
                wholeGroupSelected = false;
                break;
            }
        }

        Nodes.Add(new LayerNode(LayerNodeKind.Group, key, group.Id, depth: 1)
        {
            Container = container,
            GroupId = group.Id,
            MemberCount = group.Members.Count,
            HasChildren = true,
            IsExpanded = expanded,
            IsSelected = wholeGroupSelected
        });

        if (!expanded)
            return;

        foreach (var member in group.Members)
        {
            Nodes.Add(new LayerNode(LayerNodeKind.Member, key + ":" + Nodes.Count, TitleOf(container, member), depth: 2)
            {
                Container = container,
                Target = member,
                GroupId = group.Id,
                IsSelected = selected.Contains(member)
            });
        }
    }

    /// <summary>
    /// Имя участника: как называет его редактор, плюс подпись, если она есть.
    /// </summary>
    /// <remarks>
    /// Основа берётся у публичного <see cref="DesignSelectionTarget"/> — своя схема имён
    /// разошлась бы с тем, что редактор пишет о выбранном. Но у неразмеченной кнопки имени
    /// нет, и три подряд «Button» в списке не различить, поэтому к типу добавляется её
    /// собственный текст — то же, что видно на макете.
    /// </remarks>
    private static string TitleOf(DesignEditorItem container, Control member)
    {
        var name = new DesignSelectionTarget(container, member).DisplayName;

        var caption = member switch
        {
            TextBlock text => text.Text,
            ContentControl { Content: string content } => content,
            _ => null
        };

        return string.IsNullOrWhiteSpace(caption) ? name : name + " «" + caption.Trim() + "»";
    }

    private static string TitleOf(Editor editor, int index, DesignEditorItem container)
    {
        if (editor.Items[index] is DesignItemViewModel item)
            return item.Title;

        return new DesignSelectionTarget(container, container).DisplayName;
    }

    private void Row_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: LayerNode node } || _attached is not { } editor)
            return;

        var point = e.GetCurrentPoint(sender as Visual);
        if (!point.Properties.IsLeftButtonPressed)
            return;

        // Стрелка раскрытия занимает первые пиксели строки — клик по ней сворачивает,
        // а не выбирает: то же деление, что в любом дереве.
        if (node.HasChildren && point.Position.X <= node.Indent.Left + 14)
        {
            Toggle(node);
            e.Handled = true;
            return;
        }

        var additive = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (node.Kind)
        {
            case LayerNodeKind.Form when node.Container != null:
                editor.SelectDesignTarget(node.Container, additive);
                break;

            case LayerNodeKind.Member when node.Target != null:
                editor.SelectDesignTarget(node.Target, additive);
                break;

            case LayerNodeKind.Group when node.Container != null && node.GroupId != null:
                SelectGroup(editor, node);
                break;
        }

        e.Handled = true;
    }

    /// <summary>
    /// Выбирает группу целиком.
    /// </summary>
    /// <remarks>
    /// Раскрытие клика до всей группы — поведение указателя над холстом; публичный
    /// <see cref="Editor.SelectDesignTarget"/> работает с одним target'ом, поэтому
    /// панель набирает участников сама: первый заменяет выбор, остальные добавляются.
    /// </remarks>
    private static void SelectGroup(Editor editor, LayerNode node)
    {
        var members = editor.GetGroupMembers(node.Container!, node.GroupId!);
        for (var i = 0; i < members.Count; i++)
            editor.SelectDesignTarget(members[i], additive: i > 0);
    }

    private void Toggle(LayerNode node)
    {
        if (!_collapsed.Add(node.Key))
            _collapsed.Remove(node.Key);

        Rebuild();
    }
}
