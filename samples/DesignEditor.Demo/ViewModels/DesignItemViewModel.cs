using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DesignEditor.Demo.ViewModels;

public partial class DesignItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Location))]
    private double _x;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Location))]
    private double _y;

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

    [ObservableProperty] private double _width = 400;
    [ObservableProperty] private double _height = 300;

    protected DesignItemViewModel(double x, double y)
    {
        X = x;
        Y = y;
    }
}
