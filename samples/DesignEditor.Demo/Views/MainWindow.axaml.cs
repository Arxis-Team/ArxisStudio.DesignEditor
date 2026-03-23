using Avalonia.Controls;
using ArxisStudio;
using DesignEditor.Demo.Context;
using DesignEditor.Demo.ViewModels;

namespace DesignEditor.Demo.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        if (this.FindControl<ArxisStudio.DesignEditor>("Editor") is { } editor)
            editor.ContextActionProviders.Add(new DesignEditorDemoContextActionsProvider());
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

    private void FitActiveItem_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel || viewModel.ActiveItem == null)
            return;

        if (this.FindControl<ArxisStudio.DesignEditor>("Editor") is not { } editor)
            return;

        if (editor.ContainerFromItem(viewModel.ActiveItem) is DesignEditorItem container)
            editor.FitToView(container);
    }

    private void CenterSelection_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.FindControl<ArxisStudio.DesignEditor>("Editor") is { } editor)
            editor.CenterOnSelection();
    }

    private void FitSelection_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (this.FindControl<ArxisStudio.DesignEditor>("Editor") is { } editor)
            editor.FitSelectionToView();
    }
}
