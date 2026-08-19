using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DesignEditor.Demo.ViewModels;

/// <summary>
/// Строка в подсказке о жестах: сочетание и что оно делает.
/// </summary>
/// <param name="Keys">Сочетание, как его нажимает пользователь.</param>
/// <param name="Description">Что происходит.</param>
public sealed record GestureHint(string Keys, string Description);

public partial class MainWindowViewModel : ObservableObject
{
    public ObservableCollection<DesignItemViewModel> Elements { get; } = new();

    /// <summary>
    /// Жесты редактора для панели подсказок.
    /// </summary>
    /// <remarks>
    /// Список лежит здесь, а не в разметке, ровно потому, что рукописная сетка от него
    /// отстала: каждая строка стоила восемнадцати строк XAML и ручной нумерации
    /// <c>Grid.Row</c>, и дописать в неё жест было дороже, чем не дописать. Порядок —
    /// от указателя к клавиатуре, как ими и пользуются.
    /// <para>
    /// Модификаторы здесь названы теми, что задаёт демо (<c>InputGestures</c> в разметке)
    /// и что стоит по умолчанию у самого редактора: <c>Alt</c> — обход привязки,
    /// <c>Ctrl</c> — работа с контейнером, <c>Shift</c> — добавление к выделению.
    /// </para>
    /// </remarks>
    public IReadOnlyList<GestureHint> Gestures { get; } = new GestureHint[]
    {
        new("Left Click", "Выбрать вложенный контрол"),
        new("Double Click", "Войти в группу и выбрать её участника"),
        new("Shift + Click", "Добавить вложенный контрол в той же форме"),
        new("Right Click", "Контекстное меню"),
        new("Left Drag", "Тянуть выделение или рамку по пустой области"),
        new("Drag ручки", "Изменить размер выделения"),
        new("Alt + Drag", "Вести без привязки к сетке и направляющим"),
        new("Middle Drag", "Панорамирование viewport"),
        new("Wheel", "Масштабирование viewport"),
        new("Ctrl + Click", "Выбрать DesignEditorItem"),
        new("Ctrl + Drag", "Переместить выбранный DesignEditorItem"),
        new("Ctrl + Shift", "Добавить item или рамочное выделение контейнеров"),
        new("Drag с линейки", "Вытянуть направляющую"),
        new("Drag линии", "Переместить направляющую; увести за край — убрать"),
        new("← ↑ → ↓", "Сместить выделение на шаг"),
        new("Shift + ← ↑ → ↓", "Сместить крупным шагом"),
        new("Ctrl + Z", "Отменить"),
        new("Ctrl + X", "Повторить"),
        new("Delete", "Удалить выбранные элементы"),
        new("Esc / Ctrl + A", "Снять выделение / выбрать все"),
    };

    /// <summary>
    /// Пользовательские направляющие макета.
    /// </summary>
    /// <remarks>
    /// Набором владеет хост, поэтому он и живёт здесь, а не в редакторе. Три линии
    /// поставлены по краям колонок экранов — к ним удобно подтягивать элементы,
    /// не выцеливая координату мышью.
    ///
    /// Показ по умолчанию выключен (<c>ShowGuides="False"</c> в разметке): набор
    /// остаётся, но пустой холст встречает пользователя без лишних линий. Включается
    /// переключателем <c>guides</c> в шапке.
    /// </remarks>
    public ObservableCollection<ArxisStudio.DesignGuide> Guides { get; } = new()
    {
        ArxisStudio.DesignGuide.Vertical(560),
        ArxisStudio.DesignGuide.Vertical(1180),
        ArxisStudio.DesignGuide.Horizontal(420),
    };

    // Коллекция выделенных элементов (Avalonia биндит сюда object)
    [ObservableProperty]
    private ObservableCollection<object> _selectedElements = new();

    // НОВОЕ: Активный элемент (первый из выделенных) для отображения в панели свойств
    // Вычисляемое свойство, которое обновляется при изменении SelectedElements
    public DesignItemViewModel? ActiveItem => SelectedElements.FirstOrDefault() as DesignItemViewModel;
    public bool HasSelection => SelectedElements.Count > 0;

    [ObservableProperty]
    private double _zoom = 1.0;

    partial void OnIsGestureHelpExpandedChanged(bool value)
    {
        OnPropertyChanged(nameof(IsGestureHelpCollapsed));
    }

    [ObservableProperty]
    private bool _isGestureHelpExpanded = true;

    public bool IsGestureHelpCollapsed => !IsGestureHelpExpanded;

    [RelayCommand]
    public void ResetZoom()
    {
        Zoom = 1.0;
    }

    [RelayCommand]
    public void ToggleGestureHelp()
    {
        IsGestureHelpExpanded = !IsGestureHelpExpanded;
    }

    /// <summary>
    /// Шаги сетки, доступные в верхней панели.
    /// </summary>
    /// <remarks>
    /// Шаг 1 — это привязка к целым единицам, то есть практически свободное
    /// размещение. Сама сетка при этом на обычном масштабе не рисуется: у неё есть
    /// порог детализации (<c>DesignEditor.Grid.MinCellSize</c>), и клетка мельче него
    /// превратилась бы в сплошную заливку. С приближением она возвращается крупными
    /// линиями: на 500 % видно шаг в пять единиц. Притяжение при этом работает
    /// независимо от показа — как и при выключенном <c>ShowGrid</c>.
    /// </remarks>
    public IReadOnlyList<double> GridCellSizes { get; } = new double[] { 1, 5, 10, 20, 25, 40, 50 };

    [ObservableProperty]
    private double _gridCellSize = 20;

    public MainWindowViewModel()
    {
        // Заполняем демо-данными
        // Экраны и формы будущего приложения: каждый лежит в своём контейнере,
        // а корень его разметки — обычная панель Avalonia, поэтому редактор сам
        // определяет, что с ним можно делать.
        Elements.Add(new HomeScreenViewModel(80, 80));
        Elements.Add(new ThermostatScreenViewModel(820, 80));
        Elements.Add(new AddDeviceDialogViewModel(1180, 80));
        Elements.Add(new AutomationScreenViewModel(820, 580));
        Elements.Add(new ComponentLibraryViewModel(80, 580));

        SelectedElements.CollectionChanged += OnSelectedElementsCollectionChanged;
    }

    // Подписка переезжает вместе с коллекцией: раньше она ставилась один раз
    // в конструкторе, и замена SelectedElements через сеттер тихо оставляла
    // ActiveItem/HasSelection без обновлений.
    partial void OnSelectedElementsChanged(
        ObservableCollection<object>? oldValue,
        ObservableCollection<object> newValue)
    {
        if (oldValue != null)
            oldValue.CollectionChanged -= OnSelectedElementsCollectionChanged;

        newValue.CollectionChanged += OnSelectedElementsCollectionChanged;
        OnSelectedElementsCollectionChanged(newValue, null!);
    }

    private void OnSelectedElementsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs? e)
    {
        OnPropertyChanged(nameof(ActiveItem));
        OnPropertyChanged(nameof(HasSelection));
    }
}
