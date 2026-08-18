using System;
using System.Collections.Generic;
using Avalonia;

namespace ArxisStudio.Guides;

/// <summary>
/// Подсказка о равном зазоре: отрезок, которым он показан.
/// </summary>
/// <remarks>
/// <see cref="Start"/> и <see cref="End"/> — границы зазора по его оси,
/// <see cref="Position"/> — координата, на которой отрезок рисуется по другой оси.
/// </remarks>
internal readonly struct DesignSpacingHint : IEquatable<DesignSpacingHint>
{
    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignSpacingHint"/>.
    /// </summary>
    /// <param name="orientation">Ось, вдоль которой измерен зазор.</param>
    /// <param name="start">Начало зазора.</param>
    /// <param name="end">Конец зазора.</param>
    /// <param name="position">Координата отрисовки по другой оси.</param>
    public DesignSpacingHint(DesignSnapGuideOrientation orientation, double start, double end, double position)
    {
        Orientation = orientation;
        Start = start;
        End = end;
        Position = position;
    }

    /// <summary>Получает ось, вдоль которой измерен зазор.</summary>
    /// <remarks>
    /// <see cref="DesignSnapGuideOrientation.Vertical"/> означает зазор по оси X —
    /// та же конвенция, что и у направляющей, которая при этом вертикальна.
    /// </remarks>
    public DesignSnapGuideOrientation Orientation { get; }

    /// <summary>Получает начало зазора.</summary>
    public double Start { get; }

    /// <summary>Получает конец зазора.</summary>
    public double End { get; }

    /// <summary>Получает координату отрисовки по другой оси.</summary>
    public double Position { get; }

    /// <inheritdoc />
    public bool Equals(DesignSpacingHint other)
        => Orientation == other.Orientation
           && Start.Equals(other.Start)
           && End.Equals(other.End)
           && Position.Equals(other.Position);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DesignSpacingHint other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Orientation, Start, End, Position);
}

/// <summary>
/// Считает положение, при котором зазоры до ближайших соседей по оси равны.
/// </summary>
/// <remarks>
/// Здесь, как и в <see cref="DesignSnapGuideResolver"/>, только арифметика: соседи
/// приходят готовыми прямоугольниками в мировых координатах.
/// <para>
/// Отличие от выравнивания в том, что сравниваются <b>зазоры</b>, а не координаты.
/// Отсюда два требования, которых у выравнивания нет. Соседом считается только тот,
/// кто перекрывается с элементом по <b>другой</b> оси: «слева» и «справа» осмысленны
/// внутри одного ряда, а коробка этажом выше в ряд не входит. И соседи с нулевой
/// площадью исключаются — интервал бывает между элементами, а не до линии.
/// </para>
/// </remarks>
internal static class DesignSpacingResolver
{
    /// <summary>
    /// Допуск совпадения зазоров при сборе подсказок: смещение уже применено,
    /// и остаётся только погрешность double.
    /// </summary>
    private const double Epsilon = 0.01;

    /// <summary>
    /// Ищет смещение, уравнивающее зазоры до ближайших соседей.
    /// </summary>
    /// <param name="moving">Прямоугольник в предполагаемой позиции.</param>
    /// <param name="neighbours">Соседи в мировых координатах.</param>
    /// <param name="tolerance">Радиус захвата в мировых единицах.</param>
    /// <param name="offset">Смещение, уравнивающее зазоры.</param>
    /// <param name="spacedX">Признак того, что интервал найден по оси X.</param>
    /// <param name="spacedY">Признак того, что интервал найден по оси Y.</param>
    /// <returns><see langword="true"/>, если интервал найден хотя бы по одной оси.</returns>
    internal static bool TryResolveOffset(
        Rect moving,
        IReadOnlyList<Rect> neighbours,
        double tolerance,
        out Vector offset,
        out bool spacedX,
        out bool spacedY)
    {
        spacedX = TryResolveAxis(moving, neighbours, tolerance, xAxis: true, out var dx);
        spacedY = TryResolveAxis(moving, neighbours, tolerance, xAxis: false, out var dy);

        offset = new Vector(spacedX ? dx : 0, spacedY ? dy : 0);
        return spacedX || spacedY;
    }

    /// <summary>
    /// Ищет координату двигающегося края, при которой зазоры до соседей равны.
    /// </summary>
    /// <param name="proposed">Предполагаемая геометрия элемента.</param>
    /// <param name="neighbours">Соседи в мировых координатах.</param>
    /// <param name="tolerance">Радиус захвата в мировых единицах.</param>
    /// <param name="xAxis">Признак горизонтальной оси.</param>
    /// <param name="farEdge">Признак того, что двигается дальний край.</param>
    /// <param name="resolved">Найденная координата края.</param>
    /// <returns><see langword="true"/>, если координата найдена.</returns>
    /// <remarks>
    /// При перетаскивании элемент целиком встаёт посередине, и оба зазора считаются
    /// заново. При resize неподвижный край остаётся на месте, поэтому его зазор задан
    /// и остаётся один вопрос: где должен оказаться двигающийся край, чтобы второй
    /// зазор стал таким же. Отсюда и одна точка входа вместо смещения по двум осям —
    /// ровно так же устроена разница между перетаскиванием и resize у направляющих.
    /// </remarks>
    internal static bool TryResolveEdge(
        Rect proposed,
        IReadOnlyList<Rect> neighbours,
        double tolerance,
        bool xAxis,
        bool farEdge,
        out double resolved)
    {
        resolved = 0;

        if (!TryFindRowNeighbours(proposed, neighbours, xAxis, out var before, out var after))
            return false;

        var beforeEnd = xAxis ? before.Right : before.Bottom;
        var afterStart = xAxis ? after.X : after.Y;
        var near = xAxis ? proposed.X : proposed.Y;
        var far = xAxis ? proposed.Right : proposed.Bottom;

        double target;
        if (farEdge)
        {
            // Неподвижен ближний край: его зазор задан, дальний обязан стать таким же.
            var fixedGap = near - beforeEnd;
            if (fixedGap < 0)
                return false;

            target = afterStart - fixedGap;

            // Схлопывать элемент ради равенства нельзя: это уже не изменение размера.
            if (target <= near)
                return false;
        }
        else
        {
            var fixedGap = afterStart - far;
            if (fixedGap < 0)
                return false;

            target = beforeEnd + fixedGap;
            if (target >= far)
                return false;
        }

        var edge = farEdge ? far : near;
        if (Math.Abs(target - edge) > tolerance)
            return false;

        resolved = target;
        return true;
    }

    /// <summary>
    /// Собирает подсказки о равных зазорах вокруг применённой позиции.
    /// </summary>
    /// <param name="bounds">Применённая геометрия элемента.</param>
    /// <param name="neighbours">Соседи в мировых координатах.</param>
    internal static IReadOnlyList<DesignSpacingHint> CollectHints(Rect bounds, IReadOnlyList<Rect> neighbours)
    {
        List<DesignSpacingHint>? hints = null;

        CollectAxis(bounds, neighbours, xAxis: true, ref hints);
        CollectAxis(bounds, neighbours, xAxis: false, ref hints);

        return (IReadOnlyList<DesignSpacingHint>?)hints ?? Array.Empty<DesignSpacingHint>();
    }

    private static bool TryResolveAxis(
        Rect moving,
        IReadOnlyList<Rect> neighbours,
        double tolerance,
        bool xAxis,
        out double delta)
    {
        delta = 0;

        if (!TryFindRowNeighbours(moving, neighbours, xAxis, out var before, out var after))
            return false;

        var beforeEnd = xAxis ? before.Right : before.Bottom;
        var afterStart = xAxis ? after.X : after.Y;
        var size = xAxis ? moving.Width : moving.Height;

        // Свободное место между соседями за вычетом самого элемента делится пополам.
        var free = afterStart - beforeEnd - size;
        if (free < 0)
            return false;

        var target = beforeEnd + (free / 2);
        var current = xAxis ? moving.X : moving.Y;
        var candidate = target - current;

        if (Math.Abs(candidate) > tolerance)
            return false;

        delta = candidate;
        return true;
    }

    private static void CollectAxis(
        Rect bounds,
        IReadOnlyList<Rect> neighbours,
        bool xAxis,
        ref List<DesignSpacingHint>? hints)
    {
        if (!TryFindRowNeighbours(bounds, neighbours, xAxis, out var before, out var after))
            return;

        var beforeEnd = xAxis ? before.Right : before.Bottom;
        var start = xAxis ? bounds.X : bounds.Y;
        var end = xAxis ? bounds.Right : bounds.Bottom;
        var afterStart = xAxis ? after.X : after.Y;

        var first = start - beforeEnd;
        var second = afterStart - end;

        if (first < 0 || second < 0 || Math.Abs(first - second) > Epsilon)
            return;

        // Отрезок рисуется по середине перекрытия всех трёх: только там он читается
        // как измерение зазора, а не как ещё одна линия у края.
        var position = CrossCentre(bounds, before, after, xAxis);
        var orientation = xAxis
            ? DesignSnapGuideOrientation.Vertical
            : DesignSnapGuideOrientation.Horizontal;

        hints ??= new List<DesignSpacingHint>();
        hints.Add(new DesignSpacingHint(orientation, beforeEnd, start, position));
        hints.Add(new DesignSpacingHint(orientation, end, afterStart, position));
    }

    /// <summary>
    /// Ищет ближайших соседей по оси, лежащих с элементом в одном ряду.
    /// </summary>
    private static bool TryFindRowNeighbours(
        Rect moving,
        IReadOnlyList<Rect> neighbours,
        bool xAxis,
        out Rect before,
        out Rect after)
    {
        before = default;
        after = default;

        var hasBefore = false;
        var hasAfter = false;
        var bestBefore = double.NegativeInfinity;
        var bestAfter = double.PositiveInfinity;

        var start = xAxis ? moving.X : moving.Y;
        var end = xAxis ? moving.Right : moving.Bottom;

        for (var i = 0; i < neighbours.Count; i++)
        {
            var neighbour = neighbours[i];

            // Интервал бывает между элементами: линия нулевой площади соседом не считается.
            if (neighbour.Width <= 0 || neighbour.Height <= 0)
                continue;

            if (!OverlapsAcross(moving, neighbour, xAxis))
                continue;

            var neighbourStart = xAxis ? neighbour.X : neighbour.Y;
            var neighbourEnd = xAxis ? neighbour.Right : neighbour.Bottom;

            if (neighbourEnd <= start && neighbourEnd > bestBefore)
            {
                bestBefore = neighbourEnd;
                before = neighbour;
                hasBefore = true;
            }
            else if (neighbourStart >= end && neighbourStart < bestAfter)
            {
                bestAfter = neighbourStart;
                after = neighbour;
                hasAfter = true;
            }
        }

        return hasBefore && hasAfter;
    }

    private static bool OverlapsAcross(Rect moving, Rect neighbour, bool xAxis) => xAxis
        ? neighbour.Y < moving.Bottom && neighbour.Bottom > moving.Y
        : neighbour.X < moving.Right && neighbour.Right > moving.X;

    private static double CrossCentre(Rect bounds, Rect before, Rect after, bool xAxis)
    {
        var low = xAxis
            ? Math.Max(bounds.Y, Math.Max(before.Y, after.Y))
            : Math.Max(bounds.X, Math.Max(before.X, after.X));

        var high = xAxis
            ? Math.Min(bounds.Bottom, Math.Min(before.Bottom, after.Bottom))
            : Math.Min(bounds.Right, Math.Min(before.Right, after.Right));

        return low + ((high - low) / 2);
    }
}
