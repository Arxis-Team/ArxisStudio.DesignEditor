using System;
using Avalonia.Controls;

namespace ArxisStudio;

/// <summary>
/// Определяет уровень выбранного target в редакторе.
/// </summary>
public enum DesignSelectionScope
{
    /// <summary>
    /// Выбран весь контейнер <see cref="DesignEditorItem"/>.
    /// </summary>
    Container = 0,

    /// <summary>
    /// Выбран nested control внутри <see cref="DesignEditorItem"/>.
    /// </summary>
    NestedTarget = 1
}

/// <summary>
/// Представляет публичную запись о выбранном target редактора.
/// </summary>
public sealed class DesignSelectionTarget
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignSelectionTarget"/>.
    /// </summary>
    /// <param name="container">Контейнер, которому принадлежит выбранный target.</param>
    /// <param name="target">Выбранный visual target.</param>
    public DesignSelectionTarget(DesignEditorItem container, Control target)
    {
        Container = container ?? throw new ArgumentNullException(nameof(container));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Scope = ReferenceEquals(container, target) ? DesignSelectionScope.Container : DesignSelectionScope.NestedTarget;
        DisplayName = CreateDisplayName(target);
    }

    /// <summary>
    /// Получает контейнер выбранного target.
    /// </summary>
    public DesignEditorItem Container { get; }

    /// <summary>
    /// Получает выбранный visual target.
    /// </summary>
    public Control Target { get; }

    /// <summary>
    /// Получает уровень выбора: контейнер или nested target.
    /// </summary>
    public DesignSelectionScope Scope { get; }

    /// <summary>
    /// Получает краткое диагностическое имя выбранного target.
    /// </summary>
    public string DisplayName { get; }

    private static string CreateDisplayName(Control control)
    {
        var typeName = control.GetType().Name;
        return !string.IsNullOrWhiteSpace(control.Name)
            ? $"{typeName} ({control.Name})"
            : typeName;
    }
}
