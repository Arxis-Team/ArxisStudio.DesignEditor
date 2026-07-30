using Avalonia.Controls;
using ArxisStudio;
using DesignEditor.Demo.Context;
using DesignEditor.Demo.ViewModels;

namespace DesignEditor.Demo.Views;

public partial class MainWindow : Window
{
    private EditHistory? _history;

    public MainWindow()
    {
        InitializeComponent();

        if (this.FindControl<ArxisStudio.DesignEditor>("Editor") is { } editor)
        {
            editor.ContextActionProviders.Add(new DesignEditorDemoContextActionsProvider());

            // Отмена строится поверх DesignEditor.EditCompleted и ApplyGeometry.
            _history = new EditHistory(editor);
            _history.Changed += (_, _) => UpdateHistoryButtons();
            UpdateHistoryButtons();
        }
    }

    private void UpdateHistoryButtons()
    {
        if (this.FindControl<Button>("UndoButton") is { } undo)
            undo.IsEnabled = _history?.CanUndo ?? false;

        if (this.FindControl<Button>("RedoButton") is { } redo)
            redo.IsEnabled = _history?.CanRedo ?? false;
    }

    private void Undo_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _history?.Undo();

    private void Redo_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e) => _history?.Redo();

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
