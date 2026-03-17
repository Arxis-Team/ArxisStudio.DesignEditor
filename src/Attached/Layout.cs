using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ArxisStudio.Controls;

namespace ArxisStudio.Attached;

/// <summary>
/// Предоставляет присоединенные свойства для позиционирования элементов на поверхности редактора.
/// </summary>
/// <remarks>
/// Класс синхронизирует локальные координаты элемента относительно родительской панели
/// с глобальными координатами относительно корневой поверхности дизайна.
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <TextBlock attached:Layout.X="120"
///            attached:Layout.Y="80"
///            Text="Label" />
/// ]]></code>
/// </example>
public static class Layout
{
    private static readonly AttachedProperty<bool> IsUpdatingPositionProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "IsUpdatingPosition", typeof(Layout), false, inherits: false);

    #region Attached Properties

    /// <summary>
    /// Идентификатор присоединенного свойства локальной координаты X.
    /// </summary>
    public static readonly AttachedProperty<double> XProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "X", typeof(Layout), double.NaN, inherits: false, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Идентификатор присоединенного свойства локальной координаты Y.
    /// </summary>
    public static readonly AttachedProperty<double> YProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "Y", typeof(Layout), double.NaN, inherits: false, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Идентификатор присоединенного свойства глобальной координаты X.
    /// </summary>
    public static readonly AttachedProperty<double> DesignXProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "DesignX", typeof(Layout), 0d, inherits: false, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Идентификатор присоединенного свойства глобальной координаты Y.
    /// </summary>
    public static readonly AttachedProperty<double> DesignYProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "DesignY", typeof(Layout), 0d, inherits: false, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Идентификатор присоединенного свойства, принудительно включающего отслеживание позиции.
    /// </summary>
    public static readonly AttachedProperty<bool> IsTrackedProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>(
            "IsTracked", typeof(Layout), false, inherits: false);

    #endregion

    static Layout()
    {
        // При изменении локальных координат (X/Y) автоматически начинаем отслеживание.
        XProperty.Changed.AddClassHandler<Control>((s, e) => Track(s));
        YProperty.Changed.AddClassHandler<Control>((s, e) => Track(s));

        // Управление подпиской через свойство IsTracked.
        IsTrackedProperty.Changed.AddClassHandler<Control>((s, e) =>
        {
            if (e.NewValue is true) Track(s);
            else Untrack(s);
        });

        // Обратная связь: при изменении глобальных координат пересчитываем локальные.
        DesignXProperty.Changed.AddClassHandler<Control>((s, e) => OnDesignPositionChanged(s));
        DesignYProperty.Changed.AddClassHandler<Control>((s, e) => OnDesignPositionChanged(s));
    }

    /// <summary>
    /// Включает отслеживание положения элемента для автоматического расчета глобальных координат.
    /// </summary>
    /// <param name="control">Элемент, для которого включается отслеживание.</param>
    public static void Track(Control? control)
    {
        if (control == null) return;

        control.LayoutUpdated -= OnLayoutUpdated;
        control.LayoutUpdated += OnLayoutUpdated;

        UpdateDesignPosition(control);
    }

    /// <summary>
    /// Отключает отслеживание положения элемента.
    /// </summary>
    /// <param name="control">Элемент, для которого отключается отслеживание.</param>
    public static void Untrack(Control? control)
    {
        if (control == null) return;
        control.LayoutUpdated -= OnLayoutUpdated;
    }

    private static void OnLayoutUpdated(object? sender, EventArgs e)
    {
        if (sender is Control control)
        {
            UpdateDesignPosition(control);
        }
    }

    /// <summary>
    /// Вычисляет глобальное положение элемента и обновляет DesignX/DesignY.
    /// </summary>
    private static void UpdateDesignPosition(Control control)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (GetIsUpdatingPosition(control)) return;
            SetIsUpdatingPosition(control, true);
            try
            {
                Visual? reference = control.FindAncestorOfType<DesignSurface>()
                                    ?? control.FindAncestorOfType<DesignEditor>() as Visual;

                if (reference != null)
                {
                    var position = control.TranslatePoint(new Point(0, 0), reference);

                    if (position.HasValue)
                    {
                        if (Math.Abs(GetDesignX(control) - position.Value.X) > 0.01)
                            SetDesignX(control, position.Value.X);

                        if (Math.Abs(GetDesignY(control) - position.Value.Y) > 0.01)
                            SetDesignY(control, position.Value.Y);
                    }
                }
            }
            finally { SetIsUpdatingPosition(control, false); }
        }, DispatcherPriority.Render);
    }

    /// <summary>
    /// Обрабатывает изменение глобальных координат (DesignX/DesignY) и обновляет локальные (X/Y).
    /// </summary>
    private static void OnDesignPositionChanged(Control? control)
    {
        if (control == null || GetIsUpdatingPosition(control)) return;
        SetIsUpdatingPosition(control, true);
        try
        {
             // ИСПРАВЛЕНИЕ: Проверка инициализации XAML.
             // Если элемент еще не в дереве (нет родителя или корня), мы не можем посчитать координаты.
             // Подписываемся на AttachedToVisualTree и ждем.
             if (control.GetVisualRoot() is null || control.GetVisualParent() is null)
             {
                 void OnAttached(object? sender, VisualTreeAttachmentEventArgs e)
                 {
                     control.AttachedToVisualTree -= OnAttached;
                     // Повторный вызов уже с готовым деревом
                     OnDesignPositionChanged(control);
                 }

                 control.AttachedToVisualTree += OnAttached;
                 return;
             }

             // Стандартная логика: пересчет из глобальных в локальные
             Visual? root = control.FindAncestorOfType<DesignSurface>()
                            ?? control.FindAncestorOfType<DesignEditor>() as Visual;

             var parent = control.GetVisualParent();

             if (root != null && parent != null)
             {
                 var dx = GetDesignX(control);
                 var dy = GetDesignY(control);

                 var local = root.TranslatePoint(new Point(dx, dy), parent);

                 if (local.HasValue)
                 {
                     SetX(control, local.Value.X);
                     SetY(control, local.Value.Y);
                 }
             }
        }
        finally { SetIsUpdatingPosition(control, false); }
    }

    #region Accessors

    /// <summary>
    /// Возвращает локальную координату X элемента.
    /// </summary>
    /// <param name="o">Объект, для которого читается значение.</param>
    /// <returns>Значение локальной координаты X или <see cref="double.NaN"/>, если оно не задано.</returns>
    public static double GetX(AvaloniaObject o) => o.GetValue(XProperty);

    /// <summary>
    /// Задает локальную координату X элемента.
    /// </summary>
    /// <param name="o">Объект, для которого задается значение.</param>
    /// <param name="v">Новое значение координаты.</param>
    public static void SetX(AvaloniaObject o, double v) => o.SetValue(XProperty, v);

    /// <summary>
    /// Возвращает локальную координату Y элемента.
    /// </summary>
    /// <param name="o">Объект, для которого читается значение.</param>
    /// <returns>Значение локальной координаты Y или <see cref="double.NaN"/>, если оно не задано.</returns>
    public static double GetY(AvaloniaObject o) => o.GetValue(YProperty);

    /// <summary>
    /// Задает локальную координату Y элемента.
    /// </summary>
    /// <param name="o">Объект, для которого задается значение.</param>
    /// <param name="v">Новое значение координаты.</param>
    public static void SetY(AvaloniaObject o, double v) => o.SetValue(YProperty, v);

    /// <summary>
    /// Возвращает глобальную координату X элемента относительно поверхности дизайна.
    /// </summary>
    /// <param name="o">Объект, для которого читается значение.</param>
    /// <returns>Значение глобальной координаты X.</returns>
    /// <remarks>
    /// Это свойство особенно удобно для инспектора свойств, направляющих и оверлеев,
    /// которым нужна координата относительно корневого холста, а не локального контейнера.
    /// </remarks>
    public static double GetDesignX(AvaloniaObject o) => o.GetValue(DesignXProperty);

    /// <summary>
    /// Задает глобальную координату X элемента относительно поверхности дизайна.
    /// </summary>
    /// <param name="o">Объект, для которого задается значение.</param>
    /// <param name="v">Новое значение координаты.</param>
    public static void SetDesignX(AvaloniaObject o, double v) => o.SetValue(DesignXProperty, v);

    /// <summary>
    /// Возвращает глобальную координату Y элемента относительно поверхности дизайна.
    /// </summary>
    /// <param name="o">Объект, для которого читается значение.</param>
    /// <returns>Значение глобальной координаты Y.</returns>
    /// <remarks>
    /// Значение автоматически поддерживается системой позиционирования редактора.
    /// </remarks>
    public static double GetDesignY(AvaloniaObject o) => o.GetValue(DesignYProperty);

    /// <summary>
    /// Задает глобальную координату Y элемента относительно поверхности дизайна.
    /// </summary>
    /// <param name="o">Объект, для которого задается значение.</param>
    /// <param name="v">Новое значение координаты.</param>
    public static void SetDesignY(AvaloniaObject o, double v) => o.SetValue(DesignYProperty, v);

    /// <summary>
    /// Возвращает значение, указывающее, включено ли принудительное отслеживание позиции.
    /// </summary>
    /// <param name="o">Объект, для которого читается значение.</param>
    /// <returns><see langword="true"/>, если отслеживание включено; иначе <see langword="false"/>.</returns>
    public static bool GetIsTracked(AvaloniaObject o) => o.GetValue(IsTrackedProperty);

    /// <summary>
    /// Задает значение, указывающее, должно ли положение элемента отслеживаться всегда.
    /// </summary>
    /// <param name="o">Объект, для которого задается значение.</param>
    /// <param name="v"><see langword="true"/>, чтобы включить отслеживание; иначе <see langword="false"/>.</param>
    public static void SetIsTracked(AvaloniaObject o, bool v) => o.SetValue(IsTrackedProperty, v);

    private static bool GetIsUpdatingPosition(AvaloniaObject o) => o.GetValue(IsUpdatingPositionProperty);
    private static void SetIsUpdatingPosition(AvaloniaObject o, bool v) => o.SetValue(IsUpdatingPositionProperty, v);

    #endregion
}
