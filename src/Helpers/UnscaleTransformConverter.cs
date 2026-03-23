using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ArxisStudio.Helpers
{
    /// <summary>
    /// Преобразует масштабированную трансформацию viewport в обратную матрицу,
    /// чтобы оверлеи могли отрисовываться без масштабирования.
    /// </summary>
    internal class UnscaleTransformConverter : IValueConverter
    {
        /// <inheritdoc />
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is TransformGroup transformGroup)
                return new MatrixTransform(transformGroup.Children[0].Value.Invert());
            return null;
        }

        /// <inheritdoc />
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value;
        }
    }

    /// <summary>
    /// Умножает число на коэффициент масштаба.
    /// </summary>
    internal class ScaleDoubleConverter : IMultiValueConverter
    {
        /// <inheritdoc />
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Count == 2 && values[0] is double d1 && values[1] is double d2)
                return d1 * d2;
            return null;
        }

        /// <inheritdoc />
        public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Масштабирует точку на указанный коэффициент.
    /// </summary>
    internal class ScalePointConverter : IMultiValueConverter
    {
        /// <inheritdoc />
        public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
        {
            if (values.Any(x => x is UnsetValueType)) return false;

            Point result = (Point)((Vector)(Point)values[0]! * (double)values[1]!);
            return result;
        }

        /// <inheritdoc />
        public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
