using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;

namespace ArxisStudio;

/// <summary>
/// Накапливает изменения геометрии в пределах одного жеста.
/// </summary>
/// <remarks>
/// Запись ведётся не по факту чтения текущего состояния, а по значениям, которые
/// редактор задаёт: иначе итог зависел бы от того, успел ли пройти layout к моменту
/// фиксации. Исходное состояние снимается один раз, при первом обращении к target.
/// </remarks>
internal sealed class DesignEditScope
{
    private sealed class Entry
    {
        public Rect Before;
        public Point Position;
        public Size Size;

        public int BeforeZIndex;
        public int ZIndex;

    }

    private const double Tolerance = 0.01;

    private readonly Dictionary<Control, Entry> _entries = new();
    private readonly List<Control> _order = new();

    public DesignEditScope(DesignEditKind kind) => Kind = kind;

    public DesignEditKind Kind { get; }

    public void RecordPosition(DesignEditor editor, Control target, Point position)
        => Touch(editor, target).Position = position;

    public void RecordSize(DesignEditor editor, Control target, Size size)
        => Touch(editor, target).Size = size;

    public void RecordZIndex(DesignEditor editor, Control target, int zIndex)
        => Touch(editor, target).ZIndex = zIndex;

    /// <summary>
    /// Собирает итоговый список изменений, отбрасывая те, что вернулись к исходному.
    /// </summary>
    public IReadOnlyList<DesignChange> BuildChanges()
    {
        List<DesignChange>? changes = null;

        foreach (var target in _order)
        {
            var entry = _entries[target];
            var after = new Rect(entry.Position, entry.Size);

            if (!AreClose(entry.Before, after))
            {
                (changes ??= new List<DesignChange>()).Add(
                    new DesignGeometryChange(target, entry.Before, after));
            }

            if (entry.ZIndex != entry.BeforeZIndex)
            {
                (changes ??= new List<DesignChange>()).Add(
                    new DesignOrderChange(target, entry.BeforeZIndex, entry.ZIndex));
            }
        }

        return changes ?? (IReadOnlyList<DesignChange>)Array.Empty<DesignChange>();
    }

    private Entry Touch(DesignEditor editor, Control target)
    {
        if (_entries.TryGetValue(target, out var existing))
            return existing;

        var position = editor.GetDesignPosition(target);
        var size = editor.GetDesignSize(target);

        var entry = new Entry
        {
            Before = new Rect(position, size),
            Position = position,
            Size = size,
            BeforeZIndex = target.ZIndex,
            ZIndex = target.ZIndex
        };

        _entries[target] = entry;
        _order.Add(target);
        return entry;
    }

    private static bool AreClose(Rect a, Rect b)
        => Math.Abs(a.X - b.X) < Tolerance
           && Math.Abs(a.Y - b.Y) < Tolerance
           && Math.Abs(a.Width - b.Width) < Tolerance
           && Math.Abs(a.Height - b.Height) < Tolerance;
}
