using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless.XUnit;
using Xunit;

namespace ArxisStudio.Tests;

/// <summary>
/// Публичный шов записи геометрии: правка снаружи жеста.
/// </summary>
/// <remarks>
/// Панель свойств меняет X/Y/W/H тем же путём, что и перетаскивание, иначе её правки
/// прошли бы мимо контракта изменений и мимо политик раскладки: в <c>StackPanel</c>
/// поле X просто не подействовало бы, и пользователь не понял бы почему.
/// </remarks>
public class GeometryApiTests
{
    private static EditorHarness Create()
    {
        var harness = EditorHarness.Create(nodeCount: 1);
        harness.PlaceContainer(0, new Point(100, 100), new Size(200, 150));
        return harness;
    }

    [AvaloniaFact]
    public void Geometry_Is_Written_And_Undoable()
    {
        var harness = Create();
        var nested = harness.Nested(0);

        var edits = new List<DesignEditCompletedEventArgs>();
        harness.Editor.EditCompleted += (_, e) => edits.Add(e);

        Assert.True(harness.Editor.TryGetDesignBounds(nested, out var before));
        Assert.True(harness.Editor.SetDesignGeometry(nested, new Rect(140, 160, 80, 50)));
        harness.RunLayout();

        Assert.True(harness.Editor.TryGetDesignBounds(nested, out var after));
        Assert.Equal(new Rect(140, 160, 80, 50), after);

        var edit = Assert.Single(edits);
        foreach (var change in edit.Changes)
            harness.Editor.Revert(change);

        harness.RunLayout();
        Assert.True(harness.Editor.TryGetDesignBounds(nested, out var reverted));
        Assert.Equal(before, reverted);
    }

    /// <summary>
    /// Вид правки — по тому, что изменилось.
    /// </summary>
    /// <remarks>
    /// Хост разбирает поток изменений по <c>Kind</c>, и правка размера, приехавшая
    /// как <c>Move</c>, попала бы не в ту ветку.
    /// </remarks>
    [AvaloniaFact]
    public void Moving_And_Resizing_Are_Told_Apart()
    {
        var harness = Create();
        var nested = harness.Nested(0);
        var kinds = new List<DesignEditKind>();
        harness.Editor.EditCompleted += (_, e) => kinds.Add(e.Kind);

        Assert.True(harness.Editor.TryGetDesignBounds(nested, out var start));
        harness.Editor.SetDesignGeometry(nested, new Rect(start.X + 20, start.Y, start.Width, start.Height));
        harness.RunLayout();
        harness.Editor.SetDesignGeometry(nested, new Rect(start.X + 20, start.Y, start.Width + 20, start.Height));
        harness.RunLayout();

        Assert.Equal(new[] { DesignEditKind.Move, DesignEditKind.Resize }, kinds);
    }

    /// <summary>
    /// Раскладка, владеющая положением, отсекает позицию — но не размер.
    /// </summary>
    /// <remarks>
    /// Отсечка стоит на шве, поэтому снаружи жеста работает то же правило: в
    /// <c>StackPanel</c> положение задаёт панель, а явный размер honours любая.
    /// </remarks>
    [AvaloniaFact]
    public void The_Layout_Refuses_The_Position_But_Keeps_The_Size()
    {
        var harness = EditorHarness.CreateStackHosted();
        harness.PlaceContainer(0, new Point(100, 100), new Size(260, 240));
        var field = harness.Find<Avalonia.Controls.TextBox>(0, "Field");

        Assert.True(harness.Editor.TryGetDesignBounds(field, out var before));
        Assert.True(harness.Editor.SetDesignGeometry(field, new Rect(before.X + 40, before.Y + 40, 120, 44)));
        harness.RunLayout();

        Assert.True(harness.Editor.TryGetDesignBounds(field, out var after));
        // Высота выше MinHeight контрола: иначе проверялась бы не раскладка, а кламп.
        Assert.Equal(new Size(120, 44), after.Size);
        Assert.Equal(before.Position.Y, after.Position.Y);
    }

    [AvaloniaFact]
    public void An_Unchanged_Geometry_Records_Nothing()
    {
        var harness = Create();
        var nested = harness.Nested(0);
        var edits = 0;
        harness.Editor.EditCompleted += (_, _) => edits++;

        Assert.True(harness.Editor.TryGetDesignBounds(nested, out var bounds));

        Assert.False(harness.Editor.SetDesignGeometry(nested, bounds));
        Assert.Equal(0, edits);
    }

    [AvaloniaFact]
    public void SetDesignGeometry_Rejects_Null_Target()
    {
        var harness = Create();

        Assert.Throws<System.ArgumentNullException>(() => harness.Editor.SetDesignGeometry(null!, default));
    }
}
