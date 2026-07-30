using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Закрепляет публичную поверхность библиотеки.
/// </summary>
/// <remarks>
/// Тест не защищает конкретный список — он делает его расширение осознанным.
/// Новый публичный тип роняет тест, и автор обязан либо добавить его сюда,
/// приняв на себя обязательство поддерживать, либо сделать тип internal.
/// <para>
/// Машины состояний (<c>EditorState</c>, <c>DesignEditorItemState</c> и наследники)
/// и детали overlay (<c>SelectionAdornerLayer</c>, <c>SelectionAdornerInfo</c>)
/// намеренно отсутствуют: это реализация, а не контракт.
/// </para>
/// </remarks>
public class PublicSurfaceTests
{
    private static readonly string[] Expected =
    {
        // Редактор и контейнер элемента
        "ArxisStudio.DesignEditor",
        "ArxisStudio.DesignEditorItem",

        // Контролы, используемые в шаблонах и темах
        "ArxisStudio.Controls.AbsolutePanel",
        "ArxisStudio.Controls.DesignGrid",
        "ArxisStudio.Controls.SelectionAdorner",
        "ArxisStudio.Controls.SelectionAdornerRole",
        "ArxisStudio.Controls.ResizeDirection",
        "ArxisStudio.Controls.ResizeDeltaEventArgs",
        "ArxisStudio.Controls.ResizeStartedEventArgs",

        // Позиционирование и политики редактирования
        "ArxisStudio.Attached.Layout",
        "ArxisStudio.Attached.DesignInteraction",
        "ArxisStudio.Attached.MovePolicy",
        "ArxisStudio.Attached.ResizePolicy",

        // Настройка ввода
        "ArxisStudio.DesignEditorInputGestures",
        "ArxisStudio.DesignEditorPointerButton",
        "ArxisStudio.ContainerEmptyAreaDragGesture",
        "ArxisStudio.DesignEditorInteractionOptions",

        // Выделение
        "ArxisStudio.DesignSelectionTarget",
        "ArxisStudio.DesignSelectionScope",
        "ArxisStudio.DesignSelectionChangedEventArgs",

        // Контракт изменений
        "ArxisStudio.DesignChange",
        "ArxisStudio.DesignGeometryChange",
        "ArxisStudio.DesignOrderChange",
        "ArxisStudio.DesignEditKind",
        "ArxisStudio.DesignEditCompletedEventArgs",
        "ArxisStudio.DesignEditorDeleteRequestedEventArgs",

        // События перетаскивания контейнера
        "ArxisStudio.DragStartedEventArgs",
        "ArxisStudio.DragDeltaEventArgs",
        "ArxisStudio.DragCompletedEventArgs",

        // Контекстные действия
        "ArxisStudio.IDesignEditorContextActionProvider",
        "ArxisStudio.IDesignEditorContextPresenter",
        "ArxisStudio.ContextMenuContextPresenter",
        "ArxisStudio.DesignEditorContextAction",
        "ArxisStudio.DesignEditorContextRequest",
        "ArxisStudio.DesignEditorContextScope",
        "ArxisStudio.DesignEditorContextSource",
        "ArxisStudio.DesignEditorContextRequestingEventArgs",
        "ArxisStudio.DesignEditorContextRequestedEventArgs"
    };

    /// <summary>
    /// Фактическая поверхность без типов, сгенерированных компилятором AXAML.
    /// </summary>
    /// <remarks>
    /// Компилятор Avalonia добавляет в сборку публичные <c>CompiledAvaloniaXaml.*</c>
    /// — они не являются частью API и авторами не контролируются.
    /// </remarks>
    private static List<string> ActualSurface() =>
        typeof(DesignEditor).Assembly
            .GetExportedTypes()
            .Select(type => type.FullName!)
            .Where(name => !name.StartsWith("CompiledAvaloniaXaml", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

    [Fact]
    public void Public_Surface_Matches_The_Agreed_List()
    {
        var actual = ActualSurface();
        var expected = Expected.OrderBy(name => name, StringComparer.Ordinal).ToList();

        var added = actual.Except(expected).ToList();
        var missing = expected.Except(actual).ToList();

        Assert.True(
            added.Count == 0,
            "Публичная поверхность расширилась. Либо внесите типы в список, либо сделайте их internal:\n"
                + string.Join("\n", added));

        Assert.True(
            missing.Count == 0,
            "Типы исчезли из публичной поверхности — это ломающее изменение:\n"
                + string.Join("\n", missing));
    }

    [Fact]
    public void State_Machines_Are_Not_Public()
    {
        var leaked = ActualSurface()
            .Where(name => name.StartsWith("ArxisStudio.States.", StringComparison.Ordinal))
            .ToList();

        Assert.True(leaked.Count == 0, "Машины состояний должны быть internal:\n" + string.Join("\n", leaked));
    }

    [Fact]
    public void Overlay_Implementation_Is_Not_Public()
    {
        var leaked = ActualSurface()
            .Where(name => name.Contains("SelectionAdornerLayer", StringComparison.Ordinal)
                           || name.Contains("SelectionAdornerInfo", StringComparison.Ordinal)
                           || name.Contains("DesignSurface", StringComparison.Ordinal))
            .ToList();

        Assert.True(leaked.Count == 0, "Детали overlay должны быть internal:\n" + string.Join("\n", leaked));
    }
}
