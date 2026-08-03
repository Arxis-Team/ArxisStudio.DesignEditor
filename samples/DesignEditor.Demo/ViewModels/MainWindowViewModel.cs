using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DesignEditor.Demo.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public ObservableCollection<DesignItemViewModel> Elements { get; } = new();

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
    public IReadOnlyList<double> GridCellSizes { get; } = new double[] { 5, 10, 20, 25, 40, 50 };

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
