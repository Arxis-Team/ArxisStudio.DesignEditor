using System;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio.Attached;

/// <summary>
/// Политика изменения размера design target.
/// </summary>
[Flags]
public enum ResizePolicy
{
    /// <summary>
    /// Изменение размера запрещено.
    /// </summary>
    None = 0,

    /// <summary>
    /// Разрешено изменение размера по левой стороне.
    /// </summary>
    Left = 1 << 0,

    /// <summary>
    /// Разрешено изменение размера по верхней стороне.
    /// </summary>
    Top = 1 << 1,

    /// <summary>
    /// Разрешено изменение размера по правой стороне.
    /// </summary>
    Right = 1 << 2,

    /// <summary>
    /// Разрешено изменение размера по нижней стороне.
    /// </summary>
    Bottom = 1 << 3,

    /// <summary>
    /// Разрешено изменение размера только по горизонтали.
    /// </summary>
    Horizontal = Left | Right,

    /// <summary>
    /// Разрешено изменение размера только по вертикали.
    /// </summary>
    Vertical = Top | Bottom,

    /// <summary>
    /// Разрешено изменение размера по всем сторонам.
    /// </summary>
    All = Left | Top | Right | Bottom
}

/// <summary>
/// Политика перемещения design target.
/// </summary>
[Flags]
public enum MovePolicy
{
    /// <summary>
    /// Перемещение запрещено.
    /// </summary>
    None = 0,

    /// <summary>
    /// Разрешено перемещение по оси X.
    /// </summary>
    X = 1 << 0,

    /// <summary>
    /// Разрешено перемещение по оси Y.
    /// </summary>
    Y = 1 << 1,

    /// <summary>
    /// Разрешено перемещение по обеим осям.
    /// </summary>
    Both = X | Y
}

/// <summary>
/// Attached API политики редактирования для designer targets.
/// </summary>
public static class DesignInteraction
{
    /// <summary>
    /// Идентификатор attached-свойства политики resize.
    /// </summary>
    public static readonly AttachedProperty<ResizePolicy> ResizePolicyProperty =
        AvaloniaProperty.RegisterAttached<Control, ResizePolicy>(
            "ResizePolicy",
            typeof(DesignInteraction),
            ResizePolicy.All,
            inherits: false);

    /// <summary>
    /// Идентификатор attached-свойства политики перемещения.
    /// </summary>
    public static readonly AttachedProperty<MovePolicy> MovePolicyProperty =
        AvaloniaProperty.RegisterAttached<Control, MovePolicy>(
            "MovePolicy",
            typeof(DesignInteraction),
            MovePolicy.Both,
            inherits: false);

    /// <summary>
    /// Возвращает policy изменения размера для target.
    /// </summary>
    public static ResizePolicy GetResizePolicy(AvaloniaObject target) => target.GetValue(ResizePolicyProperty);

    /// <summary>
    /// Задает policy изменения размера для target.
    /// </summary>
    public static void SetResizePolicy(AvaloniaObject target, ResizePolicy value) => target.SetValue(ResizePolicyProperty, value);

    /// <summary>
    /// Возвращает policy перемещения для target.
    /// </summary>
    public static MovePolicy GetMovePolicy(AvaloniaObject target) => target.GetValue(MovePolicyProperty);

    /// <summary>
    /// Задает policy перемещения для target.
    /// </summary>
    public static void SetMovePolicy(AvaloniaObject target, MovePolicy value) => target.SetValue(MovePolicyProperty, value);
}
