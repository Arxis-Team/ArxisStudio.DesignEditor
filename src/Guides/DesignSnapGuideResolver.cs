using System;
using System.Collections.Generic;
using Avalonia;

namespace ArxisStudio.Guides;

/// <summary>
/// Считает выравнивание перетаскиваемого прямоугольника по краям и центрам соседей.
/// </summary>
/// <remarks>
/// Здесь только арифметика: ни визуального дерева, ни редактора. Соседи приходят
/// готовыми прямоугольниками в мировых координатах, поэтому правило одинаково
/// работает и для вложенного контрола, и для контейнера верхнего уровня.
/// <para>
/// Кандидатов по каждой оси три — ближний край, центр, дальний край, — и сравнивается
/// каждый с каждым. Выравнивание «правый край к левому краю соседа» получается само,
/// отдельного правила под него не нужно.
/// </para>
/// </remarks>
internal static class DesignSnapGuideResolver
{
    /// <summary>
    /// Допуск совпадения при сборе линий. Совпадение здесь уже точное:
    /// смещение применено, и остаётся только погрешность double.
    /// </summary>
    private const double Epsilon = 0.01;

    private static readonly DesignSnapGuideAlignment[] Alignments =
    {
        DesignSnapGuideAlignment.Near,
        DesignSnapGuideAlignment.Centre,
        DesignSnapGuideAlignment.Far
    };

    /// <summary>
    /// Ищет смещение, которое ставит прямоугольник на ближайшую направляющую.
    /// </summary>
    /// <param name="moving">Прямоугольник в предполагаемой позиции.</param>
    /// <param name="neighbours">Соседи в мировых координатах.</param>
    /// <param name="tolerance">Радиус захвата в мировых единицах.</param>
    /// <param name="offset">Смещение, приводящее к выравниванию.</param>
    /// <param name="snappedX">Признак того, что выравнивание найдено по оси X.</param>
    /// <param name="snappedY">Признак того, что выравнивание найдено по оси Y.</param>
    /// <returns><see langword="true"/>, если найдено выравнивание хотя бы по одной оси.</returns>
    /// <remarks>
    /// Оси считаются независимо: элемент может встать на направляющую по X и при этом
    /// остаться на сетке по Y. Поэтому и результат — два отдельных признака, а не один.
    /// </remarks>
    internal static bool TryResolveOffset(
        Rect moving,
        IReadOnlyList<Rect> neighbours,
        double tolerance,
        out Vector offset,
        out bool snappedX,
        out bool snappedY)
    {
        snappedX = TryResolveAxis(moving, neighbours, tolerance, xAxis: true, out var deltaX);
        snappedY = TryResolveAxis(moving, neighbours, tolerance, xAxis: false, out var deltaY);

        offset = new Vector(snappedX ? deltaX : 0, snappedY ? deltaY : 0);
        return snappedX || snappedY;
    }

    /// <summary>
    /// Собирает линии, совпавшие с уже применённой геометрией.
    /// </summary>
    /// <param name="bounds">Итоговый прямоугольник перетаскиваемого элемента.</param>
    /// <param name="neighbours">Соседи в мировых координатах.</param>
    /// <remarks>
    /// Считается по итоговой позиции, а не по найденному смещению: ось, которую
    /// направляющая не заняла, могла уйти на сетку, и линия должна быть натянута
    /// по фактическому положению элемента.
    /// <para>
    /// Совпадение проверяется у всех соседей, а не только у победившего: когда
    /// у трёх контролов один левый край, выравнивание идёт ко всем трём, и линия
    /// обязана дотянуться до самого дальнего.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<DesignSnapGuide> CollectGuides(Rect bounds, IReadOnlyList<Rect> neighbours)
    {
        List<DesignSnapGuide>? guides = null;

        CollectAxis(bounds, neighbours, xAxis: true, ref guides);
        CollectAxis(bounds, neighbours, xAxis: false, ref guides);

        return guides ?? (IReadOnlyList<DesignSnapGuide>)Array.Empty<DesignSnapGuide>();
    }

    /// <summary>
    /// Ищет ближайшее выравнивание по одной оси.
    /// </summary>
    /// <remarks>
    /// Побеждает наименьшее по модулю смещение. При равенстве остаётся найденный
    /// раньше, а порядок перебора фиксирован — ближний край, центр, дальний край, —
    /// поэтому результат не зависит от порядка соседей в списке.
    /// </remarks>
    private static bool TryResolveAxis(
        Rect moving,
        IReadOnlyList<Rect> neighbours,
        double tolerance,
        bool xAxis,
        out double delta)
    {
        delta = 0;

        if (!(tolerance > 0))
            return false;

        var found = false;
        var best = double.PositiveInfinity;

        foreach (var alignment in Alignments)
        {
            var movingCoordinate = Coordinate(moving, alignment, xAxis);

            for (var i = 0; i < neighbours.Count; i++)
            {
                foreach (var neighbourAlignment in Alignments)
                {
                    var candidate = Coordinate(neighbours[i], neighbourAlignment, xAxis) - movingCoordinate;
                    var distance = Math.Abs(candidate);

                    if (distance > tolerance || distance >= best)
                        continue;

                    best = distance;
                    delta = candidate;
                    found = true;
                }
            }
        }

        return found;
    }

    private static void CollectAxis(
        Rect bounds,
        IReadOnlyList<Rect> neighbours,
        bool xAxis,
        ref List<DesignSnapGuide>? guides)
    {
        foreach (var alignment in Alignments)
        {
            var line = Coordinate(bounds, alignment, xAxis);
            var start = xAxis ? bounds.Y : bounds.X;
            var end = xAxis ? bounds.Bottom : bounds.Right;
            var matched = false;

            for (var i = 0; i < neighbours.Count; i++)
            {
                var neighbour = neighbours[i];

                foreach (var neighbourAlignment in Alignments)
                {
                    if (Math.Abs(Coordinate(neighbour, neighbourAlignment, xAxis) - line) > Epsilon)
                        continue;

                    matched = true;
                    start = Math.Min(start, xAxis ? neighbour.Y : neighbour.X);
                    end = Math.Max(end, xAxis ? neighbour.Bottom : neighbour.Right);
                    break;
                }
            }

            if (!matched)
                continue;

            var orientation = xAxis
                ? DesignSnapGuideOrientation.Vertical
                : DesignSnapGuideOrientation.Horizontal;

            (guides ??= new List<DesignSnapGuide>()).Add(new DesignSnapGuide(orientation, line, start, end));
        }
    }

    private static double Coordinate(Rect rect, DesignSnapGuideAlignment alignment, bool xAxis) => alignment switch
    {
        DesignSnapGuideAlignment.Near => xAxis ? rect.X : rect.Y,
        DesignSnapGuideAlignment.Centre => xAxis ? rect.X + (rect.Width / 2) : rect.Y + (rect.Height / 2),
        _ => xAxis ? rect.Right : rect.Bottom
    };

    /// <summary>
    /// Точка прямоугольника, участвующая в выравнивании по одной оси.
    /// </summary>
    private enum DesignSnapGuideAlignment
    {
        Near,
        Centre,
        Far
    }
}
