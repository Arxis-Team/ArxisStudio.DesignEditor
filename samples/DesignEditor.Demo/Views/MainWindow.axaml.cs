using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
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

            // Редактор не владеет коллекцией, поэтому Delete приходит запросом.
            editor.DeleteRequested += Editor_OnDeleteRequested;

            // Деревом контролов он тоже не владеет: перестановка — структурная
            // правка, и её выполняет владелец разметки. Здесь эту роль временно
            // играет само демо, в продукте её возьмёт ArxisStudio.Markup.
            editor.ReorderRequested += Editor_OnReorderRequested;

            // Отмена строится поверх DesignEditor.EditCompleted и ApplyGeometry.
            _history = new EditHistory(editor);
            _history.Changed += (_, _) => UpdateHistoryButtons();
            UpdateHistoryButtons();
        }

        if (this.FindControl<ComboBox>("GridStepBox") is { } gridStep)
        {
            gridStep.SelectionChanged += (_, _) => ApplyGridCellSize();
            ApplyGridCellSize();
        }
    }

    /// <summary>
    /// Применяет выбранный шаг сетки.
    /// </summary>
    /// <remarks>
    /// Шаг задаётся ресурсом, а не свойством редактора: так его подхватывает
    /// <c>ControlTheme</c> сетки, и привязка следует за ним сама — по умолчанию
    /// <c>InteractionOptions.SnapStep</c> равен <see cref="double.NaN"/>, то есть
    /// «брать шаг у сетки». Одна настройка меняет и то, что нарисовано,
    /// и то, к чему притягивается.
    /// </remarks>
    private void ApplyGridCellSize()
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        Resources["DesignEditor.Grid.CellSize"] = viewModel.GridCellSize;
    }

    private void UpdateHistoryButtons()
    {
        if (this.FindControl<Button>("UndoButton") is { } undo)
            undo.IsEnabled = _history?.CanUndo ?? false;

        if (this.FindControl<Button>("RedoButton") is { } redo)
            redo.IsEnabled = _history?.CanRedo ?? false;
    }

    private void Editor_OnDeleteRequested(object? sender, DesignEditorDeleteRequestedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
            return;

        if (this.FindControl<ArxisStudio.DesignEditor>("Editor") is not { } editor)
            return;

        // Удаляем от больших индексов к меньшим, иначе последующие съезжают.
        var indexes = e.Targets
            .Select(target => editor.IndexFromContainer(target.Container))
            .Where(index => index >= 0)
            .Distinct()
            .OrderByDescending(index => index)
            .ToList();

        if (indexes.Count == 0)
            return;

        foreach (var index in indexes)
            viewModel.Elements.RemoveAt(index);

        e.Handled = true;
    }

    private void Editor_OnReorderRequested(object? sender, DesignEditorReorderRequestedEventArgs e)
    {
        if (e.Target.GetVisualParent() is not Panel panel)
            return;

        panel.Children.Move(e.OldIndex, e.NewIndex);
        e.Handled = true;
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
