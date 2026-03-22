using System.Collections.ObjectModel;
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

    public MainWindowViewModel()
    {
        // Заполняем демо-данными
        Elements.Add(new LoginElementViewModel(400, 300));
        Elements.Add(new DashboardElementViewModel(800, 300));

        // ВАЖНО: Подписываемся на изменение выделения, чтобы обновлять UI (ActiveItem)
        SelectedElements.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(ActiveItem));
            OnPropertyChanged(nameof(HasSelection));
        };
    }
}
