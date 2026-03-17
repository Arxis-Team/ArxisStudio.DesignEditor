using Avalonia.Controls;
using ArxisStudio;
using DesignEditor.Demo.ViewModels;

namespace DesignEditor.Demo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void CenterActiveItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveItem == null)
            return;

        if (this.FindControl<ArxisStudio.DesignEditor>("Editor") is not { } editor)
            return;

        if (editor.ContainerFromItem(viewModel.ActiveItem) is DesignEditorItem container)
            editor.CenterOnItem(container);
    }
}
