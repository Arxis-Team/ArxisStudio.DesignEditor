using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DesignEditor.Demo.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public ObservableCollection<DesignItemViewModel> Nodes { get; } = new();

    // Коллекция выделенных элементов (Avalonia биндит сюда object)
    [ObservableProperty]
    private ObservableCollection<object> _selectedNodes = new();

    // НОВОЕ: Активный элемент (первый из выделенных) для отображения в панели свойств
    // Вычисляемое свойство, которое обновляется при изменении SelectedNodes
    public DesignItemViewModel? ActiveItem => SelectedNodes.FirstOrDefault() as DesignItemViewModel;
    public bool HasSelection => SelectedNodes.Count > 0;

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
        Nodes.Add(new LoginNodeViewModel(400, 300));
        Nodes.Add(new DashboardNodeViewModel(800, 300));

        // ВАЖНО: Подписываемся на изменение выделения, чтобы обновлять UI (ActiveItem)
        SelectedNodes.CollectionChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(ActiveItem));
            OnPropertyChanged(nameof(HasSelection));
        };
    }
}
