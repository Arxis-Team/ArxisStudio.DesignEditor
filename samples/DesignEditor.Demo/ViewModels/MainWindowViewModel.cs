using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DesignEditor.Demo.ViewModels;

// Базовый класс для любого элемента на холсте
public partial class DesignItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Location))]
    private double _x;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Location))]
    private double _y;

    // Свойство для быстрого биндинга к IDesignEditorItem.Location
    public Point Location
    {
        get => new Point(X, Y);
        set
        {
            X = value.X;
            Y = value.Y;
            OnPropertyChanged();
        }
    }

    // НОВОЕ: Размеры элемента (Источник правды)
    // Инициализируем дефолтными значениями, чтобы окна не появлялись с size 0,0
    [ObservableProperty] private double _width = 400;
    [ObservableProperty] private double _height = 300;

    protected DesignItemViewModel(double x, double y)
    {
        X = x;
        Y = y;
    }
}

// Конкретные реализации узлов
public class LoginNodeViewModel : DesignItemViewModel
{
    public LoginNodeViewModel(double x, double y) : base(x, y)
    {
        // Можно задать специфичные размеры по умолчанию
        Width = 340;
        Height = 400;
    }
}

public class DashboardNodeViewModel : DesignItemViewModel
{
    public DashboardNodeViewModel(double x, double y) : base(x, y)
    {
        Width = 700;
        Height = 500;
    }
}

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

    [RelayCommand]
    public void ResetZoom()
    {
        Zoom = 1.0;
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
