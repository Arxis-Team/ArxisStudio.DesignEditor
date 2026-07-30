using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

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

        // Scope определяется типом самого target, а не совпадением с владельцем:
        // вложенный DesignEditorItem — это контейнер, даже если владеющий item
        // верхнего уровня другой.
        Scope = target is DesignEditorItem
            ? DesignSelectionScope.Container
            : DesignSelectionScope.NestedTarget;

        Depth = CalculateDepth(target);
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
    /// Получает глубину вложенности target в дереве контейнеров.
    /// </summary>
    /// <remarks>
    /// Считается число <see cref="DesignEditorItem"/>-предков строго выше target:
    /// <list type="bullet">
    /// <item><description><c>0</c> — контейнер верхнего уровня;</description></item>
    /// <item><description><c>1</c> — контрол внутри контейнера верхнего уровня либо вложенный контейнер;</description></item>
    /// <item><description><c>2</c> — контрол внутри вложенного контейнера, и так далее.</description></item>
    /// </list>
    /// Вместе со <see cref="Scope"/> однозначно описывает положение target в дереве.
    /// </remarks>
    public int Depth { get; }

    /// <summary>
    /// Получает краткое диагностическое имя выбранного target.
    /// </summary>
    public string DisplayName { get; }

    private static int CalculateDepth(Control target)
    {
        var depth = 0;
        var current = target.GetVisualParent();

        while (current != null)
        {
            if (current is DesignEditorItem)
                depth++;

            current = current.GetVisualParent();
        }

        return depth;
    }

    private static string CreateDisplayName(Control control)
    {
        var typeName = control.GetType().Name;
        return !string.IsNullOrWhiteSpace(control.Name)
            ? $"{typeName} ({control.Name})"
            : typeName;
    }
}
