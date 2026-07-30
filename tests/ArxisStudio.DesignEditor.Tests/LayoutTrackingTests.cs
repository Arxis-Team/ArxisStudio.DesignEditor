using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;
using DesignLayout = ArxisStudio.Attached.Layout;

namespace ArxisStudio.Tests;

/// <summary>
/// Учёт подписки на отслеживание позиции.
/// </summary>
/// <remarks>
/// Инвариант важен не сам по себе: на нём держится дешёвый отсев в глобальном
/// обработчике <c>BoundsProperty</c> и идемпотентность <c>Track</c>, который
/// вызывается из <c>AbsolutePanel.ArrangeOverride</c> для каждого ребёнка
/// на каждом arrange.
/// </remarks>
public class LayoutTrackingTests
{
    [AvaloniaFact]
    public void Control_Is_Not_Tracked_By_Default()
    {
        Assert.False(DesignLayout.IsTracking(new Border()));
    }

    [AvaloniaFact]
    public void Track_Marks_Control_As_Tracked()
    {
        var control = new Border();

        DesignLayout.Track(control);

        Assert.True(DesignLayout.IsTracking(control));
    }

    [AvaloniaFact]
    public void Track_Is_Idempotent()
    {
        var control = new Border();

        DesignLayout.Track(control);
        DesignLayout.Track(control);
        DesignLayout.Track(control);

        Assert.True(DesignLayout.IsTracking(control));

        // Одна подписка — одна отписка. Если бы Track подписывался повторно,
        // после единственного Untrack контрол остался бы подписанным.
        DesignLayout.Untrack(control);
        Assert.False(DesignLayout.IsTracking(control));
    }

    [AvaloniaFact]
    public void Untrack_On_Untracked_Control_Is_Safe()
    {
        var control = new Border();

        DesignLayout.Untrack(control);

        Assert.False(DesignLayout.IsTracking(control));
    }

    [AvaloniaFact]
    public void Setting_Layout_X_Starts_Tracking()
    {
        var control = new Border();

        DesignLayout.SetX(control, 42);

        Assert.True(DesignLayout.IsTracking(control));
    }

    [AvaloniaFact]
    public void IsTracked_Property_Toggles_Tracking()
    {
        var control = new Border();

        DesignLayout.SetIsTracked(control, true);
        Assert.True(DesignLayout.IsTracking(control));

        DesignLayout.SetIsTracked(control, false);
        Assert.False(DesignLayout.IsTracking(control));
    }
}
