using Avalonia;
using ArxisStudio.Controls;

namespace ArxisStudio;

/// <summary>
/// Представляет модель геометрии adorner'а для selection overlay.
/// </summary>
public class SelectionAdornerInfo
{
    /// <summary>
    /// Получает или задает рамку адорнера в мировых координатах редактора.
    /// </summary>
    public Rect Bounds { get; set; }

    /// <summary>
    /// Получает или задает роль адорнера.
    /// </summary>
    public SelectionAdornerRole Role { get; set; } = SelectionAdornerRole.Secondary;
}
