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
/// Предоставляет систему присоединенных свойств для управления позиционированием элементов в редакторе.
/// <para>
/// Класс связывает локальные координаты (<see cref="XProperty"/>, <see cref="YProperty"/>)
/// с глобальными координатами на холсте (<see cref="DesignXProperty"/>, <see cref="DesignYProperty"/>).
/// </para>
/// </summary>
public static class Layout
{
    private static int _isInsidePositionChange;

    #region Attached Properties

    /// <summary>
    /// Локальная координата X элемента относительно его непосредственного родителя.
    /// <para>Используется контейнерами (например, <see cref="AbsolutePanel"/>) для размещения элемента.</para>
    /// </summary>
    public static readonly AttachedProperty<double> XProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "X", typeof(Layout), double.NaN, inherits: false, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Локальная координата Y элемента относительно его непосредственного родителя.
    /// <para>Используется контейнерами (например, <see cref="AbsolutePanel"/>) для размещения элемента.</para>
    /// </summary>
    public static readonly AttachedProperty<double> YProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "Y", typeof(Layout), double.NaN, inherits: false, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Глобальная координата X относительно поверхности дизайнера (<see cref="DesignSurface"/>).
    /// <para>Рассчитывается автоматически системой и используется для отображения в инспекторе свойств или отрисовки оверлеев.</para>
    /// </summary>
    public static readonly AttachedProperty<double> DesignXProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "DesignX", typeof(Layout), 0d, inherits: false, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Глобальная координата Y относительно поверхности дизайнера (<see cref="DesignSurface"/>).
    /// <para>Рассчитывается автоматически системой и используется для отображения в инспекторе свойств или отрисовки оверлеев.</para>
    /// </summary>
    public static readonly AttachedProperty<double> DesignYProperty =
        AvaloniaProperty.RegisterAttached<Control, double>(
            "DesignY", typeof(Layout), 0d, inherits: false, defaultBindingMode: BindingMode.TwoWay);

    /// <summary>
    /// Флаг принудительного отслеживания позиции элемента.
    /// <para>
    /// Если установлено значение <c>true</c>, система будет пересчитывать <see cref="DesignXProperty"/> и <see cref="DesignYProperty"/>
    /// при каждом обновлении макета (LayoutUpdated), даже если элемент находится глубоко во вложенных контейнерах (Grid, StackPanel).
    /// </para>
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
    /// Включает отслеживание перемещений элемента для расчета его глобальных координат.
    /// </summary>
    public static void Track(Control? control)
    {
        if (control == null) return;

        control.LayoutUpdated -= OnLayoutUpdated;
        control.LayoutUpdated += OnLayoutUpdated;

        UpdateDesignPosition(control);
    }

    /// <summary>
    /// Отключает отслеживание перемещений элемента.
    /// </summary>
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
            if (Interlocked.Exchange(ref _isInsidePositionChange, 1) == 1) return;
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
            finally { Interlocked.Exchange(ref _isInsidePositionChange, 0); }
        }, DispatcherPriority.Render);
    }

    /// <summary>
    /// Обрабатывает изменение глобальных координат (DesignX/DesignY) и обновляет локальные (X/Y).
    /// </summary>
    private static void OnDesignPositionChanged(Control? control)
    {
        if (control == null || Interlocked.Exchange(ref _isInsidePositionChange, 1) == 1) return;
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
        finally { Interlocked.Exchange(ref _isInsidePositionChange, 0); }
    }

    #region Accessors

    public static double GetX(AvaloniaObject o) => o.GetValue(XProperty);
    public static void SetX(AvaloniaObject o, double v) => o.SetValue(XProperty, v);

    public static double GetY(AvaloniaObject o) => o.GetValue(YProperty);
    public static void SetY(AvaloniaObject o, double v) => o.SetValue(YProperty, v);

    public static double GetDesignX(AvaloniaObject o) => o.GetValue(DesignXProperty);
    public static void SetDesignX(AvaloniaObject o, double v) => o.SetValue(DesignXProperty, v);

    public static double GetDesignY(AvaloniaObject o) => o.GetValue(DesignYProperty);
    public static void SetDesignY(AvaloniaObject o, double v) => o.SetValue(DesignYProperty, v);

    public static bool GetIsTracked(AvaloniaObject o) => o.GetValue(IsTrackedProperty);
    public static void SetIsTracked(AvaloniaObject o, bool v) => o.SetValue(IsTrackedProperty, v);

    #endregion
}
