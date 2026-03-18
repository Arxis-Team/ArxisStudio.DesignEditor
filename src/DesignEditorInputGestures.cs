using Avalonia;
using Avalonia.Input;

namespace ArxisStudio;

/// <summary>
/// Представляет набор настраиваемых input gestures для <see cref="DesignEditor"/>.
/// </summary>
/// <remarks>
/// Объект предназначен для конфигурации пользовательских сценариев взаимодействия редактора
/// из XAML, styles, code-behind или через привязки в MVVM.
/// </remarks>
public class DesignEditorInputGestures : AvaloniaObject
{
    /// <summary>
    /// Идентификатор модификаторов, принудительно переключающих взаимодействие на уровень контейнера.
    /// </summary>
    public static readonly StyledProperty<KeyModifiers> ContainerInteractionModifiersProperty =
        AvaloniaProperty.Register<DesignEditorInputGestures, KeyModifiers>(
            nameof(ContainerInteractionModifiers),
            KeyModifiers.Control);

    /// <summary>
    /// Идентификатор модификаторов, переводящих selection в additive-режим.
    /// </summary>
    public static readonly StyledProperty<KeyModifiers> AdditiveSelectionModifiersProperty =
        AvaloniaProperty.Register<DesignEditorInputGestures, KeyModifiers>(
            nameof(AdditiveSelectionModifiers),
            KeyModifiers.Shift);

    /// <summary>
    /// Получает или задает модификаторы клавиатуры, которые принудительно переключают selection,
    /// drag и resize на уровень <see cref="DesignEditorItem"/>.
    /// </summary>
    public KeyModifiers ContainerInteractionModifiers
    {
        get => GetValue(ContainerInteractionModifiersProperty);
        set => SetValue(ContainerInteractionModifiersProperty, value);
    }

    /// <summary>
    /// Получает или задает модификаторы клавиатуры, которые включают additive selection.
    /// </summary>
    public KeyModifiers AdditiveSelectionModifiers
    {
        get => GetValue(AdditiveSelectionModifiersProperty);
        set => SetValue(AdditiveSelectionModifiersProperty, value);
    }
}
