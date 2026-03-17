using Avalonia;

namespace ArxisStudio;

/// <summary>
/// Описывает минимальный контракт элемента, который может размещаться на поверхности редактора.
/// </summary>
public interface IDesignEditorItem
{
    /// <summary>
    /// Получает позицию элемента на холсте.
    /// </summary>
    Point Location { get; }
}
