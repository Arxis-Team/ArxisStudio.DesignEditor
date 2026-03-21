using Avalonia;
using Avalonia.Controls;
using ArxisStudio.Controls;

namespace ArxisStudio;

/// <summary>
/// Представляет модель геометрии adorner'а для selection overlay.
/// </summary>
public class SelectionAdornerInfo
{
    /// <summary>
    /// Получает или задает контейнер, которому принадлежит visual target.
    /// </summary>
    public DesignEditorItem? Container { get; set; }

    /// <summary>
    /// Получает или задает visual target, для которого строится adorner.
    /// </summary>
    public Control? Target { get; set; }

    /// <summary>
    /// Получает или задает рамку адорнера в мировых координатах редактора.
    /// </summary>
    public Rect Bounds { get; set; }

    /// <summary>
    /// Получает или задает роль адорнера.
    /// </summary>
    public SelectionAdornerRole Role { get; set; } = SelectionAdornerRole.Secondary;

    /// <summary>
    /// Получает или задает признак отображения resize handles.
    /// </summary>
    public bool ShowHandles { get; set; }

    /// <summary>
    /// Получает или задает признак интерактивности adorner.
    /// </summary>
    public bool IsInteractive { get; set; }
}
