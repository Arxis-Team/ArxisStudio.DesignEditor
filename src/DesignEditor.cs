using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Logging;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DesignLayout = ArxisStudio.Attached.Layout;
using DesignInteraction = ArxisStudio.Attached.DesignInteraction;
using ArxisStudio.Controls;
using ArxisStudio.Guides;
using ArxisStudio.Placement;
using ArxisStudio.States;

namespace ArxisStudio;

/// <summary>
/// Представляет поверхность визуального редактора с поддержкой панорамирования,
/// масштабирования, множественного выделения, перетаскивания и изменения размеров элементов.
/// </summary>
/// <remarks>
/// Контрол наследуется от <see cref="SelectingItemsControl"/> и использует
/// <see cref="DesignEditorItem"/> в качестве контейнера для элементов коллекции.
/// <para>
/// Для корректной работы визуальных стилей необходимо подключить словари ресурсов
/// из каталога <c>Themes/Styles</c> библиотеки.
/// </para>
/// </remarks>
/// <example>
/// <code language="xml"><![CDATA[
/// <design:DesignEditor ItemsSource="{Binding Nodes}"
///                      SelectedItems="{Binding SelectedNodes}"
///                      SelectionMode="Multiple"
///                      ViewportZoom="{Binding Zoom, Mode=TwoWay}" />
/// ]]></code>
/// </example>
public class DesignEditor : SelectingItemsControl
{
    #region Constants
    private const double ZoomTolerance = 0.0001;
    private const double FitToViewPadding = 32.0;
    #endregion

    #region State Machine

    private readonly Stack<EditorState> _states = new();

    /// <summary>
    /// Получает текущее активное состояние редактора.
    /// </summary>
    internal EditorState CurrentState => _states.Count > 0 ? _states.Peek() : null!;

    /// <summary>
    /// Помещает новое состояние в стек и вызывает его инициализацию.
    /// </summary>
    /// <param name="state">Состояние, которое должно стать активным.</param>
    internal void PushState(EditorState state)
    {
        var previous = _states.Count > 0 ? _states.Peek() : null;
        _states.Push(state);
        state.Enter(previous);
    }

    /// <summary>
    /// Завершает текущее состояние и возвращается к предыдущему, если стек содержит более одного состояния.
    /// </summary>
    internal void PopState()
    {
        if (_states.Count > 1)
        {
            var current = _states.Pop();
            current.Exit();
        }
    }

    #endregion

    #region Re-exposed Properties

    /// <summary>
    /// Идентификатор свойства модели выделения, повторно экспортированный из базового класса.
    /// </summary>
    public new static readonly DirectProperty<SelectingItemsControl, ISelectionModel> SelectionProperty =
        SelectingItemsControl.SelectionProperty;

    /// <summary>
    /// Идентификатор свойства коллекции выбранных элементов, повторно экспортированный из базового класса.
    /// </summary>
    public new static readonly DirectProperty<SelectingItemsControl, IList?> SelectedItemsProperty =
        SelectingItemsControl.SelectedItemsProperty;

    /// <summary>
    /// Идентификатор свойства режима выделения.
    /// </summary>
    public new static readonly StyledProperty<SelectionMode> SelectionModeProperty =
        SelectingItemsControl.SelectionModeProperty.AddOwner<DesignEditor>();

    /// <summary>
    /// Получает или задает модель выделения редактора.
    /// </summary>
    public new ISelectionModel Selection
    {
        get => base.Selection;
        set => base.Selection = value;
    }

    /// <summary>
    /// Получает или задает внешнюю коллекцию выбранных элементов.
    /// </summary>
    public new IList? SelectedItems
    {
        get => base.SelectedItems;
        set => base.SelectedItems = value;
    }

    /// <summary>
    /// Получает или задает режим выделения элементов.
    /// </summary>
    public new SelectionMode SelectionMode
    {
        get => base.SelectionMode;
        set => base.SelectionMode = value;
    }

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Идентификатор свойства позиции viewport в мировых координатах.
    /// </summary>
    public static readonly StyledProperty<Point> ViewportLocationProperty =
        AvaloniaProperty.Register<DesignEditor, Point>(nameof(ViewportLocation));

    /// <summary>
    /// Идентификатор свойства текущего масштаба viewport.
    /// </summary>
    public static readonly StyledProperty<double> ViewportZoomProperty =
        AvaloniaProperty.Register<DesignEditor, double>(nameof(ViewportZoom), 1.0);

    /// <summary>
    /// Идентификатор свойства минимального допустимого масштаба.
    /// </summary>
    public static readonly StyledProperty<double> MinZoomProperty =
        AvaloniaProperty.Register<DesignEditor, double>(nameof(MinZoom), 0.1);

    /// <summary>
    /// Идентификатор свойства максимального допустимого масштаба.
    /// </summary>
    public static readonly StyledProperty<double> MaxZoomProperty =
        AvaloniaProperty.Register<DesignEditor, double>(nameof(MaxZoom), 5.0);

    /// <summary>
    /// Идентификатор трансформации viewport в логических координатах.
    /// </summary>
    public static readonly StyledProperty<Transform> ViewportTransformProperty =
        AvaloniaProperty.Register<DesignEditor, Transform>(nameof(ViewportTransform), new TransformGroup());

    /// <summary>
    /// Идентификатор трансформации viewport с учетом текущего DPI.
    /// </summary>
    public static readonly StyledProperty<Transform> DpiScaledViewportTransformProperty =
        AvaloniaProperty.Register<DesignEditor, Transform>(nameof(DpiScaledViewportTransform), new TransformGroup());

    /// <summary>
    /// Идентификатор свойства видимости фоновой сетки.
    /// </summary>
    public static readonly StyledProperty<bool> ShowGridProperty =
        AvaloniaProperty.Register<DesignEditor, bool>(nameof(ShowGrid), true);

    /// <summary>
    /// Идентификатор темы для прямоугольника выделения.
    /// </summary>
    public static readonly StyledProperty<ControlTheme> SelectionRectangleStyleProperty =
        AvaloniaProperty.Register<DesignEditor, ControlTheme>(nameof(SelectionRectangleStyle));

    /// <summary>
    /// Идентификатор объекта с настройками input gestures редактора.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, DesignEditorInputGestures> InputGesturesProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, DesignEditorInputGestures>(
            nameof(InputGestures),
            o => o.InputGestures,
            (o, v) => o.InputGestures = v);

    /// <summary>
    /// Идентификатор объекта с runtime-настройками взаимодействия редактора.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, DesignEditorInteractionOptions> InteractionOptionsProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, DesignEditorInteractionOptions>(
            nameof(InteractionOptions),
            o => o.InteractionOptions,
            (o, v) => o.InteractionOptions = v);

    /// <summary>
    /// Идентификатор модификаторов, принудительно переключающих взаимодействие на уровень контейнера.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, KeyModifiers> ContainerInteractionModifiersProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, KeyModifiers>(
            nameof(ContainerInteractionModifiers),
            o => o.ContainerInteractionModifiers,
            (o, v) => o.ContainerInteractionModifiers = v);

    /// <summary>
    /// Идентификатор модификаторов additive selection.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, KeyModifiers> AdditiveSelectionModifiersProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, KeyModifiers>(
            nameof(AdditiveSelectionModifiers),
            o => o.AdditiveSelectionModifiers,
            (o, v) => o.AdditiveSelectionModifiers = v);

    /// <summary>
    /// Идентификатор свойства, показывающего активен ли marquee-selection.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> IsSelectingProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(IsSelecting), o => o.IsSelecting, (o, v) => o.IsSelecting = v);

    /// <summary>
    /// Идентификатор свойства имени раскладки primary target.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, string?> PrimarySelectionPlacementProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, string?>(
            nameof(PrimarySelectionPlacement), o => o.PrimarySelectionPlacement);

    /// <summary>
    /// Идентификатор свойства действующей политики перемещения primary target.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, ArxisStudio.Attached.MovePolicy> PrimarySelectionMovePolicyProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, ArxisStudio.Attached.MovePolicy>(
            nameof(PrimarySelectionMovePolicy), o => o.PrimarySelectionMovePolicy);

    /// <summary>
    /// Идентификатор свойства действующей политики изменения размера primary target.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, ArxisStudio.Attached.ResizePolicy> PrimarySelectionResizePolicyProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, ArxisStudio.Attached.ResizePolicy>(
            nameof(PrimarySelectionResizePolicy), o => o.PrimarySelectionResizePolicy);

    /// <summary>
    /// Идентификатор свойства, показывающего активна ли перестановка среди соседей.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> IsReorderingProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(IsReordering), o => o.IsReordering);

    /// <summary>
    /// Идентификатор свойства индикатора вставки.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, Rect> ReorderIndicatorProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, Rect>(nameof(ReorderIndicator), o => o.ReorderIndicator);

    /// <summary>
    /// Идентификатор свойства набора активных направляющих.
    /// </summary>
    /// <remarks>
    /// Свойство internal: форму направляющих ещё рано фиксировать публично, а шаблон
    /// библиотеки компилируется в ту же сборку и привязывается к нему без ограничений.
    /// </remarks>
    internal static readonly DirectProperty<DesignEditor, IReadOnlyList<DesignSnapGuide>> SnapGuidesProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, IReadOnlyList<DesignSnapGuide>>(
            nameof(SnapGuides),
            o => o.SnapGuides);

    /// <summary>
    /// Идентификатор свойства контейнера, в пределах которого работает текущая рамка.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, DesignEditorItem?> MarqueeScopeProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, DesignEditorItem?>(
            nameof(MarqueeScope),
            o => o.MarqueeScope);

    /// <summary>
    /// Идентификатор свойства прямоугольника выделения в мировых координатах.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, Rect> SelectedAreaProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, Rect>(nameof(SelectedArea), o => o.SelectedArea, (o, v) => o.SelectedArea = v);

    /// <summary>
    /// Идентификатор свойства прямоугольника, охватывающего все размещенные элементы.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, Rect> ItemsExtentProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, Rect>(nameof(ItemsExtent), o => o.ItemsExtent, (o, v) => o.ItemsExtent = v);

    /// <summary>
    /// Идентификатор свойства прямоугольника, охватывающего текущее выделение.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, Rect> SelectionBoundsProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, Rect>(nameof(SelectionBounds), o => o.SelectionBounds, (o, v) => o.SelectionBounds = v);

    /// <summary>
    /// Идентификатор коллекции per-target secondary outlines для multi-selection.
    /// </summary>
    internal static readonly DirectProperty<DesignEditor, IReadOnlyList<SelectionAdornerInfo>> SecondarySelectionAdornersProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, IReadOnlyList<SelectionAdornerInfo>>(
            nameof(SecondarySelectionAdorners),
            o => o.SecondarySelectionAdorners,
            (o, v) => o.SecondarySelectionAdorners = v);

    /// <summary>
    /// Идентификатор количества secondary selection adorner'ов.
    /// </summary>
    internal static readonly DirectProperty<DesignEditor, int> SecondarySelectionAdornersCountProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, int>(
            nameof(SecondarySelectionAdornersCount),
            o => o.SecondarySelectionAdornersCount);

    /// <summary>
    /// Идентификатор primary selection target.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, DesignSelectionTarget?> PrimarySelectionTargetProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, DesignSelectionTarget?>(
            nameof(PrimarySelectionTarget),
            o => o.PrimarySelectionTarget);

    /// <summary>
    /// Идентификатор коллекции всех выбранных design targets.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, IReadOnlyList<DesignSelectionTarget>> SelectedDesignTargetsProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, IReadOnlyList<DesignSelectionTarget>>(
            nameof(SelectedDesignTargets),
            o => o.SelectedDesignTargets,
            (o, v) => o.SelectedDesignTargets = v);

    /// <summary>
    /// Идентификатор количества выбранных design targets.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, int> SelectedDesignTargetsCountProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, int>(
            nameof(SelectedDesignTargetsCount),
            o => o.SelectedDesignTargetsCount);

    /// <summary>
    /// Идентификатор свойства, указывающего наличие ровно одного выбранного элемента.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> HasSingleSelectionProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(HasSingleSelection), o => o.HasSingleSelection, (o, v) => o.HasSingleSelection = v);

    /// <summary>
    /// Идентификатор свойства, указывающего наличие множественного выделения.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> HasMultipleSelectionProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(HasMultipleSelection), o => o.HasMultipleSelection, (o, v) => o.HasMultipleSelection = v);

    /// <summary>
    /// Идентификатор свойства, указывающего на множественное выделение nested targets.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> HasMultipleNestedSelectionProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(HasMultipleNestedSelection), o => o.HasMultipleNestedSelection, (o, v) => o.HasMultipleNestedSelection = v);

    /// <summary>
    /// Идентификатор свойства, указывающего на множественное выделение контейнеров.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, bool> HasMultipleContainerSelectionProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, bool>(nameof(HasMultipleContainerSelection), o => o.HasMultipleContainerSelection, (o, v) => o.HasMultipleContainerSelection = v);

    #endregion

    #region Wrappers

    /// <summary>
    /// Получает или задает положение viewport в мировых координатах.
    /// </summary>
    /// <remarks>
    /// Значение задает левый верхний угол видимой области в координатах содержимого.
    /// Обычно изменяется автоматически во время панорамирования или программно для перехода к нужной области.
    /// </remarks>
    public Point ViewportLocation
    {
        get => GetValue(ViewportLocationProperty);
        set => SetValue(ViewportLocationProperty, value);
    }

    /// <summary>
    /// Получает или задает текущий коэффициент масштабирования viewport.
    /// </summary>
    /// <remarks>
    /// Значение ограничивается диапазоном между <see cref="MinZoom"/> и <see cref="MaxZoom"/>.
    /// </remarks>
    public double ViewportZoom
    {
        get => GetValue(ViewportZoomProperty);
        set => SetValue(ViewportZoomProperty, value);
    }

    /// <summary>
    /// Получает или задает минимальное значение <see cref="ViewportZoom"/>.
    /// </summary>
    public double MinZoom
    {
        get => GetValue(MinZoomProperty);
        set => SetValue(MinZoomProperty, value);
    }

    /// <summary>
    /// Получает или задает максимальное значение <see cref="ViewportZoom"/>.
    /// </summary>
    public double MaxZoom
    {
        get => GetValue(MaxZoomProperty);
        set => SetValue(MaxZoomProperty, value);
    }

    /// <summary>
    /// Получает или задает трансформацию, применяемую к содержимому viewport.
    /// </summary>
    public Transform ViewportTransform
    {
        get => GetValue(ViewportTransformProperty);
        set => SetValue(ViewportTransformProperty, value);
    }

    /// <summary>
    /// Получает или задает DPI-aware трансформацию viewport.
    /// </summary>
    public Transform DpiScaledViewportTransform
    {
        get => GetValue(DpiScaledViewportTransformProperty);
        set => SetValue(DpiScaledViewportTransformProperty, value);
    }

    /// <summary>
    /// Получает или задает признак отображения фоновой сетки.
    /// </summary>
    /// <remarks>
    /// Сетка входит в шаблон редактора и настраивается через тему
    /// <see cref="Controls.DesignGrid"/> и ресурсы <c>DesignEditor.Grid.*</c>.
    /// Для собственного фона достаточно выключить её и задать <see cref="TemplatedControl.Background"/>.
    /// </remarks>
    public bool ShowGrid
    {
        get => GetValue(ShowGridProperty);
        set => SetValue(ShowGridProperty, value);
    }

    /// <summary>
    /// Получает или задает тему визуализации рамки выделения.
    /// </summary>
    public ControlTheme SelectionRectangleStyle
    {
        get => GetValue(SelectionRectangleStyleProperty);
        set => SetValue(SelectionRectangleStyleProperty, value);
    }

    private DesignEditorInputGestures _inputGestures = new DesignEditorInputGestures();

    /// <summary>
    /// Получает или задает набор настраиваемых input gestures редактора.
    /// </summary>
    /// <remarks>
    /// Это основная точка конфигурации горячих клавиш и модификаторов взаимодействия.
    /// Свойство можно задавать из AXAML, styles, code-behind или через привязки.
    /// </remarks>
    public DesignEditorInputGestures InputGestures
    {
        get => _inputGestures;
        set
        {
            var gestures = value ?? new DesignEditorInputGestures();
            SetAndRaise(InputGesturesProperty, ref _inputGestures, gestures);
            SetAndRaise(ContainerInteractionModifiersProperty, ref _containerInteractionModifiers, gestures.ContainerInteractionModifiers);
            SetAndRaise(AdditiveSelectionModifiersProperty, ref _additiveSelectionModifiers, gestures.AdditiveSelectionModifiers);
        }
    }

    private DesignEditorInteractionOptions _interactionOptions = new DesignEditorInteractionOptions();

    /// <summary>
    /// Получает или задает runtime-настройки взаимодействия редактора, не относящиеся к жестам ввода.
    /// </summary>
    /// <remarks>
    /// В этом объекте настраиваются числовые параметры поведения, такие как шаг zoom,
    /// порог начала drag и минимальный размер при resize.
    /// </remarks>
    public DesignEditorInteractionOptions InteractionOptions
    {
        get => _interactionOptions;
        set
        {
            var options = value ?? new DesignEditorInteractionOptions();
            SetAndRaise(InteractionOptionsProperty, ref _interactionOptions, options);
        }
    }

    private KeyModifiers _containerInteractionModifiers = KeyModifiers.Control;

    /// <summary>
    /// Получает или задает модификаторы клавиатуры, которые принудительно переключают selection,
    /// drag и resize на уровень <see cref="DesignEditorItem"/>.
    /// </summary>
    /// <remarks>
    /// Совместимое сокращенное свойство над <see cref="InputGestures"/>.
    /// Для нового кода рекомендуется использовать <see cref="InputGestures"/> напрямую.
    /// </remarks>
    public KeyModifiers ContainerInteractionModifiers
    {
        get => InputGestures.ContainerInteractionModifiers;
        set
        {
            SetAndRaise(ContainerInteractionModifiersProperty, ref _containerInteractionModifiers, value);
            InputGestures.ContainerInteractionModifiers = value;
        }
    }

    private KeyModifiers _additiveSelectionModifiers = KeyModifiers.Shift;

    /// <summary>
    /// Получает или задает модификаторы additive selection.
    /// </summary>
    /// <remarks>
    /// Совместимое сокращенное свойство над <see cref="InputGestures"/>.
    /// Для нового кода рекомендуется использовать <see cref="InputGestures"/> напрямую.
    /// </remarks>
    public KeyModifiers AdditiveSelectionModifiers
    {
        get => InputGestures.AdditiveSelectionModifiers;
        set
        {
            SetAndRaise(AdditiveSelectionModifiersProperty, ref _additiveSelectionModifiers, value);
            InputGestures.AdditiveSelectionModifiers = value;
        }
    }

    private bool _isSelecting;
    /// <summary>
    /// Получает или задает признак активного прямоугольного выделения.
    /// </summary>
    public bool IsSelecting
    {
        get => _isSelecting;
        set => SetAndRaise(IsSelectingProperty, ref _isSelecting, value);
    }

    private string? _primarySelectionPlacement;
    /// <summary>
    /// Получает имя раскладки, которая распоряжается положением primary target.
    /// </summary>
    /// <remarks>
    /// Отвечает на вопрос «почему этот контрол не двигается»: <c>Stack</c> означает,
    /// что панель расставляет детей сама и перетаскивание меняет их порядок, а не
    /// координату; <c>Grid</c> и <c>Dock</c> — что положение задаётся присоединёнными
    /// свойствами раскладки. <see langword="null"/> — выделения нет.
    /// </remarks>
    public string? PrimarySelectionPlacement
    {
        get => _primarySelectionPlacement;
        private set => SetAndRaise(PrimarySelectionPlacementProperty, ref _primarySelectionPlacement, value);
    }

    private ArxisStudio.Attached.MovePolicy _primarySelectionMovePolicy;
    /// <summary>
    /// Получает действующую политику перемещения primary target.
    /// </summary>
    /// <remarks>
    /// Именно действующую, а не заданную: это <c>политика пользователя &amp; возможности
    /// раскладки</c>, то есть то, что редактор реально позволит сделать.
    /// </remarks>
    public ArxisStudio.Attached.MovePolicy PrimarySelectionMovePolicy
    {
        get => _primarySelectionMovePolicy;
        private set => SetAndRaise(PrimarySelectionMovePolicyProperty, ref _primarySelectionMovePolicy, value);
    }

    private ArxisStudio.Attached.ResizePolicy _primarySelectionResizePolicy;
    /// <summary>
    /// Получает действующую политику изменения размера primary target.
    /// </summary>
    /// <remarks>
    /// Раскладка её не сужает: явный размер honours любая панель, потому что
    /// применяется до выравнивания. Ограничивают размер <c>Min</c>/<c>Max</c>
    /// самого контрола и границы формы, а не родительская раскладка.
    /// </remarks>
    public ArxisStudio.Attached.ResizePolicy PrimarySelectionResizePolicy
    {
        get => _primarySelectionResizePolicy;
        private set => SetAndRaise(PrimarySelectionResizePolicyProperty, ref _primarySelectionResizePolicy, value);
    }

    private bool _isReordering;
    /// <summary>
    /// Получает признак активной перестановки контрола среди соседей.
    /// </summary>
    public bool IsReordering
    {
        get => _isReordering;
        private set => SetAndRaise(IsReorderingProperty, ref _isReordering, value);
    }

    private Rect _reorderIndicator;
    /// <summary>
    /// Получает прямоугольник индикатора вставки в мировых координатах.
    /// </summary>
    /// <remarks>
    /// Толщина линии намеренно нулевая: на экране её задаёт шаблон, поэтому
    /// индикатор остаётся одинаково тонким на любом масштабе.
    /// </remarks>
    public Rect ReorderIndicator
    {
        get => _reorderIndicator;
        private set => SetAndRaise(ReorderIndicatorProperty, ref _reorderIndicator, value);
    }

    private IReadOnlyList<DesignSnapGuide> _snapGuides = Array.Empty<DesignSnapGuide>();
    /// <summary>
    /// Получает направляющие, действующие в текущем жесте, в мировых координатах.
    /// </summary>
    /// <remarks>
    /// Набор пуст вне жеста и всегда, когда выравнивания не нашлось. Публикуется он
    /// только при фактическом изменении — см. <see cref="PublishSnapGuides"/>.
    /// </remarks>
    internal IReadOnlyList<DesignSnapGuide> SnapGuides
    {
        get => _snapGuides;
        private set => SetAndRaise(SnapGuidesProperty, ref _snapGuides, value);
    }

    private Rect _selectedArea;
    /// <summary>
    /// Получает или задает текущий прямоугольник выделения в мировых координатах.
    /// </summary>
    public Rect SelectedArea
    {
        get => _selectedArea;
        set => SetAndRaise(SelectedAreaProperty, ref _selectedArea, value);
    }

    private DesignEditorItem? _marqueeScope;
    /// <summary>
    /// Получает контейнер, в пределах которого сейчас работает рамка выделения,
    /// либо <see langword="null"/>, если рамка не активна или работает на уровне контейнеров.
    /// </summary>
    /// <remarks>
    /// Значение пересчитывается на каждом шаге протяжки, поэтому по нему можно
    /// подсвечивать целевой контейнер прямо во время жеста: пользователь видит,
    /// что именно попадёт в выборку, ещё до отпускания кнопки.
    /// <para>
    /// Библиотека не навязывает визуал подсветки — это решение конкретного продукта.
    /// </para>
    /// </remarks>
    public DesignEditorItem? MarqueeScope
    {
        get => _marqueeScope;
        private set => SetAndRaise(MarqueeScopeProperty, ref _marqueeScope, value);
    }

    private Rect _itemsExtent;
    /// <summary>
    /// Получает или задает прямоугольник, охватывающий все дочерние элементы редактора.
    /// </summary>
    public Rect ItemsExtent
    {
        get => _itemsExtent;
        set => SetAndRaise(ItemsExtentProperty, ref _itemsExtent, value);
    }

    private Rect _selectionBounds;
    /// <summary>
    /// Получает или задает прямоугольник, охватывающий текущее выделение.
    /// </summary>
    public Rect SelectionBounds
    {
        get => _selectionBounds;
        private set => SetAndRaise(SelectionBoundsProperty, ref _selectionBounds, value);
    }

    private IReadOnlyList<SelectionAdornerInfo> _secondarySelectionAdorners = Array.Empty<SelectionAdornerInfo>();
    /// <summary>
    /// Получает коллекцию per-target secondary adorner'ов для multi-selection.
    /// </summary>
    internal IReadOnlyList<SelectionAdornerInfo> SecondarySelectionAdorners
    {
        get => _secondarySelectionAdorners;
        private set
        {
            SetAndRaise(SecondarySelectionAdornersProperty, ref _secondarySelectionAdorners, value);
            SetAndRaise(SecondarySelectionAdornersCountProperty, ref _secondarySelectionAdornersCount, value.Count);
        }
    }

    private int _secondarySelectionAdornersCount;
    /// <summary>
    /// Получает количество secondary adorner'ов в текущем multi-selection overlay.
    /// </summary>
    internal int SecondarySelectionAdornersCount => _secondarySelectionAdornersCount;

    private DesignSelectionTarget? _primarySelectionTarget;
    /// <summary>
    /// Получает primary selection target редактора.
    /// </summary>
    public DesignSelectionTarget? PrimarySelectionTarget
    {
        get => _primarySelectionTarget;
        private set => SetAndRaise(PrimarySelectionTargetProperty, ref _primarySelectionTarget, value);
    }

    private IReadOnlyList<DesignSelectionTarget> _selectedDesignTargets = Array.Empty<DesignSelectionTarget>();
    /// <summary>
    /// Получает снимок всех выбранных design targets.
    /// </summary>
    public IReadOnlyList<DesignSelectionTarget> SelectedDesignTargets
    {
        get => _selectedDesignTargets;
        private set
        {
            SetAndRaise(SelectedDesignTargetsProperty, ref _selectedDesignTargets, value);
            SetAndRaise(SelectedDesignTargetsCountProperty, ref _selectedDesignTargetsCount, value.Count);
        }
    }

    private int _selectedDesignTargetsCount;
    /// <summary>
    /// Получает количество выбранных design targets.
    /// </summary>
    public int SelectedDesignTargetsCount => _selectedDesignTargetsCount;

    private bool _hasSingleSelection;
    /// <summary>
    /// Получает значение, указывающее, что в редакторе выбран ровно один элемент.
    /// </summary>
    public bool HasSingleSelection
    {
        get => _hasSingleSelection;
        private set => SetAndRaise(HasSingleSelectionProperty, ref _hasSingleSelection, value);
    }

    private bool _hasMultipleSelection;
    /// <summary>
    /// Получает значение, указывающее, что в редакторе выбрано более одного элемента.
    /// </summary>
    public bool HasMultipleSelection
    {
        get => _hasMultipleSelection;
        private set => SetAndRaise(HasMultipleSelectionProperty, ref _hasMultipleSelection, value);
    }

    private bool _hasMultipleNestedSelection;
    /// <summary>
    /// Получает значение, указывающее, что выбрано несколько nested targets внутри одного контейнера.
    /// </summary>
    public bool HasMultipleNestedSelection
    {
        get => _hasMultipleNestedSelection;
        private set => SetAndRaise(HasMultipleNestedSelectionProperty, ref _hasMultipleNestedSelection, value);
    }

    private bool _hasMultipleContainerSelection;
    /// <summary>
    /// Получает значение, указывающее, что выбрано несколько контейнеров <see cref="DesignEditorItem"/>.
    /// </summary>
    public bool HasMultipleContainerSelection
    {
        get => _hasMultipleContainerSelection;
        private set => SetAndRaise(HasMultipleContainerSelectionProperty, ref _hasMultipleContainerSelection, value);
    }

    /// <summary>
    /// Получает коллекцию провайдеров действий контекстного меню.
    /// </summary>
    public IList<IDesignEditorContextActionProvider> ContextActionProviders { get; } = new List<IDesignEditorContextActionProvider>();

    /// <summary>
    /// Возникает перед показом контекстного меню.
    /// </summary>
    public event EventHandler<DesignEditorContextRequestingEventArgs>? ContextMenuRequesting;

    /// <summary>
    /// Возникает после разрешения контекста и списка действий.
    /// </summary>
    public event EventHandler<DesignEditorContextRequestedEventArgs>? ContextMenuResolved;

    /// <summary>
    /// Возникает после завершения единицы редактирования — перемещения или изменения размера.
    /// </summary>
    /// <remarks>
    /// Одно событие на жест целиком, а не на кадр: это та гранулярность, в которой
    /// изменения кладутся в стек undo. Жест, не изменивший геометрию, события не вызывает.
    /// <para>
    /// Стек отмены библиотека не ведёт: она отдаёт поток изменений, а хранит его приложение.
    /// Вернуть состояние можно через <see cref="ApplyGeometry"/>.
    /// </para>
    /// </remarks>
    public event EventHandler<DesignEditCompletedEventArgs>? EditCompleted;

    /// <summary>
    /// Возникает при запросе удаления выделения с клавиатуры.
    /// </summary>
    /// <remarks>
    /// Редактор не владеет коллекцией элементов и удалять их не может: обработчик
    /// должен выполнить удаление сам и выставить
    /// <see cref="DesignEditorDeleteRequestedEventArgs.Handled"/>.
    /// </remarks>
    public event EventHandler<DesignEditorDeleteRequestedEventArgs>? DeleteRequested;

    /// <summary>
    /// Возникает, когда пользователь перетащил контрол на новое место среди соседей.
    /// </summary>
    /// <remarks>
    /// Деревом контролов редактор не владеет: структурную правку выполняет
    /// библиотека разметки. Обработчик должен переставить контрол сам и выставить
    /// <see cref="DesignEditorReorderRequestedEventArgs.Handled"/>.
    /// </remarks>
    public event EventHandler<DesignEditorReorderRequestedEventArgs>? ReorderRequested;

    /// <summary>
    /// Возникает при изменении набора выбранных design targets.
    /// </summary>
    /// <remarks>
    /// Это не то же, что унаследованное <see cref="SelectingItemsControl.SelectionChanged"/>:
    /// то работает на уровне элементов <c>ItemsSource</c>, а это — на уровне design targets,
    /// включая вложенные контролы и вложенные контейнеры.
    /// <para>
    /// Событие возникает только при фактической смене набора или primary target.
    /// Перетаскивание и изменение размера его не поднимают, хотя внутренний снимок
    /// пересобирается на каждом кадре.
    /// </para>
    /// </remarks>
    public event EventHandler<DesignSelectionChangedEventArgs>? DesignSelectionChanged;

    /// <summary>
    /// Получает или задает presenter контекстных действий.
    /// </summary>
    public IDesignEditorContextPresenter ContextPresenter { get; set; } = new ContextMenuContextPresenter();

    #endregion

    #region Internal Helpers

    private Point _lastMousePosition;
    internal KeyModifiers LastInputModifiers { get; private set; }

    private SelectionAdorner? _selectionAdorner;
    private SelectionAdorner? _groupSelectionAdorner;
    private SelectionAdornerLayer? _secondarySelectionAdornerLayer;
    private DesignGrid? _grid;
    private DesignEditorItem? _primarySelectionItem;
    private Control? _primarySelectionControl;
    // Выбранные design targets в порядке приоритета: первый — primary.
    // Контейнеры и вложенные контролы лежат вместе: контейнер, выбранный целиком,
    // это просто DesignEditorItem в списке. Владелец каждого target вычисляется
    // по дереву, поэтому структура не привязана к глубине вложенности.
    private readonly List<Control> _selectedTargets = new();

    // Targets, на изменения свойств которых редактор сейчас подписан.
    // Ведётся отдельно от _selectedTargets: следить нужно за разрешёнными
    // targets, включая default'ные для item'ов без явного выбора.
    private readonly List<Control> _subscribedTargets = new();
    private GroupResizeOperation? _groupResizeOperation;
    private GroupDragOperation? _groupDragOperation;

    // Текущая единица редактирования. Живёт от начала жеста до его завершения:
    // все мутации проходят через SetDesignPosition/SetDesignSize и попадают в неё.
    private DesignEditScope? _activeEdit;

    // Подавляет запись на время программного применения геометрии,
    // чтобы отмена не превращалась в новое изменение.
    private bool _suppressEditRecording;

    // Соседи, к которым идёт выравнивание в текущем жесте. Снимаются один раз
    // на входе в жест; null означает, что жест не идёт.
    private IReadOnlyList<Rect>? _snapGuideNeighbours;

    /// <summary>
    /// Снимок контейнеров на время жеста; <c>null</c> вне жеста.
    /// </summary>
    private IReadOnlyList<DesignEditorItem>? _containerSnapshot;

    private readonly TranslateTransform _translateTransform = new TranslateTransform();
    private readonly ScaleTransform _scaleTransform = new ScaleTransform();
    private readonly TranslateTransform _dpiTranslateTransform = new TranslateTransform();

    // TopLevel, с которого читается RenderScaling и на который подписан ScalingChanged.
    // Разрешается один раз при подключении к дереву, чтобы подписка и чтение DPI
    // не расходились между собой.
    private TopLevel? _scalingHost;

    #endregion

    static DesignEditor()
    {
        FocusableProperty.OverrideDefaultValue<DesignEditor>(true);
        ViewportLocationProperty.Changed.AddClassHandler<DesignEditor>((x, e) => x.UpdateTransforms());
        ViewportZoomProperty.Changed.AddClassHandler<DesignEditor>((x, e) => x.UpdateTransforms());

        DesignEditorItem.DragStartedEvent.AddClassHandler<DesignEditor>((x, e) => x.OnItemsDragStarted(e));
        DesignEditorItem.DragDeltaEvent.AddClassHandler<DesignEditor>((x, e) => x.OnItemsDragDelta(e));
        DesignEditorItem.DragCompletedEvent.AddClassHandler<DesignEditor>((x, e) => x.OnItemsDragCompleted(e));
        DesignEditorItem.ResizeDeltaEvent.AddClassHandler<DesignEditor>((x, e) => x.OnItemsResizeDelta(e));
        DesignEditorItem.IsSelectedProperty.Changed.AddClassHandler<DesignEditorItem>((item, _) =>
        {
            if (item.FindAncestorOfType<DesignEditor>() is { } editor)
                editor.UpdateSelectionOverlayState();
        });
        DesignEditorItem.LocationProperty.Changed.AddClassHandler<DesignEditorItem>((item, _) =>
        {
            if (item.IsSelected && item.FindAncestorOfType<DesignEditor>() is { } editor)
                editor.UpdateSelectionOverlayState();
        });
        // Геометрия и политики выбранных targets отслеживаются точечно —
        // подпиской на сами targets, см. SyncSelectedTargetSubscriptions.
        // Раньше здесь висели AddClassHandler<Control> на Bounds, DesignX/DesignY
        // и политики: они срабатывали на любой Control во всём приложении
        // и на каждое срабатывание поднимались по дереву в поисках редактора.
    }

    /// <summary>
    /// Инициализирует новый экземпляр <see cref="DesignEditor"/>.
    /// </summary>
    public DesignEditor()
    {
        SelectionMode = SelectionMode.Multiple;
        _inputGestures = new DesignEditorInputGestures();
        _containerInteractionModifiers = _inputGestures.ContainerInteractionModifiers;
        _additiveSelectionModifiers = _inputGestures.AdditiveSelectionModifiers;

        var contentGroup = new TransformGroup();
        contentGroup.Children.Add(_scaleTransform);
        contentGroup.Children.Add(_translateTransform);
        SetCurrentValue(ViewportTransformProperty, contentGroup);

        var dpiGroup = new TransformGroup();
        dpiGroup.Children.Add(_scaleTransform);
        dpiGroup.Children.Add(_dpiTranslateTransform);
        SetCurrentValue(DpiScaledViewportTransformProperty, dpiGroup);

        _states.Push(new EditorIdleState(this));
        UpdateSelectionOverlayState();
    }

    /// <summary>
    /// Определяет необходимость создания контейнера <see cref="DesignEditorItem"/> для элемента коллекции.
    /// </summary>
    /// <param name="item">Элемент источника данных.</param>
    /// <param name="index">Индекс элемента.</param>
    /// <param name="recycleKey">Ключ повторного использования контейнера.</param>
    /// <returns><see langword="true"/>, если для элемента требуется контейнер.</returns>
    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        return NeedsContainer<DesignEditorItem>(item, out recycleKey);
    }

    /// <summary>
    /// Создает контейнер визуального элемента редактора.
    /// </summary>
    /// <param name="item">Элемент источника данных.</param>
    /// <param name="index">Индекс элемента.</param>
    /// <param name="recycleKey">Ключ повторного использования контейнера.</param>
    /// <returns>Новый экземпляр <see cref="DesignEditorItem"/>.</returns>
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return new DesignEditorItem();
    }

    /// <summary>
    /// Применяет шаблон редактора и подключает overlay-элементы к обработчикам взаимодействия.
    /// </summary>
    /// <param name="e">Аргументы применения шаблона.</param>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        if (_selectionAdorner != null)
        {
            _selectionAdorner.ResizeStarted -= OnSelectionResizeStarted;
            _selectionAdorner.ResizeDelta -= OnSelectionResizeDelta;
            _selectionAdorner.ResizeCompleted -= OnSelectionResizeCompleted;
        }

        if (_groupSelectionAdorner != null)
        {
            _groupSelectionAdorner.ResizeStarted -= OnGroupSelectionResizeStarted;
            _groupSelectionAdorner.ResizeDelta -= OnGroupSelectionResizeDelta;
            _groupSelectionAdorner.ResizeCompleted -= OnGroupSelectionResizeCompleted;
        }

        if (_secondarySelectionAdornerLayer != null)
        {
            _secondarySelectionAdornerLayer.AdornerResizeStarted -= OnSecondarySelectionResizeStarted;
            _secondarySelectionAdornerLayer.AdornerResizeDelta -= OnSecondarySelectionResizeDelta;
            _secondarySelectionAdornerLayer.AdornerResizeCompleted -= OnSecondarySelectionResizeCompleted;
        }

        _grid = e.NameScope.Find<DesignGrid>("PART_Grid");
        _selectionAdorner = e.NameScope.Find<SelectionAdorner>("PART_SelectionAdorner");
        _groupSelectionAdorner = e.NameScope.Find<SelectionAdorner>("PART_GroupSelectionAdorner");
        _secondarySelectionAdornerLayer = e.NameScope.Find<SelectionAdornerLayer>("PART_SecondarySelectionAdorners");

        if (_selectionAdorner != null)
        {
            _selectionAdorner.ResizeStarted += OnSelectionResizeStarted;
            _selectionAdorner.ResizeDelta += OnSelectionResizeDelta;
            _selectionAdorner.ResizeCompleted += OnSelectionResizeCompleted;
        }

        if (_groupSelectionAdorner != null)
        {
            _groupSelectionAdorner.ResizeStarted += OnGroupSelectionResizeStarted;
            _groupSelectionAdorner.ResizeDelta += OnGroupSelectionResizeDelta;
            _groupSelectionAdorner.ResizeCompleted += OnGroupSelectionResizeCompleted;
        }

        if (_secondarySelectionAdornerLayer != null)
        {
            _secondarySelectionAdornerLayer.AdornerResizeStarted += OnSecondarySelectionResizeStarted;
            _secondarySelectionAdornerLayer.AdornerResizeDelta += OnSecondarySelectionResizeDelta;
            _secondarySelectionAdornerLayer.AdornerResizeCompleted += OnSecondarySelectionResizeCompleted;
        }

        UpdateSelectionAdornerPolicies();
    }

    /// <summary>
    /// Подключает обработчики, зависящие от visual tree, после присоединения редактора к дереву.
    /// </summary>
    /// <param name="e">Аргументы присоединения к visual tree.</param>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        // e.RootVisual в Avalonia 12 не гарантированно TopLevel, поэтому host ищется
        // подъемом по дереву, а не приведением корня.
        SetScalingHost(TopLevel.GetTopLevel(this));
        UpdateTransforms();
    }

    /// <summary>
    /// Освобождает обработчики, зависящие от visual tree, перед отсоединением редактора.
    /// </summary>
    /// <param name="e">Аргументы отсоединения от visual tree.</param>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        SetScalingHost(null);
    }

    private void SetScalingHost(TopLevel? topLevel)
    {
        if (ReferenceEquals(_scalingHost, topLevel))
            return;

        if (_scalingHost != null)
            _scalingHost.ScalingChanged -= OnScreenScalingChanged;

        _scalingHost = topLevel;

        if (_scalingHost != null)
            _scalingHost.ScalingChanged += OnScreenScalingChanged;
    }

    private void OnScreenScalingChanged(object? sender, EventArgs e) => UpdateTransforms();

    private void UpdateTransforms()
    {
        _scaleTransform.ScaleX = ViewportZoom;
        _scaleTransform.ScaleY = ViewportZoom;

        double x = -ViewportLocation.X * ViewportZoom;
        double y = -ViewportLocation.Y * ViewportZoom;

        _translateTransform.X = x;
        _translateTransform.Y = y;

        // В Avalonia 12 IRenderRoot больше не публичный: RenderScaling берется с TopLevel.
        // Проверка через "не > 0" отсекает и 0, и NaN: иначе деление ниже дало бы
        // нечисловой transform для фона и сетки.
        double renderScaling = _scalingHost?.RenderScaling ?? 1.0;
        if (!(renderScaling > 0))
            renderScaling = 1.0;

        _dpiTranslateTransform.X = Math.Round(x * renderScaling) / renderScaling;
        _dpiTranslateTransform.Y = Math.Round(y * renderScaling) / renderScaling;

        // Группы собраны в конструкторе и содержат эти же трансформации, поэтому
        // мутации выше видны через них сразу. Пересобирать их заново на каждом кадре
        // приходилось только ради обратного масштаба оверлеев: тот конвертер снимал
        // матрицу в момент преобразования и перевычислялся лишь при смене
        // идентичности значения. Теперь оверлеи привязаны к ViewportZoom напрямую.
    }

    /// <summary>
    /// Преобразует экранную точку в мировые координаты холста.
    /// </summary>
    /// <param name="screenPoint">Точка в координатах контрола.</param>
    /// <returns>Точка в координатах содержимого редактора.</returns>
    /// <example>
    /// Это полезно, когда нужно разместить новый элемент в позиции курсора с учетом текущего зума и панорамирования.
    /// </example>
    public Point GetWorldPosition(Point screenPoint)
        => (screenPoint / ViewportZoom) + ViewportLocation;

    /// <summary>
    /// Возвращает последнюю известную позицию указателя для текущего ввода.
    /// </summary>
    /// <param name="relativeTo">Параметр сохранен для совместимости с будущими реализациями.</param>
    /// <returns>Последняя позиция указателя в координатах редактора.</returns>
    public Point GetPositionForInput(Visual relativeTo)
        => _lastMousePosition;

    /// <summary>
    /// Выполняет масштабирование относительно текущей позиции курсора.
    /// </summary>
    /// <param name="e">Аргументы колесика мыши.</param>
    public void HandleZoom(PointerWheelEventArgs e) => TryHandleZoom(e);

    /// <summary>
    /// Масштабирует viewport и сообщает, состоялось ли масштабирование.
    /// </summary>
    /// <remarks>
    /// Публичный <see cref="HandleZoom"/> остаётся void ради совместимости;
    /// ответ нужен только редактору, чтобы решить судьбу <c>e.Handled</c>.
    /// </remarks>
    internal bool TryHandleZoom(PointerWheelEventArgs e)
    {
        if (!ShouldHandleZoom(e.KeyModifiers))
            return false;

        var zoomStep = InteractionOptions.ZoomStep > 1.0 ? InteractionOptions.ZoomStep : 1.1;
        double prevZoom = ViewportZoom;
        double newZoom = e.Delta.Y > 0 ? prevZoom * zoomStep : prevZoom / zoomStep;
        newZoom = Math.Max(GetValue(MinZoomProperty), Math.Min(GetValue(MaxZoomProperty), newZoom));

        if (Math.Abs(newZoom - prevZoom) > ZoomTolerance)
        {
            Point mousePos = e.GetPosition(this);
            Vector correction = (Vector)mousePos / prevZoom - (Vector)mousePos / newZoom;
            ViewportZoom = newZoom;
            ViewportLocation += correction;
        }

        // Колесо потреблено даже когда масштаб упёрся в Min/Max: жест был наш,
        // и отдавать его наружу на границе диапазона значило бы, что у края
        // зума страница вдруг начинает прокручиваться.
        return true;
    }

    /// <summary>
    /// Смещает viewport так, чтобы указанная мировая точка оказалась в центре видимой области редактора.
    /// </summary>
    /// <param name="worldPoint">Точка в координатах содержимого редактора.</param>
    /// <remarks>
    /// Метод не изменяет <see cref="ViewportZoom"/> и пересчитывает только <see cref="ViewportLocation"/>.
    /// </remarks>
    public void CenterOn(Point worldPoint)
    {
        var visibleWorldSize = new Size(Bounds.Width / ViewportZoom, Bounds.Height / ViewportZoom);
        ViewportLocation = new Point(
            worldPoint.X - (visibleWorldSize.Width / 2),
            worldPoint.Y - (visibleWorldSize.Height / 2));
    }

    /// <summary>
    /// Смещает viewport так, чтобы центр указанной области оказался в центре видимой области редактора.
    /// </summary>
    /// <param name="bounds">Прямоугольная область в мировых координатах.</param>
    /// <remarks>
    /// Метод не изменяет <see cref="ViewportZoom"/> и использует геометрический центр переданного прямоугольника.
    /// </remarks>
    public void CenterOn(Rect bounds)
    {
        CenterOn(bounds.Center);
    }

    /// <summary>
    /// Смещает viewport так, чтобы указанный элемент оказался в центре видимой области редактора.
    /// </summary>
    /// <param name="item">Элемент, который необходимо центрировать в области просмотра.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="item"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Выбрасывается, если <paramref name="item"/> не принадлежит текущему экземпляру <see cref="DesignEditor"/>.
    /// </exception>
    /// <remarks>
    /// Метод изменяет только <see cref="ViewportLocation"/> и не изменяет <see cref="ViewportZoom"/>.
    /// <para>
    /// Если размер элемента превышает размер видимой области, элемент не масштабируется и не вписывается целиком:
    /// в центр видимой области помещается только геометрический центр элемента.
    /// </para>
    /// <para>
    /// Метод использует текущие <see cref="DesignEditorItem.Location"/> и <see cref="Visual.Bounds"/> элемента.
    /// Для корректного результата элемент должен принадлежать текущему редактору и иметь актуальный layout.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.CenterOnItem(container);
    /// ]]></code>
    /// </example>
    public void CenterOnItem(DesignEditorItem item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        if (!ReferenceEquals(item.FindAncestorOfType<DesignEditor>(), this))
            throw new InvalidOperationException("The specified item does not belong to this DesignEditor.");

        if (TryGetDesignBounds(item, out var bounds))
        {
            CenterOn(bounds.Center);
            return;
        }

        var fallbackCenter = new Point(
            item.Location.X + (item.Bounds.Width / 2),
            item.Location.Y + (item.Bounds.Height / 2));

        CenterOn(fallbackCenter);
    }

    /// <summary>
    /// Изменяет положение и масштаб viewport так, чтобы указанная область целиком поместилась в видимой области редактора.
    /// </summary>
    /// <param name="bounds">Прямоугольная область в мировых координатах, которую необходимо вписать в окно.</param>
    /// <remarks>
    /// Метод изменяет <see cref="ViewportLocation"/> и <see cref="ViewportZoom"/>.
    /// <para>
    /// Для более аккуратного отображения вокруг области добавляется внутренний отступ.
    /// Итоговый масштаб ограничивается значениями <see cref="MinZoom"/> и <see cref="MaxZoom"/>.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.FitToView(new Rect(100, 100, 640, 360));
    /// ]]></code>
    /// </example>
    public void FitToView(Rect bounds)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var paddedBounds = bounds.Inflate(FitToViewPadding);
        var targetWidth = Math.Max(1.0, paddedBounds.Width);
        var targetHeight = Math.Max(1.0, paddedBounds.Height);

        var zoomX = Bounds.Width / targetWidth;
        var zoomY = Bounds.Height / targetHeight;
        var newZoom = Math.Min(zoomX, zoomY);
        newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));

        ViewportZoom = newZoom;
        CenterOn(paddedBounds.Center);
    }

    /// <summary>
    /// Изменяет положение и масштаб viewport так, чтобы указанный элемент целиком поместился в видимой области редактора.
    /// </summary>
    /// <param name="item">Элемент, который необходимо вписать в окно редактора.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="item"/> равен <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Выбрасывается, если <paramref name="item"/> не принадлежит текущему экземпляру <see cref="DesignEditor"/>.
    /// </exception>
    /// <remarks>
    /// Метод использует текущие <see cref="DesignEditorItem.Location"/> и <see cref="Visual.Bounds"/> элемента
    /// и делегирует расчет геометрии перегрузке <see cref="FitToView(Rect)"/>.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// editor.FitToView(container);
    /// ]]></code>
    /// </example>
    public void FitToView(DesignEditorItem item)
    {
        if (item == null)
            throw new ArgumentNullException(nameof(item));

        if (!ReferenceEquals(item.FindAncestorOfType<DesignEditor>(), this))
            throw new InvalidOperationException("The specified item does not belong to this DesignEditor.");

        if (TryGetDesignBounds(item, out var bounds))
        {
            FitToView(bounds);
            return;
        }

        FitToView(new Rect(item.Location, item.Bounds.Size));
    }

    /// <summary>
    /// Смещает viewport так, чтобы центр области, охватывающей все выбранные элементы, оказался в центре видимой области редактора.
    /// </summary>
    /// <remarks>
    /// Метод не изменяет <see cref="ViewportZoom"/>. Если в редакторе нет выбранных элементов, вызов игнорируется.
    /// </remarks>
    public void CenterOnSelection()
    {
        if (TryGetSelectedDesignBounds(out var bounds, out _, out _, out _, out _, out _, out _))
            CenterOn(bounds);
    }

    /// <summary>
    /// Изменяет положение и масштаб viewport так, чтобы все выбранные элементы целиком поместились в видимой области редактора.
    /// </summary>
    /// <remarks>
    /// Если в редакторе нет выбранных элементов, вызов игнорируется.
    /// </remarks>
    public void FitSelectionToView()
    {
        if (TryGetSelectedDesignBounds(out var bounds, out _, out _, out _, out _, out _, out _))
            FitToView(bounds);
    }

    private bool TryGetSelectedDesignBounds(
        out Rect bounds,
        out int selectedCount,
        out DesignEditorItem? primaryItem,
        out Control? primaryControl,
        out IReadOnlyList<SelectionAdornerInfo> secondaryAdorners,
        out bool hasMultipleNestedSelection,
        out bool hasMultipleContainerSelection)
    {
        bounds = default;
        selectedCount = 0;
        primaryItem = null;
        primaryControl = null;
        secondaryAdorners = Array.Empty<SelectionAdornerInfo>();
        hasMultipleNestedSelection = false;
        hasMultipleContainerSelection = false;

        var items = SelectedItems;
        if (items == null || items.Count == 0)
            return false;

        var perTargetBounds = new List<SelectionAdornerInfo>();
        var hasBounds = false;
        var containerTargetCount = 0;
        var nestedTargetCount = 0;
        double left = 0;
        double top = 0;
        double right = 0;
        double bottom = 0;

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null)
                continue;

            foreach (var selectionTarget in ResolveSelectionTargets(container))
            {
                if (!TryGetDesignBounds(selectionTarget, out var itemBounds))
                    continue;

                selectedCount++;
                primaryItem ??= container;
                primaryControl ??= selectionTarget;

                // Контейнером считается любой DesignEditorItem, а не только владелец:
                // вложенные контейнеры должны попадать в группу контейнеров,
                // иначе их множественный выбор рисуется как nested-группа.
                if (selectionTarget is DesignEditorItem)
                    containerTargetCount++;
                else
                    nestedTargetCount++;

                perTargetBounds.Add(new SelectionAdornerInfo
                {
                    Container = container,
                    Target = selectionTarget,
                    Bounds = itemBounds,
                    Role = SelectionAdornerRole.Secondary,
                    ResizePolicy = GetResizePolicy(selectionTarget),
                    MovePolicy = GetEffectiveMovePolicy(selectionTarget)
                });

                if (!hasBounds)
                {
                    left = itemBounds.Left;
                    top = itemBounds.Top;
                    right = itemBounds.Right;
                    bottom = itemBounds.Bottom;
                    hasBounds = true;
                    continue;
                }

                left = Math.Min(left, itemBounds.Left);
                top = Math.Min(top, itemBounds.Top);
                right = Math.Max(right, itemBounds.Right);
                bottom = Math.Max(bottom, itemBounds.Bottom);
            }
        }

        if (!hasBounds)
            return false;

        bounds = new Rect(left, top, right - left, bottom - top);
        hasMultipleContainerSelection = selectedCount > 1 && containerTargetCount == selectedCount;
        hasMultipleNestedSelection = selectedCount > 1 && nestedTargetCount == selectedCount;

        if (hasMultipleNestedSelection)
        {
            foreach (var adorner in perTargetBounds)
            {
                adorner.ShowHandles = true;
                adorner.IsInteractive = true;
            }
        }

        secondaryAdorners = hasMultipleNestedSelection ? perTargetBounds : Array.Empty<SelectionAdornerInfo>();
        return true;
    }

    internal void UpdateSelectionOverlayState()
    {
        if (TryGetSelectedDesignBounds(
                out var bounds,
                out var selectedCount,
                out var primaryItem,
                out var primaryControl,
                out var secondaryAdorners,
                out var hasMultipleNestedSelection,
                out var hasMultipleContainerSelection))
        {
            CleanupSelectionTargets();
            SelectionBounds = bounds;
            SecondarySelectionAdorners = secondaryAdorners;
            HasSingleSelection = selectedCount == 1;
            HasMultipleSelection = selectedCount > 1;
            HasMultipleNestedSelection = hasMultipleNestedSelection;
            HasMultipleContainerSelection = hasMultipleContainerSelection;
            _primarySelectionItem = primaryItem;
            _primarySelectionControl = primaryControl;
            UpdatePrimaryPlacementReadout(primaryControl);
            ApplySelectionSnapshot(CreateSelectionTargetsSnapshot(primaryItem, primaryControl));
            SyncSelectedTargetSubscriptions();
            UpdateSelectionAdornerPolicies();
            return;
        }

        _selectedTargets.Clear();
        SelectionBounds = default;
        SecondarySelectionAdorners = Array.Empty<SelectionAdornerInfo>();
        HasSingleSelection = false;
        HasMultipleSelection = false;
        HasMultipleNestedSelection = false;
        HasMultipleContainerSelection = false;
        _primarySelectionItem = null;
        _primarySelectionControl = null;
        UpdatePrimaryPlacementReadout(null);
        ApplySelectionSnapshot(Array.Empty<DesignSelectionTarget>());
        SyncSelectedTargetSubscriptions();
        UpdateSelectionAdornerPolicies();
    }

    internal void CommitSelection(Rect bounds, bool isCtrlPressed)
        => CommitSelection(bounds, isCtrlPressed, ShouldUseContainerInteraction(LastInputModifiers));

    /// <summary>
    /// Применяет выделение по прямоугольнику рамки.
    /// </summary>
    /// <param name="bounds">Прямоугольник в мировых координатах.</param>
    /// <param name="isCtrlPressed">Признак добавления к текущему выделению.</param>
    /// <param name="useContainerSelection">
    /// Признак работы на уровне контейнеров. Передаётся явно, а не читается из
    /// <see cref="LastInputModifiers"/>: режим фиксируется в момент нажатия, иначе
    /// отпускание модификатора посреди протяжки меняло бы смысл начатого жеста.
    /// </param>
    internal void CommitSelection(Rect bounds, bool isCtrlPressed, bool useContainerSelection)
    {
        if (Presenter?.Panel == null) return;

        // Владелец определяется итоговым прямоугольником — тем же правилом,
        // по которому во время протяжки обновлялся MarqueeScope.
        var marqueeOwner = useContainerSelection ? null : FindContainerForMarquee(bounds);

        // Владелец рамки может быть вложенным, а индексный выбор работает только
        // с item'ами верхнего уровня — сверять и выбирать нужно владеющий item.
        var marqueeOwnerItem = marqueeOwner != null ? ResolveOwningItem(marqueeOwner) : null;

        if (!useContainerSelection && isCtrlPressed && marqueeOwnerItem != null && !CanAddNestedTargetToContainer(marqueeOwnerItem))
            return;

        using (Selection.BatchUpdate())
        {
            if (!isCtrlPressed)
            {
                Selection.Clear();
                // Целевой набор накапливается по контейнерам, поэтому чистится
                // один раз здесь, а не внутри каждой итерации.
                _selectedTargets.Clear();
            }

            if (marqueeOwner != null)
            {
                // Рамка попала внутрь конкретного контейнера — работаем в его пределах.
                var selectedAny = CommitMarqueeWithinContainer(marqueeOwner, marqueeOwnerItem, bounds, isCtrlPressed);

                // Пустая рамка — это клик по пустой области контейнера,
                // и он должен выбрать сам контейнер, как и до появления рамки внутри.
                if (!selectedAny && !isCtrlPressed)
                {
                    var ownerIndex = IndexFromContainer(marqueeOwnerItem ?? marqueeOwner);
                    if (ownerIndex >= 0)
                    {
                        SetSingleSelectedTarget(marqueeOwner);
                        Selection.Select(ownerIndex);
                    }
                }
            }
            else
            {
                foreach (var child in Presenter.Panel.Children)
                {
                    if (child is not DesignEditorItem container)
                        continue;

                    if (useContainerSelection)
                    {
                        if (!TryGetContainerWorldBounds(container, out var containerBounds) ||
                            !bounds.Intersects(containerBounds))
                            continue;

                        AddSelectedTarget(container);
                        Selection.Select(IndexFromContainer(container));
                        continue;
                    }

                    CommitMarqueeWithinContainer(container, container, bounds, isCtrlPressed);
                }
            }
        }

        UpdateSelectionOverlayState();
    }

    /// <summary>
    /// Выбирает design targets внутри <paramref name="scope"/>, попавшие в рамку.
    /// </summary>
    /// <param name="scope">Контейнер, в пределах которого ищутся targets. Может быть вложенным.</param>
    /// <param name="ownerItem">Item верхнего уровня, на который адресуется индексный выбор.</param>
    /// <param name="bounds">Прямоугольник рамки в мировых координатах.</param>
    /// <param name="isAdditive">Признак добавления к текущему выбору.</param>
    /// <returns><see langword="true"/>, если хотя бы один target попал в выделение.</returns>
    private bool CommitMarqueeWithinContainer(
        DesignEditorItem scope,
        DesignEditorItem? ownerItem,
        Rect bounds,
        bool isAdditive)
    {
        ownerItem ??= ResolveOwningItem(scope);
        if (ownerItem == null)
            return false;

        var nestedTargets = new List<Control>();
        foreach (var target in EnumerateSelectionCandidates(scope))
        {
            if (!IsSelectableTarget(target, scope))
                continue;

            // Рамка выбирает соседей внутри одного host'а, а не смесь уровней:
            // иначе в выборку попадали бы и вложенный контейнер, и его содержимое.
            if (!ReferenceEquals(FindDesignHost(target), scope))
                continue;

            if (TryGetDesignBounds(target, out var targetBounds) && bounds.Intersects(targetBounds))
                nestedTargets.Add(target);
        }

        if (nestedTargets.Count == 0)
            return false;

        foreach (var target in nestedTargets)
            AddSelectedTarget(target);

        Selection.Select(IndexFromContainer(ownerItem));
        return true;
    }

    /// <summary>
    /// Держит подписки на свойства текущих selection targets в актуальном состоянии.
    /// </summary>
    /// <remarks>
    /// Подписка идёт на разрешённые targets из <see cref="SelectedDesignTargets"/>,
    /// а не на <c>_selectedTargets</c>: если у выбранного item'а нет явного target,
    /// его геометрию задаёт default target, и следить нужно за ним.
    /// </remarks>
    private void SyncSelectedTargetSubscriptions()
    {
        var targets = SelectedDesignTargets;

        for (var i = _subscribedTargets.Count - 1; i >= 0; i--)
        {
            var subscribed = _subscribedTargets[i];

            var stillSelected = false;
            for (var j = 0; j < targets.Count; j++)
            {
                if (ReferenceEquals(targets[j].Target, subscribed))
                {
                    stillSelected = true;
                    break;
                }
            }

            if (stillSelected)
                continue;

            subscribed.PropertyChanged -= OnSelectedTargetPropertyChanged;
            _subscribedTargets.RemoveAt(i);
        }

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i].Target;
            if (_subscribedTargets.Contains(target))
                continue;

            target.PropertyChanged += OnSelectedTargetPropertyChanged;
            _subscribedTargets.Add(target);
        }
    }

    private void OnSelectedTargetPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == BoundsProperty ||
            e.Property == DesignLayout.DesignXProperty ||
            e.Property == DesignLayout.DesignYProperty ||
            e.Property == DesignInteraction.ResizePolicyProperty ||
            e.Property == DesignInteraction.MovePolicyProperty)
        {
            UpdateSelectionOverlayState();
        }
    }

    private void AddSelectedTarget(Control target)
    {
        if (!_selectedTargets.Contains(target))
            _selectedTargets.Add(target);
    }

    private void SetSingleSelectedTarget(Control target)
    {
        _selectedTargets.Clear();
        _selectedTargets.Add(target);
    }

    /// <summary>
    /// Добавляет target в выделение или убирает его оттуда.
    /// </summary>
    /// <remarks>
    /// Повторный additive-клик снимает target, но не даёт опустошить выделение
    /// полностью: пустой выбор — это результат клика по холсту, а не по элементу.
    /// </remarks>
    private void ToggleTargetInSelection(Control target)
    {
        if (!_selectedTargets.Contains(target))
        {
            _selectedTargets.Add(target);
            return;
        }

        if (_selectedTargets.Count > 1)
            _selectedTargets.Remove(target);
    }

    /// <summary>
    /// Приводит индексную модель Avalonia в соответствие со слоем design target'ов.
    /// </summary>
    /// <remarks>
    /// Оверлей обходит <c>SelectedItems</c> и для каждого item'а спрашивает его
    /// targets. Контейнер, попавший в <c>Selection</c> без собственного target'а,
    /// подменяется вложенным по умолчанию — и группа контейнеров превращается
    /// в смешанную. Поэтому оба слоя обязаны меняться вместе.
    /// </remarks>
    private void SyncContainerItemSelection(DesignEditorItem container)
    {
        var index = IndexFromContainer(container);
        if (index < 0)
            return;

        if (_selectedTargets.Contains(container))
            Selection.Select(index);
        else
            Selection.Deselect(index);
    }

    /// <summary>
    /// Возвращает item верхнего уровня, которому принадлежит target.
    /// </summary>
    private DesignEditorItem? ResolveOwningItemForTarget(Control target)
    {
        var container = target as DesignEditorItem ?? FindDesignHost(target);
        return container == null ? null : ResolveOwningItem(container);
    }

    private void UpdateSelectionAdornerPolicies()
    {
        var primaryResizePolicy = _primarySelectionControl != null
            ? GetResizePolicy(_primarySelectionControl)
            : ArxisStudio.Attached.ResizePolicy.None;
        var primaryMovePolicy = _primarySelectionControl != null
            ? GetEffectiveMovePolicy(_primarySelectionControl)
            : ArxisStudio.Attached.MovePolicy.None;

        if (_selectionAdorner != null)
        {
            _selectionAdorner.ResizePolicy = primaryResizePolicy;
            _selectionAdorner.MovePolicy = primaryMovePolicy;
        }

        var groupResizePolicy = ArxisStudio.Attached.ResizePolicy.None;
        var groupMovePolicy = ArxisStudio.Attached.MovePolicy.None;
        if (HasMultipleContainerSelection && SelectedDesignTargets.Count > 1)
        {
            groupResizePolicy = ArxisStudio.Attached.ResizePolicy.All;
            groupMovePolicy = ArxisStudio.Attached.MovePolicy.Both;

            foreach (var selectedTarget in SelectedDesignTargets)
            {
                groupResizePolicy &= GetResizePolicy(selectedTarget.Target);
                groupMovePolicy &= GetEffectiveMovePolicy(selectedTarget.Target);
            }
        }

        if (_groupSelectionAdorner != null)
        {
            _groupSelectionAdorner.ResizePolicy = groupResizePolicy;
            _groupSelectionAdorner.MovePolicy = groupMovePolicy;
        }
    }

    /// <summary>
    /// Запрашивает контекстное меню программно.
    /// </summary>
    /// <param name="source">Источник запроса.</param>
    /// <param name="viewportPoint">Точка в координатах DesignEditor.</param>
    /// <param name="modifiers">Модификаторы ввода.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    public Task RequestContextAsync(
        DesignEditorContextSource source,
        Point viewportPoint,
        KeyModifiers modifiers = KeyModifiers.None,
        CancellationToken cancellationToken = default)
    {
        var request = BuildContextRequest(source, viewportPoint, modifiers);
        return HandleContextRequestAsync(request, cancellationToken);
    }

    /// <summary>
    /// Запрашивает контекстное меню программно в позиции последнего ввода.
    /// </summary>
    public Task RequestContextAsync(CancellationToken cancellationToken = default)
    {
        var request = BuildContextRequest(DesignEditorContextSource.Programmatic, _lastMousePosition, LastInputModifiers);
        return HandleContextRequestAsync(request, cancellationToken);
    }

    private async Task HandleContextRequestAsync(DesignEditorContextRequest request, CancellationToken cancellationToken)
    {
        var resolvedActions = await ResolveContextActionsAsync(request, cancellationToken);
        var requestingArgs = new DesignEditorContextRequestingEventArgs(request)
        {
            Actions = resolvedActions
        };

        ContextMenuRequesting?.Invoke(this, requestingArgs);
        if (requestingArgs.Cancel)
            return;

        var actions = requestingArgs.Actions ?? Array.Empty<DesignEditorContextAction>();
        var handled = requestingArgs.Handled;
        if (!handled && actions.Count > 0)
            handled = ContextPresenter.TryShow(this, request, actions);

        ContextMenuResolved?.Invoke(this, new DesignEditorContextRequestedEventArgs(request, actions, handled));
    }

    private async Task<IReadOnlyList<DesignEditorContextAction>> ResolveContextActionsAsync(
        DesignEditorContextRequest request,
        CancellationToken cancellationToken)
    {
        if (ContextActionProviders.Count == 0)
            return Array.Empty<DesignEditorContextAction>();

        var result = new List<DesignEditorContextAction>();
        foreach (var provider in ContextActionProviders)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var actions = await provider.GetActionsAsync(this, request, cancellationToken);
            if (actions == null || actions.Count == 0)
                continue;

            foreach (var action in actions)
            {
                if (action.IsVisible)
                    result.Add(action);
            }
        }

        return result
            .OrderBy(static a => a.Group, StringComparer.Ordinal)
            .ThenBy(static a => a.Order)
            .ToArray();
    }

    private DesignEditorContextRequest BuildContextRequest(
        DesignEditorContextSource source,
        Point viewportPoint,
        KeyModifiers modifiers)
    {
        var worldPoint = GetWorldPosition(viewportPoint);
        var hasHitTarget = TryResolveContextTarget(worldPoint, out var hitTarget);
        var selection = SelectedDesignTargets;
        var scope = DesignEditorContextScope.Surface;
        var topLevel = TopLevel.GetTopLevel(this);

        if (selection.Count > 1 &&
            hitTarget != null &&
            selection.Any(selected => ReferenceEquals(selected.Target, hitTarget.Target)))
        {
            scope = DesignEditorContextScope.Selection;
        }
        else if (hasHitTarget && hitTarget != null)
        {
            scope = hitTarget.Scope == DesignSelectionScope.Container
                ? DesignEditorContextScope.Container
                : DesignEditorContextScope.NestedTarget;
        }

        return new DesignEditorContextRequest
        {
            Scope = scope,
            Target = hitTarget,
            Selection = selection,
            WorldPoint = worldPoint,
            ViewportPoint = viewportPoint,
            ScreenPoint = topLevel?.PointToScreen(viewportPoint) ?? default,
            Modifiers = modifiers,
            Source = source
        };
    }

    private bool TryResolveContextTarget(Point worldPoint, out DesignSelectionTarget? target)
    {
        target = null;
        var container = FindContainerAtWorldPoint(worldPoint);
        if (container == null)
            return false;

        Control? bestMatch = null;
        Rect bestBounds = default;
        var bestDepth = -1;

        foreach (var control in EnumerateSelectionCandidates(container))
        {
            if (!IsSelectableTarget(control, container))
                continue;

            if (!TryGetDesignBounds(control, out var bounds) || !bounds.Contains(worldPoint))
                continue;

            var depth = GetVisualDepth(control, container);
            if (bestMatch == null ||
                depth > bestDepth ||
                (depth == bestDepth && bounds.Width * bounds.Height < bestBounds.Width * bestBounds.Height))
            {
                bestMatch = control;
                bestBounds = bounds;
                bestDepth = depth;
            }
        }

        // Container в контракте target — это владеющий item верхнего уровня,
        // согласованно со snapshot'ом выделения; глубина передаётся через Depth.
        var resolvedTarget = (Control?)bestMatch ?? container;
        var ownerItem = ResolveOwningItem(container) ?? container;
        target = new DesignSelectionTarget(ownerItem, resolvedTarget);
        return true;
    }

    // --- Input Handling ---

    /// <summary>
    /// Обрабатывает нажатие указателя и маршрутизирует его в active state редактора.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _lastMousePosition = e.GetPosition(this);
        LastInputModifiers = e.KeyModifiers;

        // Focusable сам по себе фокус не даёт: без него клавиатурные жесты
        // до редактора не доходят. Проверка IsKeyboardFocusWithin не даёт
        // отобрать фокус у вложенного редактируемого контрола.
        if (!IsKeyboardFocusWithin)
            Focus();

        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            RetargetSelectionForContext(_lastMousePosition, e.KeyModifiers);
            RequestContextSafe(DesignEditorContextSource.Pointer, _lastMousePosition, e.KeyModifiers);
            e.Handled = true;
            return;
        }

        CurrentState.OnPointerPressed(e);

        if (!e.Handled) base.OnPointerPressed(e);
    }

    /// <summary>
    /// Обрабатывает нажатие клавиши: смещение выделения, снятие выделения,
    /// выбор всего и запрос удаления.
    /// </summary>
    /// <param name="e">Аргументы клавиатуры.</param>
    /// <remarks>
    /// Уже обработанные нажатия пропускаются: если фокус во вложенном редактируемом
    /// контроле, стрелки и Delete принадлежат ему, а не редактору.
    /// </remarks>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;

        switch (e.Key)
        {
            case Key.Left:
            case Key.Right:
            case Key.Up:
            case Key.Down:
                e.Handled = TryNudgeSelection(e.Key, e.KeyModifiers);
                break;

            case Key.Escape:
                e.Handled = TryClearSelection();
                break;

            case Key.Delete:
            case Key.Back:
                e.Handled = TryRequestDelete();
                break;

            case Key.A when ShouldUseContainerInteraction(e.KeyModifiers):
                e.Handled = TrySelectAll();
                break;
        }
    }

    private bool TryNudgeSelection(Key key, KeyModifiers modifiers)
    {
        var targets = SelectedDesignTargets;
        if (targets.Count == 0)
            return false;

        var step = MatchesModifiers(modifiers, InputGestures.LargeNudgeModifiers)
            && InputGestures.LargeNudgeModifiers != KeyModifiers.None
                ? InteractionOptions.LargeNudgeStep
                : InteractionOptions.NudgeStep;

        var delta = key switch
        {
            Key.Left => new Vector(-step, 0),
            Key.Right => new Vector(step, 0),
            Key.Up => new Vector(0, -step),
            Key.Down => new Vector(0, step),
            _ => default
        };

        if (delta.X == 0 && delta.Y == 0)
            return false;

        // Одно нажатие — одна единица редактирования, как и одно перетаскивание.
        BeginEdit(DesignEditKind.Move);

        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i].Target;
            var filtered = ApplyMovePolicy(target, delta);
            if (filtered.X == 0 && filtered.Y == 0)
                continue;

            SetDesignPosition(target, GetDesignPosition(target) + filtered);
        }

        CommitEdit();
        UpdateSelectionOverlayState();
        return true;
    }

    private bool TryClearSelection()
    {
        if (SelectedDesignTargets.Count == 0)
            return false;

        Selection.Clear();
        _selectedTargets.Clear();
        UpdateSelectionOverlayState();
        return true;
    }

    private bool TrySelectAll()
    {
        if (ItemCount == 0)
            return false;

        using (Selection.BatchUpdate())
        {
            Selection.Clear();
            Selection.SelectAll();
        }

        // Выбор всего работает на уровне контейнеров: это единица документа.
        _selectedTargets.Clear();
        if (Presenter?.Panel != null)
        {
            foreach (var child in Presenter.Panel.Children)
            {
                if (child is DesignEditorItem container)
                    AddSelectedTarget(container);
            }
        }

        UpdateSelectionOverlayState();
        return true;
    }

    private bool TryRequestDelete()
    {
        var targets = SelectedDesignTargets;
        if (targets.Count == 0)
            return false;

        var handler = DeleteRequested;
        if (handler == null)
            return false;

        var args = new DesignEditorDeleteRequestedEventArgs(targets);

        // Обработчики обходятся по одному, и первый же выполнивший удаление
        // останавливает обход. Список targets снят до правки, поэтому следующему
        // он описывал бы выделение, которого уже нет. Ровно это было исправлено
        // для ReorderRequested и не было исправлено здесь.
        foreach (var invocation in handler.GetInvocationList())
        {
            ((EventHandler<DesignEditorDeleteRequestedEventArgs>)invocation)(this, args);

            if (args.Handled)
                break;
        }

        return args.Handled;
    }

    /// <summary>
    /// Обрабатывает перемещение указателя и обновляет последнюю известную позицию курсора.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        _lastMousePosition = e.GetPosition(this);
        CurrentState.OnPointerMoved(e);
        base.OnPointerMoved(e);
    }

    /// <summary>
    /// Обрабатывает отпускание указателя и завершает текущее interaction-состояние при необходимости.
    /// </summary>
    /// <param name="e">Аргументы указателя.</param>
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        CurrentState.OnPointerReleased(e);
        base.OnPointerReleased(e);
    }

    /// <summary>
    /// Разбирает стек состояний, если редактор потерял захват указателя.
    /// </summary>
    /// <param name="e">Аргументы потери захвата.</param>
    /// <remarks>
    /// Рамка выделения и панорамирование выходят только через отпускание. Отпускание
    /// доходит почти всегда, но захват можно и потерять — его забирает другой элемент,
    /// захваченный уходит из дерева, платформа отбирает сама. Тогда состояние остаётся
    /// на стеке, и это не косметика: брошенная рамка держит <see cref="IsSelecting"/>,
    /// а по нему <c>OnItemsDragStarted</c> отклоняет следующее перетаскивание — при том
    /// что контейнер об отказе не узнаёт и продолжает писать геометрию каждый кадр
    /// с закрытой единицей редактирования. Правка уходила мимо undo.
    /// <para>
    /// Тот же приём, что у контейнера (<c>DesignEditorItem.OnPointerCaptureLost</c>):
    /// разобрать стек до базового состояния, дав каждому выйти своим <c>Exit</c>.
    /// </para>
    /// </remarks>
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);

        while (_states.Count > 1)
            PopState();
    }

    /// <summary>
    /// Обрабатывает колесо мыши и делегирует управление активному состоянию редактора.
    /// </summary>
    /// <param name="e">Аргументы колесика мыши.</param>
    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.Handled) return;

        // Помечать обработанным можно только то, что действительно потребили.
        // Безусловный Handled съедал колесо и тогда, когда зум не сработал —
        // заданы ZoomModifiers, но не нажаты, — и внешний ScrollViewer,
        // внутри которого лежит редактор, переставал прокручиваться вовсе.
        e.Handled = CurrentState.OnPointerWheelChanged(e);
    }

    /// <summary>
    /// Запускает запрос контекста, не дожидаясь его завершения.
    /// </summary>
    /// <remarks>
    /// Указатель ждать не может: обработчик нажатия обязан вернуться сразу. Отсюда
    /// два следствия, которых раньше не было.
    /// <para>
    /// Новый запрос отменяет предыдущий. Провайдер объявлен асинхронным, значит он
    /// вправе ходить в свою модель; два правых клика подряд доводили до конца оба
    /// запроса, и меню разрешалось дважды — второй показ поверх первого.
    /// </para>
    /// <para>
    /// Упавший провайдер больше не исчезает молча. Публичного события об ошибке
    /// здесь нет намеренно — контракт провайдера асинхронный, и ловить свои
    /// исключения хост умеет сам, — но в лог сообщение уходит, иначе отладка
    /// сводится к «меню не открылось, и никаких следов».
    /// </para>
    /// </remarks>
    private void RequestContextSafe(DesignEditorContextSource source, Point viewportPoint, KeyModifiers modifiers)
    {
        var previous = _contextRequest;
        var current = new CancellationTokenSource();
        _contextRequest = current;

        if (previous != null)
        {
            previous.Cancel();
            previous.Dispose();
        }

        _ = RequestContextAsync(source, viewportPoint, modifiers, current.Token).ContinueWith(
            task =>
            {
                if (ReferenceEquals(_contextRequest, current))
                    _contextRequest = null;

                current.Dispose();

                if (task.Exception is { } exception && !task.IsCanceled)
                {
                    Logger.TryGet(LogEventLevel.Error, LogArea.Control)?.Log(
                        this, "Провайдер контекстных действий завершился ошибкой: {Error}", exception.GetBaseException());
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    // Отмена запроса контекста, который ещё идёт. Null означает, что запроса нет.
    private CancellationTokenSource? _contextRequest;

    private void RetargetSelectionForContext(Point viewportPoint, KeyModifiers modifiers)
    {
        var worldPoint = GetWorldPosition(viewportPoint);
        if (!TryResolveContextTarget(worldPoint, out var hitTarget) || hitTarget == null)
            return;

        var target = hitTarget.Target;
        var container = hitTarget.Container;
        if (target == null)
            return;

        var isTargetInSelection = SelectedDesignTargets.Any(selected =>
            ReferenceEquals(selected.Target, target));

        if (!isTargetInSelection)
        {
            var index = IndexFromContainer(container);
            if (index >= 0)
            {
                Selection.Clear();
                Selection.Select(index);
            }
        }

        // Context invocation must not use additive toggle semantics (e.g. Shift+RMB).
        var normalizedModifiers = modifiers & ~InputGestures.AdditiveSelectionModifiers;
        UpdateSelectionTargetFromPoint(container, viewportPoint, normalizedModifiers);
    }

    // --- Drag & Drop ---

    private void OnItemsDragStarted(DragStartedEventArgs e)
    {
        _groupDragOperation = null;

        if (IsSelecting || CurrentState is EditorPanningState)
        {
            e.Handled = true;
            return;
        }

        var sourceContainer = e.Source as DesignEditorItem;
        var items = SelectedItems;
        if (sourceContainer == null || items == null || items.Count == 0)
        {
            e.Handled = true;
            return;
        }

        var selectionCapabilities = GetSelectionInteractionCapabilities();
        if (ShouldBlockNestedGroupDrag(selectionCapabilities))
        {
            e.Handled = true;
            return;
        }

        var sourceTarget = ResolveInteractionTarget(sourceContainer);
        var sourceMovePolicy = GetEffectiveMovePolicy(sourceTarget);
        if (sourceMovePolicy == ArxisStudio.Attached.MovePolicy.None)
        {
            e.Handled = true;
            return;
        }

        // Все проверки пройдены — жест состоится, открываем единицу редактирования.
        BeginEdit(DesignEditKind.Move);
        _groupDragOperation = GroupDragOperation.TryCreate(this, sourceContainer, sourceTarget);

        e.Handled = true;
    }

    private void OnItemsDragDelta(DragDeltaEventArgs e)
    {
        if (IsSelecting || CurrentState is EditorPanningState) return;

        var items = SelectedItems;
        if (items == null || items.Count == 0) return;
        var source = e.Source as DesignEditorItem;
        if (source != null)
        {
            if (ShouldBlockNestedGroupDrag(GetSelectionInteractionCapabilities()))
            {
                e.Handled = true;
                return;
            }

            var sourceTarget = ResolveInteractionTarget(source);
            if (GetEffectiveMovePolicy(sourceTarget) == ArxisStudio.Attached.MovePolicy.None)
            {
                e.Handled = true;
                return;
            }
        }

        if (_groupDragOperation != null &&
            e.Source is DesignEditorItem sourceContainer &&
            _groupDragOperation.CanHandle(sourceContainer))
        {
            UpdateInteractionOperation(_groupDragOperation, new Vector(e.HorizontalChange, e.VerticalChange));

            e.Handled = true;
            UpdateSelectionOverlayState();
            return;
        }

        var delta = new Vector(e.HorizontalChange, e.VerticalChange);

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null || !container.IsDraggable || ReferenceEquals(container, source))
                continue;

            var target = ResolveInteractionTarget(container);
            var position = GetDesignPosition(target);
            var filteredDelta = ApplyMovePolicy(target, delta);
            SetDesignPosition(target, position + filteredDelta);
        }
        e.Handled = true;
        UpdateSelectionOverlayState();
    }

    private void OnItemsDragCompleted(DragCompletedEventArgs e)
    {
        CompleteInteractionOperation(ref _groupDragOperation);
        CommitEdit();
        e.Handled = true;
    }

    private void OnItemsResizeDelta(ResizeDeltaEventArgs e)
    {
        UpdateSelectionOverlayState();
        e.Handled = false;
    }

    private void OnSelectionResizeStarted(object? sender, ResizeStartedEventArgs e)
    {
        if (_primarySelectionItem == null || _primarySelectionControl == null || !HasSingleSelection)
            return;

        if (!IsResizeAllowed(_primarySelectionControl, e.Direction))
            return;

        // До PushState: ItemResizingState.Enter уже фиксирует текущий размер.
        BeginEdit(DesignEditKind.Resize);
        _primarySelectionItem.PushState(new ItemResizingState(_primarySelectionItem, _primarySelectionControl, e.Direction));
        _primarySelectionItem.OnResizeStarted(e.Vector);
        e.Handled = true;
    }

    private void OnSelectionResizeDelta(object? sender, ResizeDeltaEventArgs e)
    {
        if (_primarySelectionItem == null || _primarySelectionControl == null || _primarySelectionItem.CurrentState is not ItemResizingState)
            return;
        if (!IsResizeAllowed(_primarySelectionControl, e.Direction))
            return;

        var worldDelta = NormalizeResizeDelta(e.Delta);
        var normalizedArgs = new ResizeDeltaEventArgs(worldDelta, e.Direction, SelectionAdorner.ResizeDeltaEvent)
        {
            Source = e.Source
        };

        _primarySelectionItem.CurrentState.OnResizeDelta(normalizedArgs);
        _primarySelectionItem.OnResizeDelta(new ResizeDeltaEventArgs(worldDelta, e.Direction, DesignEditorItem.ResizeDeltaEvent));
        UpdateSelectionOverlayState();
        e.Handled = true;
    }

    private void OnSelectionResizeCompleted(object? sender, VectorEventArgs e)
    {
        if (_primarySelectionItem == null || _primarySelectionControl == null || _primarySelectionItem.CurrentState is not ItemResizingState)
            return;

        _primarySelectionItem.PopState();
        _primarySelectionItem.OnResizeCompleted(e.Vector);
        UpdateSelectionOverlayState();
        CommitEdit();
        e.Handled = true;
    }

    private void OnSecondarySelectionResizeStarted(object? sender, SelectionAdornerResizeStartedEventArgs e)
    {
        var container = e.AdornerInfo.Container;
        var target = e.AdornerInfo.Target;

        if (container == null || target == null || !HasMultipleNestedSelection)
            return;
        if (!IsResizeAllowed(target, e.Direction))
            return;

        BeginEdit(DesignEditKind.Resize);
        container.PushState(new ItemResizingState(container, target, e.Direction));
        container.OnResizeStarted(e.Vector);
        e.Handled = true;
    }

    private void OnSecondarySelectionResizeDelta(object? sender, SelectionAdornerResizeDeltaEventArgs e)
    {
        var container = e.AdornerInfo.Container;
        var target = e.AdornerInfo.Target;

        if (container == null || target == null || container.CurrentState is not ItemResizingState)
            return;
        if (!IsResizeAllowed(target, e.Direction))
            return;

        var worldDelta = NormalizeResizeDelta(e.Delta);
        var normalizedArgs = new ResizeDeltaEventArgs(worldDelta, e.Direction, SelectionAdorner.ResizeDeltaEvent)
        {
            Source = e.Source
        };

        container.CurrentState.OnResizeDelta(normalizedArgs);
        container.OnResizeDelta(new ResizeDeltaEventArgs(worldDelta, e.Direction, DesignEditorItem.ResizeDeltaEvent));
        UpdateSelectionOverlayState();
        e.Handled = true;
    }

    private void OnSecondarySelectionResizeCompleted(object? sender, SelectionAdornerResizeCompletedEventArgs e)
    {
        var container = e.AdornerInfo.Container;
        var target = e.AdornerInfo.Target;

        if (container == null || target == null || container.CurrentState is not ItemResizingState)
            return;

        container.PopState();
        container.OnResizeCompleted(e.Vector);
        UpdateSelectionOverlayState();
        CommitEdit();
        e.Handled = true;
    }

    private void OnGroupSelectionResizeStarted(object? sender, ResizeStartedEventArgs e)
    {
        if (!HasMultipleContainerSelection)
            return;

        // TryCreateGroupResizeOperation фиксирует текущие размеры target'ов,
        // поэтому открывать единицу редактирования нужно до него — и отменять,
        // если операция так и не создалась.
        BeginEdit(DesignEditKind.Resize);
        if (!TryCreateGroupResizeOperation(e.Direction, out var operation))
        {
            CancelEdit();
            return;
        }

        _groupResizeOperation = operation;

        // У группового resize нет состояния контейнера, поэтому снимок соседей
        // берётся здесь. Исключается всё выделение целиком, а не один target.
        if (operation?.SourceTarget is { } guideSource)
            BeginSnapGuides(guideSource);

        e.Handled = true;
    }

    private void OnGroupSelectionResizeDelta(object? sender, ResizeDeltaEventArgs e)
    {
        if (_groupResizeOperation == null)
            return;

        UpdateInteractionOperation(_groupResizeOperation, NormalizeResizeDelta(e.Delta));

        UpdateSelectionOverlayState();
        e.Handled = true;
    }

    private Vector NormalizeResizeDelta(Vector delta)
    {
        var zoom = Math.Max(0.0001, ViewportZoom);
        return delta / zoom;
    }

    private void OnGroupSelectionResizeCompleted(object? sender, VectorEventArgs e)
    {
        if (_groupResizeOperation == null)
            return;

        CompleteInteractionOperation(ref _groupResizeOperation);
        EndSnapGuides();
        UpdateSelectionOverlayState();
        CommitEdit();
        e.Handled = true;
    }

    internal void UpdateSelectionTargetFromPoint(DesignEditorItem container, Point screenPoint, KeyModifiers modifiers)
    {
        // Оба слоя пишутся одной транзакцией. Раньше индексный слой писало состояние
        // контейнера, а этот метод дописывал слой target'ов уже после — и между двумя
        // записями оверлей успевал пересобраться на промежуточном состоянии.
        using (Selection.BatchUpdate())
        {
            if (!container.IsSelected)
            {
                if (!ShouldUseAdditiveSelection(modifiers))
                    Selection.Clear();

                var ownerIndex = IndexFromContainer(container);
                if (ownerIndex >= 0)
                    Selection.Select(ownerIndex);
            }

            ApplyTargetFromPoint(container, screenPoint, modifiers);
        }

        UpdateSelectionOverlayState();
    }

    /// <summary>
    /// Схлопывает выделение до одного контейнера.
    /// </summary>
    /// <remarks>
    /// Одна транзакция: двумя записями подряд наружу публиковалось промежуточное
    /// пустое выделение, и обычный клик по контейнеру внутри группы стоил трёх
    /// событий вместо одного. Запись индексного слоя принадлежит редактору —
    /// состояние контейнера только сообщает о жесте.
    /// </remarks>
    internal void CollapseSelectionTo(DesignEditorItem container)
    {
        var index = IndexFromContainer(container);
        if (index < 0)
            return;

        using (Selection.BatchUpdate())
        {
            Selection.Clear();
            Selection.Select(index);
        }
    }

    /// <summary>
    /// Пишет слой design target'ов по точке нажатия.
    /// </summary>
    /// <remarks>
    /// Оверлей отсюда не пересобирается: это половина транзакции, и пересборка
    /// на её середине публиковала бы состояние, которого пользователь не просил.
    /// </remarks>
    private void ApplyTargetFromPoint(DesignEditorItem container, Point screenPoint, KeyModifiers modifiers)
    {
        if (ShouldUseContainerInteraction(modifiers))
        {
            if (ShouldUseAdditiveSelection(modifiers))
            {
                // Группа контейнеров набирается тем же правилом, что и группа
                // вложенных target'ов. Раньше эта ветка всегда заменяла выбор,
                // и добавить второй контейнер кликом было нельзя вовсе.
                ToggleTargetInSelection(container);
                SyncContainerItemSelection(container);
            }
            else
            {
                SetSingleSelectedTarget(container);
            }

            return;
        }

        var worldPoint = GetWorldPosition(screenPoint);
        if (!TryResolveSelectionTargetAtPoint(container, worldPoint, out var target))
        {
            // Клик по области без designer-metadata внутри контейнера
            // переводит selection target на уровень контейнера.
            SetSingleSelectedTarget(container);
            return;
        }

        var isAdditive = ShouldUseAdditiveSelection(modifiers);
        // Грубая проверка по владельцу верхнего уровня плюс точная по design host:
        // target уже известен, поэтому уровень вложенности можно сверить честно.
        if (isAdditive && (!CanAddNestedTargetToContainer(container) || !SharesDesignHostWithSelection(target)))
            return;

        if (isAdditive)
        {
            ToggleTargetInSelection(target);
        }
        else
        {
            var index = _selectedTargets.IndexOf(target);
            if (_selectedTargets.Count > 1 && index >= 0)
            {
                // Обычный клик по уже выбранному target внутри группы
                // не должен схлопывать multi-selection. Переносим target в начало,
                // чтобы он стал primary selection target.
                if (index > 0)
                {
                    _selectedTargets.RemoveAt(index);
                    _selectedTargets.Insert(0, target);
                }
            }
            else
            {
                SetSingleSelectedTarget(target);
            }
        }
    }

    /// <summary>
    /// Возвращает <see cref="DesignEditorItem"/> верхнего уровня, которому принадлежит
    /// указанный контейнер, либо <see langword="null"/>, если он не в этом редакторе.
    /// </summary>
    /// <remarks>
    /// Контейнеры могут быть вложены друг в друга, но индексная модель выбора Avalonia
    /// знает только контейнеры собственного <c>ItemsSource</c>. Поэтому выбор всегда
    /// маршрутизируется на владеющий item верхнего уровня, а сам вложенный контейнер
    /// участвует как design target — это и даёт дерево любой глубины без отказа от
    /// <see cref="SelectingItemsControl"/>.
    /// </remarks>
    internal DesignEditorItem? ResolveOwningItem(DesignEditorItem container)
    {
        var current = container;
        while (current != null)
        {
            if (IndexFromContainer(current) >= 0)
                return current;

            current = current.FindAncestorOfType<DesignEditorItem>();
        }

        return null;
    }

    internal Control ResolveInteractionTarget(DesignEditorItem container)
    {
        if (_selectedTargets.Contains(container) || ShouldUseContainerInteraction(LastInputModifiers))
            return container;

        return ResolveSelectionTarget(container);
    }

    internal void SetLastInputModifiers(KeyModifiers modifiers)
    {
        LastInputModifiers = modifiers;
    }

    /// <summary>
    /// Пересчитывает контейнер, в пределах которого работает текущая рамка выделения.
    /// </summary>
    /// <param name="worldBounds">Прямоугольник рамки в мировых координатах.</param>
    /// <param name="useContainerSelection">Признак работы рамки на уровне контейнеров.</param>
    /// <remarks>
    /// Вызывается на каждом шаге протяжки, поэтому <see cref="MarqueeScope"/> отражает
    /// текущий прямоугольник, а не точку нажатия. Иначе рамка, начатая внутри контейнера,
    /// оставалась бы привязанной к нему при любой протяжке, и её визуальный охват
    /// обещал бы выборку, которой не происходит.
    /// </remarks>
    internal void UpdateMarqueeScope(Rect worldBounds, bool useContainerSelection)
    {
        MarqueeScope = useContainerSelection ? null : FindContainerForMarquee(worldBounds);
    }

    internal void ClearMarqueeScope() => MarqueeScope = null;

    internal Point GetDesignPosition(Control control)
        => GetPlacementStrategy(control).GetPosition(control, this);

    /// <summary>
    /// Задаёт позицию target'а в design-координатах.
    /// </summary>
    /// <remarks>
    /// Раскладка, которая владеет позицией ребёнка, отсекается здесь, а не выше:
    /// это единственная точка записи, поэтому только тут можно гарантировать,
    /// что в контракт изменений не попадёт перемещение, которого не произошло.
    /// </remarks>
    internal void SetDesignPosition(Control control, Point position)
    {
        var strategy = GetPlacementStrategy(control);
        if (strategy.MoveSemantics != DesignMoveSemantics.Reposition)
            return;

        if (!_suppressEditRecording)
            _activeEdit?.RecordPosition(this, control, position);

        strategy.SetPosition(control, position, this);
    }

    internal Size GetDesignSize(Control control)
    {
        var width = double.IsNaN(control.Width) ? control.Bounds.Width : control.Width;
        var height = double.IsNaN(control.Height) ? control.Bounds.Height : control.Height;
        return new Size(width, height);
    }

    internal void SetDesignSize(Control control, Size size)
    {
        var coerced = CoerceDesignSize(control, size);

        if (!_suppressEditRecording)
            _activeEdit?.RecordSize(this, control, coerced);

        control.Width = coerced.Width;
        control.Height = coerced.Height;
    }

    /// <summary>
    /// Приводит запрошенный размер к ограничениям самого контрола.
    /// </summary>
    /// <remarks>
    /// До появления этого метода редактор писал <c>Width</c>/<c>Height</c> мимо
    /// <c>MinWidth</c>/<c>MaxWidth</c>: раскладка применяла ограничение уже после,
    /// и запрошенный размер расходился с фактическим — редактор считал от одного,
    /// а пользователь видел другое.
    /// <para>
    /// При <c>Max &lt; Min</c> побеждает минимум — так же, как в самой Avalonia.
    /// Минимальный размер редактора (<see cref="DesignEditorInteractionOptions.ResizeMinSize"/>)
    /// участвует наравне с <c>MinWidth</c>, поэтому правило остаётся одно.
    /// </para>
    /// </remarks>
    internal Size CoerceDesignSize(Control control, Size size)
    {
        var floor = Math.Max(0.0, InteractionOptions.ResizeMinSize);

        return new Size(
            ClampSize(size.Width, Math.Max(floor, control.MinWidth), control.MaxWidth),
            ClampSize(size.Height, Math.Max(floor, control.MinHeight), control.MaxHeight));
    }

    private static double ClampSize(double value, double min, double max)
        => Math.Max(Math.Min(value, max), min);

    /// <summary>
    /// Возвращает прямоугольник, за который target не должен выходить при изменении размера.
    /// </summary>
    /// <remarks>
    /// Границей выбран владеющий <see cref="DesignEditorItem"/>, а не прямой родитель.
    /// Панель, которая растёт по содержимому, границей быть не может: ограничивать
    /// ребёнка её же высотой — рассуждение по кругу. Форма же всегда имеет размер,
    /// и правило формулируется одной фразой: контрол не выходит за свою форму.
    /// <para>
    /// У контейнера верхнего уровня владельца нет, поэтому его размер не ограничен —
    /// он лежит на бесконечном холсте.
    /// </para>
    /// </remarks>
    internal bool TryGetContainmentBounds(Control target, out Rect bounds)
    {
        bounds = default;

        if (!InteractionOptions.IsResizeContainedToParent)
            return false;

        var host = FindDesignHost(target);

        // Приведение к Control обязательно: перегрузка для DesignEditorItem
        // возвращает границы выбранного внутри него target'а, а не самой формы.
        return host != null && TryGetDesignBounds((Control)host, out bounds);
    }

    /// <summary>
    /// Определяет, есть ли кому выполнить перестановку.
    /// </summary>
    /// <remarks>
    /// Жест не предлагается, когда исполнить его некому: иначе редактор вёл бы
    /// точку вставки за курсором и обещал результат, которого не будет. Правило
    /// то же, что и у политик размещения, — заблокированный жест не начинается.
    /// </remarks>
    internal bool CanRequestReorder => ReorderRequested != null;

    /// <summary>
    /// Просит приложение переставить контрол перед указанным соседом.
    /// </summary>
    /// <param name="control">Контрол, который требуется переставить.</param>
    /// <param name="insertBefore">
    /// Позиция вставки в <b>текущем</b> списке детей; значение, равное их числу,
    /// означает «в конец». Выход за эти границы означает, что дерево изменилось
    /// внутри жеста, и запрос отклоняется.
    /// </param>
    /// <returns><see langword="true"/>, если перестановку выполнил обработчик.</returns>
    /// <remarks>
    /// Редактор сам дерево не правит: он распознаёт жест и сообщает намерение.
    /// Структурная правка, её запись и отмена — зона библиотеки разметки.
    /// <para>
    /// Обе половины запроса считаются от <b>одного</b> чтения коллекции. Раньше
    /// состояние жеста переводило точку вставки в индекс переноса по своему снимку,
    /// снятому на входе в жест, а текущую позицию редактор перечитывал заново, —
    /// и стоило дереву измениться во время протяжки, как хост получал пару индексов,
    /// описывающих разные состояния панели.
    /// </para>
    /// </remarks>
    internal bool RequestChildIndex(Control control, int insertBefore)
    {
        if (control.GetVisualParent() is not Panel panel)
            return false;

        var current = panel.Children.IndexOf(control);
        if (current < 0)
            return false;

        var handler = ReorderRequested;
        if (handler == null)
            return false;

        // Точка вставки описывает ту же коллекцию, которую редактор только что
        // прочитал: от нуля до числа детей включительно, где верхняя граница
        // означает «в конец». Всё, что вне, — снимок панели, которой уже нет:
        // дерево изменилось внутри жеста. Такой запрос отклоняется, а не
        // подгоняется под новый размер. Подгонка ставила бы контрол туда, куда
        // пользователь не целился, и хост не смог бы это заметить: индексы
        // приходили бы к нему валидными на вид.
        if (insertBefore < 0 || insertBefore > panel.Children.Count)
            return false;

        // Перенос удаляет контрол и только потом вставляет, поэтому позиции
        // правее источника сдвигаются на одну. Из проверки выше следует, что
        // результат уже лежит в границах коллекции, и ограничивать его нечем.
        var requested = insertBefore > current ? insertBefore - 1 : insertBefore;
        if (requested == current)
            return false;

        var anchor = insertBefore < panel.Children.Count
            ? panel.Children[insertBefore]
            : null;

        var args = new DesignEditorReorderRequestedEventArgs(
            control,
            current,
            requested,
            ReferenceEquals(anchor, control) ? null : anchor);

        // Обработчики обходятся по одному, и первый же выполнивший правку
        // останавливает обход. Индексы сняты до правки, поэтому следующему
        // они описывали бы дерево, которого уже нет: на двух подписчиках
        // одинаковой формы перестановка молча отменяла сама себя.
        foreach (var invocation in handler.GetInvocationList())
        {
            ((EventHandler<DesignEditorReorderRequestedEventArgs>)invocation)(this, args);

            if (args.Handled)
                break;
        }

        if (!args.Handled)
            return false;

        // Обработчик мог не переставить контрол, а пересобрать разметку. Выделение,
        // оставшееся на контроле вне дерева, рисовало бы рамку по его последним
        // координатам и принимало бы на него нюдж. Пересборка оверлея этим и
        // занимается: сверка выделения отбрасывает target, у которого больше нет
        // владеющего item'а, — отдельная зачистка рядом с ней ничего не добавляла
        // и ни одним тестом не держалась.
        UpdateSelectionOverlayState();
        return true;
    }

    internal void SetDesignZIndex(Control control, int zIndex)
    {
        if (!_suppressEditRecording)
            _activeEdit?.RecordZIndex(this, control, zIndex);

        control.ZIndex = zIndex;
    }

    /// <summary>
    /// Выбирает контрол как design target — то же, что клик по нему на поверхности.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Существует ради хоста, у которого есть собственное дерево. Выделение с поверхности он
    /// получал и раньше — через <see cref="DesignSelectionChanged"/>, — а вот обратной дороги не
    /// было вовсе: клик по строке в дереве не мог выбрать контрол на канве. Приложению оставалось
    /// либо лезть во внутренности редактора, либо не иметь дерева.
    /// </para>
    /// <para>
    /// Принимает и сам контейнер: выбрать форму целиком — такое же состояние, как выбрать контрол
    /// внутри неё, и указатель его достаёт. Владелец разрешается тем же путём, что и на пути
    /// указателя, поэтому контрол во вложенном контейнере выбирается, а не отвергается: ближайший
    /// design host у него вложенный, а индекс есть только у item'а верхнего уровня.
    /// </para>
    /// <para>
    /// Оба слоя выбора меняются здесь вместе, и это не осторожность, а необходимость: контейнер,
    /// попавший в <c>Selection</c> без собственной записи в слое target'ов, подменяется вложенным
    /// target'ом по умолчанию, и группа контейнеров молча становится смешанной.
    /// </para>
    /// <para>
    /// Снять выделение этим методом нельзя, и отдельного метода для этого нет, потому что он не
    /// нужен: <c>SelectedItems.Clear()</c> снимает и индексный слой, и слой target'ов — обработчик
    /// на <c>IsSelected</c> перестраивает оверлей, а тот на пустом выборе вычищает target'ы. Сказано
    /// здесь, потому что искать это в другом свойстве никто не догадается.
    /// </para>
    /// <para>
    /// Набор выделяется по одному вызову на контрол, и это стоит одного
    /// <see cref="DesignSelectionChanged"/> на каждый: три строки в дереве — три события и три
    /// перестроения оверлея. Пакетной формы («выделение теперь вот это») пока нет намеренно —
    /// заводить её стоит под потребителя с мультивыбором, а не заранее.
    /// </para>
    /// </remarks>
    /// <param name="target">Контрол или контейнер, который нужно выбрать.</param>
    /// <param name="additive">Добавить к текущему выделению, а не заменить его.</param>
    /// <returns><see langword="true"/>, если контрол редактируем и после вызова выбран.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="target"/> равен <see langword="null"/>.</exception>
    public bool SelectDesignTarget(Control target, bool additive = false)
    {
        ArgumentNullException.ThrowIfNull(target);

        return ApplySelection(target, additive ? SelectionIntent.Add : SelectionIntent.Replace);
    }

    /// <summary>
    /// Намерение записи выделения.
    /// </summary>
    private enum SelectionIntent
    {
        /// <summary>Заменить выделение целиком.</summary>
        Replace,

        /// <summary>Добавить к текущему выделению.</summary>
        Add
    }

    /// <summary>
    /// Единственная точка записи выделения: пишет оба слоя за одну транзакцию.
    /// </summary>
    /// <remarks>
    /// Оба слоя обязаны меняться вместе, и до появления этой точки правило держалось
    /// в каждой точке записи своими руками — с разными резолверами владельца, разным
    /// набором guard'ов и батчингом в двух местах из десяти. Отсюда и брались состояния,
    /// которые указатель построить не может, а публичный API строил.
    /// <para>
    /// Четыре вещи, которых не делала ни одна прежняя точка записи:
    /// </para>
    /// <list type="number">
    /// <item><description>
    /// <b>Один резолвер владельца.</b> Владелец — item верхнего уровня, у него есть индекс;
    /// host — ближайший контейнер, он решает редактируемость. Оба ответа нужны, вход один.
    /// </description></item>
    /// <item><description>
    /// <b>Ворота редактируемости, которые нельзя обойти выбором аргумента.</b> Проверка
    /// <c>IsSelectableTarget(target, FindDesignHost(target))</c> в режиме <c>Loaded</c> была
    /// тавтологией — её ветка это <c>ReferenceEquals(FindDesignHost(control), owner)</c>,
    /// а owner вызывающий вычислял тем же <c>FindDesignHost</c>. Теперь target обязан
    /// оказаться среди кандидатов своего host'а: ровно тем списком пользуется указатель,
    /// и именно он не спускается внутрь шаблонов.
    /// </description></item>
    /// <item><description>
    /// <b>Оба слоя внутри одного <c>BatchUpdate</c>.</b> Без него замена публиковала
    /// два <see cref="DesignSelectionChanged"/>, первый — с пустым выделением: <c>Clear()</c>
    /// синхронно доходит до обработчика <c>IsSelected</c>, тот пересобирает оверлей и
    /// публикует пустой снимок. Хост, зеркалящий выделение в своё дерево, гасил подсветку
    /// между двумя событиями одного вызова.
    /// </description></item>
    /// <item><description>
    /// <b>Материализация неявного target'а на записи.</b> Контейнер, попавший в индексный
    /// слой без записи в слое target'ов, читался как «выбран его ребёнок по умолчанию» —
    /// и потому <c>additive</c> поверх него молча терял то, к чему добавлял.
    /// </description></item>
    /// </list>
    /// </remarks>
    private bool ApplySelection(Control target, SelectionIntent intent)
    {
        // Жест владеет выделением, пока идёт. Вызов извне посреди него ставит
        // контрол в группу, которую перетаскивают, — он уезжает вместе с ней,
        // а лишняя правка попадает в чужую единицу редактирования.
        if (IsSelecting || CurrentState is not EditorIdleState)
            return false;

        var host = target as DesignEditorItem is { } item && !ReferenceEquals(item, target)
            ? null
            : FindDesignHost(target);

        var owner = ResolveOwningItemForTarget(target);
        if (owner == null)
            return false;

        var index = IndexFromContainer(owner);
        if (index < 0)
            return false;

        if (host != null && !IsEditableTarget(target, host))
            return false;

        if (intent == SelectionIntent.Add && !SharesDesignHostWithSelection(target))
            return false;

        using (Selection.BatchUpdate())
        {
            if (intent == SelectionIntent.Replace)
            {
                Selection.Clear();
                SetSingleSelectedTarget(target);
            }
            else
            {
                MaterialiseImplicitTargets();
                AddSelectedTarget(target);
            }

            Selection.Select(index);
        }

        UpdateSelectionOverlayState();

        // Тот же жест, что и у указателя: без фокуса клавиатура до редактора
        // не доходит, и выделение, заданное хостом, нельзя сдвинуть стрелками.
        if (!IsKeyboardFocusWithin)
            Focus();

        return SelectedDesignTargets.Any(selected => ReferenceEquals(selected.Target, target));
    }

    /// <summary>
    /// Проверяет, что target вообще редактируем в своём host'е.
    /// </summary>
    /// <remarks>
    /// Мало спросить <see cref="IsSelectableTarget"/>: в режиме <c>Loaded</c> он отсекает
    /// только чужой контейнер, а внутренности шаблонов отсекает сам обход авторской
    /// разметки. Указателю этого хватает, потому что он и берёт кандидатов из обхода;
    /// публичному входу target приносят снаружи, поэтому обход нужно спросить явно.
    /// </remarks>
    private static bool IsEditableTarget(Control target, DesignEditorItem host)
    {
        if (!IsSelectableTarget(target, host))
            return false;

        foreach (var candidate in EnumerateSelectionCandidates(host))
        {
            if (ReferenceEquals(candidate, target))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Записывает неявные target'ы контейнеров, выбранных только в индексном слое.
    /// </summary>
    /// <remarks>
    /// Такой контейнер читается через <see cref="ResolveSelectionTargets"/> как его
    /// ребёнок по умолчанию, но в <c>_selectedTargets</c> его нет — и добавление
    /// к выделению теряло его молча. Материализация делает чтение чистым.
    /// </remarks>
    private void MaterialiseImplicitTargets()
    {
        var items = SelectedItems;
        if (items == null)
            return;

        foreach (var item in items)
        {
            if (ContainerFromItem(item) is not DesignEditorItem container)
                continue;

            var owned = false;
            foreach (var selected in _selectedTargets)
            {
                if (!IsOwnedByContainer(selected, container))
                    continue;

                owned = true;
                break;
            }

            if (!owned)
                AddSelectedTarget(ResolveDefaultSelectionTarget(container));
        }
    }

    /// <summary>
    /// Перемещает выделение на передний план.
    /// </summary>
    /// <returns><see langword="true"/>, если порядок был изменён.</returns>
    public bool BringToFront() => TryReorder(DesignOrderPlacement.Front);

    /// <summary>
    /// Перемещает выделение на задний план.
    /// </summary>
    /// <returns><see langword="true"/>, если порядок был изменён.</returns>
    public bool SendToBack() => TryReorder(DesignOrderPlacement.Back);

    /// <summary>
    /// Поднимает выделение на одну позицию.
    /// </summary>
    /// <returns><see langword="true"/>, если порядок был изменён.</returns>
    public bool BringForward() => TryReorder(DesignOrderPlacement.Forward);

    /// <summary>
    /// Опускает выделение на одну позицию.
    /// </summary>
    /// <returns><see langword="true"/>, если порядок был изменён.</returns>
    public bool SendBackward() => TryReorder(DesignOrderPlacement.Backward);

    private enum DesignOrderPlacement
    {
        Front,
        Back,
        Forward,
        Backward
    }

    /// <summary>
    /// Меняет порядок перекрытия выбранных targets.
    /// </summary>
    /// <remarks>
    /// Порядок осмыслен только среди соседей по родительской панели, поэтому targets
    /// группируются по родителю и переставляются независимо в каждой группе.
    /// <para>
    /// Внутри группы <c>ZIndex</c> нормализуется в последовательность 0..n-1. Иначе
    /// перестановка на одну позицию не работала бы: по умолчанию у всех соседей
    /// <c>ZIndex</c> равен нулю, и менять местами было бы нечего. Первая операция
    /// поэтому затрагивает всю группу, последующие — только сдвинутые элементы,
    /// потому что фильтр no-op отбрасывает совпавшие.
    /// </para>
    /// </remarks>
    private bool TryReorder(DesignOrderPlacement placement)
    {
        var targets = SelectedDesignTargets;
        if (targets.Count == 0)
            return false;

        var groups = new Dictionary<Visual, List<Control>>();
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i].Target;
            if (target.GetVisualParent() is not { } parent)
                continue;

            if (!groups.TryGetValue(parent, out var members))
                groups[parent] = members = new List<Control>();

            members.Add(target);
        }

        if (groups.Count == 0)
            return false;

        BeginEdit(DesignEditKind.Order);

        foreach (var pair in groups)
            ReorderWithinParent(pair.Key, pair.Value, placement);

        CommitEdit();
        return true;
    }

    private void ReorderWithinParent(Visual parent, List<Control> moving, DesignOrderPlacement placement)
    {
        var siblings = new List<Control>();
        foreach (var child in parent.GetVisualChildren())
        {
            if (child is Control control)
                siblings.Add(control);
        }

        if (siblings.Count <= 1)
            return;

        // Текущий видимый порядок: сначала ZIndex, при равенстве — порядок в панели.
        var order = new List<Control>(siblings);
        order.Sort((a, b) =>
        {
            var byZ = a.ZIndex.CompareTo(b.ZIndex);
            return byZ != 0 ? byZ : siblings.IndexOf(a).CompareTo(siblings.IndexOf(b));
        });

        var isMoving = new HashSet<Control>(moving);
        var arranged = placement switch
        {
            DesignOrderPlacement.Front => Partition(order, isMoving, movedLast: true),
            DesignOrderPlacement.Back => Partition(order, isMoving, movedLast: false),
            DesignOrderPlacement.Forward => Shift(order, isMoving, forward: true),
            _ => Shift(order, isMoving, forward: false)
        };

        for (var i = 0; i < arranged.Count; i++)
            SetDesignZIndex(arranged[i], i);
    }

    private static List<Control> Partition(List<Control> order, HashSet<Control> moving, bool movedLast)
    {
        var stationary = new List<Control>();
        var moved = new List<Control>();

        foreach (var control in order)
            (moving.Contains(control) ? moved : stationary).Add(control);

        var result = new List<Control>(order.Count);
        if (movedLast)
        {
            result.AddRange(stationary);
            result.AddRange(moved);
        }
        else
        {
            result.AddRange(moved);
            result.AddRange(stationary);
        }

        return result;
    }

    private static List<Control> Shift(List<Control> order, HashSet<Control> moving, bool forward)
    {
        var result = new List<Control>(order);

        if (forward)
        {
            // С конца, иначе сдвинутый элемент обгонял бы соседа дважды.
            for (var i = result.Count - 2; i >= 0; i--)
            {
                if (moving.Contains(result[i]) && !moving.Contains(result[i + 1]))
                    (result[i], result[i + 1]) = (result[i + 1], result[i]);
            }
        }
        else
        {
            for (var i = 1; i < result.Count; i++)
            {
                if (moving.Contains(result[i]) && !moving.Contains(result[i - 1]))
                    (result[i], result[i - 1]) = (result[i - 1], result[i]);
            }
        }

        return result;
    }

    /// <summary>
    /// Отменяет изменение, возвращая target в состояние до него.
    /// </summary>
    /// <param name="change">Изменение из <see cref="DesignEditCompletedEventArgs.Changes"/>.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="change"/> равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Разбирать конкретный тип изменения приложению не нужно: стек отмены пишется
    /// одинаково для геометрии и для порядка перекрытия.
    /// </remarks>
    public void Revert(DesignChange change) => Apply(change, revert: true);

    /// <summary>
    /// Повторяет ранее отменённое изменение.
    /// </summary>
    /// <param name="change">Изменение из <see cref="DesignEditCompletedEventArgs.Changes"/>.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="change"/> равен <see langword="null"/>.</exception>
    public void Reapply(DesignChange change) => Apply(change, revert: false);

    private void Apply(DesignChange change, bool revert)
    {
        if (change == null)
            throw new ArgumentNullException(nameof(change));

        switch (change)
        {
            case DesignGeometryChange geometry:
                ApplyGeometry(geometry.Target, revert ? geometry.OldBounds : geometry.NewBounds);
                break;

            case DesignOrderChange order:
                ApplyOrder(order.Target, revert ? order.OldZIndex : order.NewZIndex);
                break;

        }
    }

    /// <summary>
    /// Задаёт порядок перекрытия, не создавая новой единицы редактирования.
    /// </summary>
    /// <param name="target">Контрол, порядок которого нужно задать.</param>
    /// <param name="zIndex">Новое значение порядка.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="target"/> равен <see langword="null"/>.</exception>
    public void ApplyOrder(Control target, int zIndex)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        var previous = _suppressEditRecording;
        _suppressEditRecording = true;
        try
        {
            SetDesignZIndex(target, zIndex);
        }
        finally
        {
            _suppressEditRecording = previous;
        }
    }

    /// <summary>
    /// Применяет геометрию к target, не создавая новой единицы редактирования.
    /// </summary>
    /// <param name="target">Контрол, геометрию которого нужно задать.</param>
    /// <param name="bounds">Целевая геометрия в design-координатах.</param>
    /// <exception cref="ArgumentNullException">Выбрасывается, если <paramref name="target"/> равен <see langword="null"/>.</exception>
    /// <remarks>
    /// Предназначен для отмены и повтора: принимает <see cref="DesignGeometryChange.OldBounds"/>
    /// или <see cref="DesignGeometryChange.NewBounds"/> напрямую. Запись изменений на время
    /// вызова подавляется, поэтому отмена не порождает новую запись в стеке.
    /// </remarks>
    /// <example>
    /// <code language="csharp"><![CDATA[
    /// foreach (var change in edit.Changes)
    ///     editor.ApplyGeometry(change.Target, change.OldBounds);
    /// ]]></code>
    /// </example>
    public void ApplyGeometry(Control target, Rect bounds)
    {
        if (target == null)
            throw new ArgumentNullException(nameof(target));

        var previous = _suppressEditRecording;
        _suppressEditRecording = true;
        try
        {
            SetDesignSize(target, bounds.Size);
            SetDesignPosition(target, bounds.Position);
        }
        finally
        {
            _suppressEditRecording = previous;
        }

        UpdateSelectionOverlayState();
    }

    internal ArxisStudio.Attached.ResizePolicy GetResizePolicy(Control control)
    {
        return DesignInteraction.GetResizePolicy(control);
    }

    internal ArxisStudio.Attached.MovePolicy GetMovePolicy(Control control)
    {
        return DesignInteraction.GetMovePolicy(control);
    }

    /// <summary>
    /// Возвращает стратегию размещения контрола.
    /// </summary>
    internal static IDesignPlacementStrategy GetPlacementStrategy(Control control)
        => DesignPlacementResolver.Resolve(control);

    /// <summary>
    /// Возвращает политику перемещения с учётом того, что реально умеет родительская раскладка.
    /// </summary>
    /// <remarks>
    /// Правило одно: <c>effective = user &amp; layout</c>. Раскладка задаёт потолок —
    /// что физически работает; политика пользователя только сужает. Ни одна не расширяет
    /// другую, иначе редактор снова начал бы предлагать жест, который ничего не делает.
    /// </remarks>
    internal ArxisStudio.Attached.MovePolicy GetEffectiveMovePolicy(Control control)
    {
        var user = GetMovePolicy(control);
        if (user == ArxisStudio.Attached.MovePolicy.None)
            return ArxisStudio.Attached.MovePolicy.None;

        return GetPlacementStrategy(control).MoveSemantics == DesignMoveSemantics.Reposition
            ? user
            : ArxisStudio.Attached.MovePolicy.None;
    }

    internal Vector ApplyMovePolicy(Control control, Vector delta)
    {
        return ApplyMovePolicy(delta, GetEffectiveMovePolicy(control));
    }

    internal bool IsResizeAllowed(Control control, ResizeDirection direction)
    {
        return IsResizeAllowed(GetResizePolicy(control), direction);
    }

    private SelectionInteractionCapabilities GetSelectionInteractionCapabilities()
    {
        var selectedTargets = SelectedDesignTargets;
        var selectedTargetCount = selectedTargets.Count;
        var isNestedGroupSelection = HasMultipleNestedSelection && selectedTargetCount > 1;
        var hasAnyMoveLockedTarget = false;
        var hasAnyMoveEnabledTarget = false;

        for (var i = 0; i < selectedTargetCount; i++)
        {
            var movePolicy = GetEffectiveMovePolicy(selectedTargets[i].Target);
            if (movePolicy == ArxisStudio.Attached.MovePolicy.None)
                hasAnyMoveLockedTarget = true;
            else
                hasAnyMoveEnabledTarget = true;

            if (hasAnyMoveLockedTarget && hasAnyMoveEnabledTarget)
                break;
        }

        return new SelectionInteractionCapabilities(
            selectedTargetCount,
            isNestedGroupSelection,
            hasAnyMoveLockedTarget,
            hasAnyMoveEnabledTarget);
    }

    internal bool ShouldBlockNestedGroupDrag(SelectionInteractionCapabilities capabilities)
    {
        if (!capabilities.IsNestedGroupSelection)
            return false;

        return capabilities.HasAnyMoveLockedTarget;
    }

    internal bool ShouldBlockNestedGroupDrag()
    {
        return ShouldBlockNestedGroupDrag(GetSelectionInteractionCapabilities());
    }

    private static Vector ApplyMovePolicy(Vector delta, ArxisStudio.Attached.MovePolicy policy)
    {
        var x = policy.HasFlag(ArxisStudio.Attached.MovePolicy.X) ? delta.X : 0d;
        var y = policy.HasFlag(ArxisStudio.Attached.MovePolicy.Y) ? delta.Y : 0d;
        return new Vector(x, y);
    }

    private static bool IsResizeAllowed(ArxisStudio.Attached.ResizePolicy policy, ResizeDirection direction)
    {
        return direction switch
        {
            ResizeDirection.Left => policy.HasFlag(ArxisStudio.Attached.ResizePolicy.Left),
            ResizeDirection.Top => policy.HasFlag(ArxisStudio.Attached.ResizePolicy.Top),
            ResizeDirection.Right => policy.HasFlag(ArxisStudio.Attached.ResizePolicy.Right),
            ResizeDirection.Bottom => policy.HasFlag(ArxisStudio.Attached.ResizePolicy.Bottom),
            ResizeDirection.TopLeft => policy.HasFlag(ArxisStudio.Attached.ResizePolicy.Top) &&
                                       policy.HasFlag(ArxisStudio.Attached.ResizePolicy.Left),
            ResizeDirection.TopRight => policy.HasFlag(ArxisStudio.Attached.ResizePolicy.Top) &&
                                        policy.HasFlag(ArxisStudio.Attached.ResizePolicy.Right),
            ResizeDirection.BottomLeft => policy.HasFlag(ArxisStudio.Attached.ResizePolicy.Bottom) &&
                                          policy.HasFlag(ArxisStudio.Attached.ResizePolicy.Left),
            ResizeDirection.BottomRight => policy.HasFlag(ArxisStudio.Attached.ResizePolicy.Bottom) &&
                                           policy.HasFlag(ArxisStudio.Attached.ResizePolicy.Right),
            _ => false
        };
    }

    private bool TryGetDesignBounds(DesignEditorItem item, out Rect bounds)
    {
        if (item == null)
        {
            bounds = default;
            return false;
        }

        return TryGetDesignBounds(ResolveSelectionTarget(item), out bounds);
    }

    internal bool TryGetDesignBounds(Control control, out Rect bounds)
    {
        if (!ReferenceEquals(control.FindAncestorOfType<DesignEditor>(), this))
        {
            bounds = default;
            return false;
        }

        if (control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
        {
            bounds = default;
            return false;
        }

        EnsureTracked(control);

        Visual? reference = control.FindAncestorOfType<DesignSurface>()
                            ?? control.FindAncestorOfType<DesignEditor>() as Visual;

        var position = reference != null
            ? control.TranslatePoint(new Point(0, 0), reference)
            : null;

        double x;
        double y;

        if (position.HasValue)
        {
            x = position.Value.X;
            y = position.Value.Y;
        }
        else
        {
            x = DesignLayout.GetDesignX(control);
            y = DesignLayout.GetDesignY(control);

            if (double.IsNaN(x) || double.IsNaN(y))
            {
                bounds = default;
                return false;
            }
        }

        bounds = new Rect(new Point(x, y), control.Bounds.Size);
        return true;
    }

    private static Control ResolveSelectionTarget(DesignEditorItem item)
    {
        if (item.FindAncestorOfType<DesignEditor>() is { } editor)
        {
            // Первый по приоритету target, принадлежащий этому item'у.
            // Сам item в списке означает, что он выбран целиком.
            foreach (var selected in editor._selectedTargets)
            {
                if (IsOwnedByContainer(selected, item))
                    return selected;
            }
        }

        return ResolveDefaultSelectionTarget(item);
    }

    private static Control ResolveDefaultSelectionTarget(DesignEditorItem item)
    {
        foreach (var control in EnumerateSelectionCandidates(item))
        {
            if (IsSelectableTarget(control, item))
                return control;
        }

        return item;
    }

    private static Control ResolveSelectionTarget(DesignEditorItem item, Visual? source)
    {
        var current = source;
        while (current != null && !ReferenceEquals(current, item))
        {
            if (current is Control control &&
                IsOwnedByContainer(control, item) &&
                HasDesignerLayoutMetadata(control))
            {
                return control;
            }

            current = current.GetVisualParent();
        }

        return ResolveSelectionTarget(item);
    }

    private bool TryResolveSelectionTargetAtPoint(DesignEditorItem item, Point worldPoint, out Control target)
    {
        Control? bestMatch = null;
        Rect bestBounds = default;
        var bestDepth = -1;

        foreach (var control in EnumerateSelectionCandidates(item))
        {
            if (!IsSelectableTarget(control, item))
                continue;

            if (!TryGetDesignBounds(control, out var bounds) || !bounds.Contains(worldPoint))
                continue;

            var depth = GetVisualDepth(control, item);
            if (bestMatch == null ||
                depth > bestDepth ||
                (depth == bestDepth && bounds.Width * bounds.Height < bestBounds.Width * bestBounds.Height))
            {
                bestMatch = control;
                bestBounds = bounds;
                bestDepth = depth;
            }
        }

        if (bestMatch == null)
        {
            target = null!;
            return false;
        }

        target = bestMatch;
        return true;
    }

    private static Control ResolveSelectionTarget(Control root)
    {
        if (HasDesignerLayoutMetadata(root))
            return root;

        foreach (var descendant in root.GetVisualDescendants())
        {
            if (descendant is Control control && HasDesignerLayoutMetadata(control))
                return control;
        }

        return root;
    }

    /// <summary>
    /// Определяет, может ли контрол быть design target внутри указанного контейнера.
    /// </summary>
    /// <remarks>
    /// В режиме <see cref="DesignContentMode.Loaded"/> размечать содержимое некому,
    /// поэтому редактируется всё, что автор написал в разметке. Внутренности
    /// контролов при этом отсекает <c>TemplatedParent</c>: у частей шаблона он задан,
    /// у элементов из <c>.axaml</c> — нет. Иначе клик по кнопке выбирал бы её
    /// внутренний <c>TextBlock</c>.
    /// </remarks>
    private static bool IsSelectableTarget(Control control, DesignEditorItem owner)
    {
        if (owner.ContentMode != DesignContentMode.Loaded)
            return HasDesignerLayoutMetadata(control);

        // Внутренности контролов отсекает уже сам обход авторской разметки,
        // здесь остаётся только не залезть в чужой контейнер.
        return ReferenceEquals(FindDesignHost(control), owner);
    }

    private static bool HasDesignerLayoutMetadata(Control control)
    {
        return DesignLayout.GetIsTracked(control)
            || !double.IsNaN(DesignLayout.GetX(control))
            || !double.IsNaN(DesignLayout.GetY(control));
    }

    internal static void EnsureTracked(Control control)
    {
        // Track идемпотентен. Прежний guard по GetIsTracked не работал:
        // Track не выставляет публичное IsTracked, поэтому для контролов
        // с одними Layout.X/Y условие было истинно всегда.
        DesignLayout.Track(control);
    }

    private static int GetVisualDepth(Control control, Visual root)
    {
        var depth = 0;
        var current = control as Visual;
        while (current != null && !ReferenceEquals(current, root))
        {
            depth++;
            current = current.GetVisualParent();
        }

        return depth;
    }

    private void CleanupSelectionTargets()
    {
        if (_selectedTargets.Count == 0)
            return;

        var selectedContainers = new HashSet<DesignEditorItem>();
        var items = SelectedItems;
        if (items != null)
        {
            foreach (var item in items)
            {
                var container = ContainerFromItem(item) as DesignEditorItem;
                if (container == null && item is DesignEditorItem directItem)
                    container = directItem;

                if (container != null)
                    selectedContainers.Add(container);
            }
        }

        // Target выживает, пока его владелец верхнего уровня остаётся выбранным.
        // Владелец вычисляется по дереву, поэтому правило одинаково работает
        // для любой глубины вложенности.
        _selectedTargets.RemoveAll(target =>
        {
            var owner = ResolveOwningItemForTarget(target);
            return owner == null || !selectedContainers.Contains(owner);
        });
    }

    internal IReadOnlyList<Control> ResolveSelectionTargets(DesignEditorItem item)
    {
        // Targets этого item'а в порядке приоритета. Сам item в списке означает,
        // что он выбран целиком; вложенные контейнеры попадают сюда наравне
        // с обычными контролами.
        List<Control>? owned = null;
        foreach (var target in _selectedTargets)
        {
            if (!IsOwnedByContainer(target, item))
                continue;

            (owned ??= new List<Control>()).Add(target);
        }

        // Item выбран, а вложенного target'а никто не называл — значит выбрана форма
        // целиком. Прежний ответ «его первый ребёнок» был неявным target'ом: из-за него
        // хост, выбравший форму через SelectedIndex, читал Scope как NestedTarget,
        // а группа контейнеров молча становилась смешанной.
        return owned ?? (IReadOnlyList<Control>)new Control[] { item };
    }

    /// <summary>
    /// Публикует новый снимок выделения, если он действительно отличается от текущего.
    /// </summary>
    /// <remarks>
    /// <see cref="UpdateSelectionOverlayState"/> вызывается из двух десятков мест,
    /// в том числе на каждом кадре перетаскивания и на каждое изменение геометрии.
    /// Снимок при этом пересобирается всегда, но выделение меняется редко, поэтому
    /// без этой проверки и свойства, и событие срабатывали бы на изменение геометрии.
    /// </remarks>
    private void ApplySelectionSnapshot(IReadOnlyList<DesignSelectionTarget> next)
    {
        var previous = _selectedDesignTargets;
        if (AreSameTargets(previous, next))
            return;

        var previousPrimary = _primarySelectionTarget;

        SelectedDesignTargets = next;
        PrimarySelectionTarget = next.Count > 0 ? next[0] : null;

        var handler = DesignSelectionChanged;
        if (handler == null)
            return;

        handler(this, new DesignSelectionChangedEventArgs(
            previous,
            next,
            Difference(next, previous),
            Difference(previous, next),
            previousPrimary,
            PrimarySelectionTarget));
    }

    /// <summary>
    /// Сравнивает наборы по контролам, а не по обёрткам <see cref="DesignSelectionTarget"/>:
    /// обёртки пересоздаются на каждой пересборке.
    /// </summary>
    private static bool AreSameTargets(
        IReadOnlyList<DesignSelectionTarget> left,
        IReadOnlyList<DesignSelectionTarget> right)
    {
        if (left.Count != right.Count)
            return false;

        // Порядок значим: первый элемент — primary target.
        for (var i = 0; i < left.Count; i++)
        {
            if (!ReferenceEquals(left[i].Target, right[i].Target))
                return false;
        }

        return true;
    }

    private static IReadOnlyList<DesignSelectionTarget> Difference(
        IReadOnlyList<DesignSelectionTarget> source,
        IReadOnlyList<DesignSelectionTarget> exclude)
    {
        List<DesignSelectionTarget>? result = null;

        for (var i = 0; i < source.Count; i++)
        {
            var candidate = source[i];
            var found = false;

            for (var j = 0; j < exclude.Count; j++)
            {
                if (ReferenceEquals(exclude[j].Target, candidate.Target))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
                (result ??= new List<DesignSelectionTarget>()).Add(candidate);
        }

        return result ?? (IReadOnlyList<DesignSelectionTarget>)Array.Empty<DesignSelectionTarget>();
    }

    private IReadOnlyList<DesignSelectionTarget> CreateSelectionTargetsSnapshot(DesignEditorItem? primaryItem, Control? primaryControl)
    {
        var result = new List<DesignSelectionTarget>();
        var dedup = new HashSet<Control>();

        if (primaryItem != null && primaryControl != null && dedup.Add(primaryControl))
            result.Add(new DesignSelectionTarget(primaryItem, primaryControl));

        var items = SelectedItems;
        if (items == null)
            return result;

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null)
                continue;

            foreach (var target in ResolveSelectionTargets(container))
            {
                if (!dedup.Add(target))
                    continue;

                result.Add(new DesignSelectionTarget(container, target));
            }
        }

        return result;
    }

    /// <summary>
    /// Возвращает ближайший design host target'а — контейнер, непосредственно
    /// внутри которого он лежит.
    /// </summary>
    /// <remarks>
    /// Для контрола внутри контейнера верхнего уровня это сам контейнер, для
    /// контрола внутри вложенного — вложенный, для вложенного контейнера — его владелец.
    /// Это единица группировки выделения: вместе выбираются только соседи по host'у.
    /// </remarks>
    internal static DesignEditorItem? FindDesignHost(Control target)
        => target.FindAncestorOfType<DesignEditorItem>();

    private IEnumerable<Control> EnumerateSelectedTargets() => _selectedTargets;

    /// <summary>
    /// Проверяет, что target лежит в том же design host, что и всё текущее выделение.
    /// </summary>
    /// <remarks>
    /// Правило точнее, чем «один item верхнего уровня»: два контрола из соседних
    /// вложенных контейнеров принадлежат одному item'у, но разным host'ам,
    /// и группировать их вместе нельзя.
    /// </remarks>
    private bool SharesDesignHostWithSelection(Control target)
    {
        var host = FindDesignHost(target);

        foreach (var selected in EnumerateSelectedTargets())
        {
            if (ReferenceEquals(selected, target))
                continue;

            if (!ReferenceEquals(FindDesignHost(selected), host))
                return false;
        }

        return true;
    }

    internal bool CanAddNestedTargetToContainer(DesignEditorItem container)
    {
        var items = SelectedItems;
        if (items == null || items.Count == 0)
            return true;

        DesignEditorItem? owner = null;
        foreach (var item in items)
        {
            var selectedContainer = ContainerFromItem(item) as DesignEditorItem;
            if (selectedContainer == null && item is DesignEditorItem directItem)
                selectedContainer = directItem;

            if (selectedContainer == null)
                continue;

            if (owner == null)
            {
                owner = selectedContainer;
                continue;
            }

            if (!ReferenceEquals(owner, selectedContainer))
                return false;
        }

        return owner == null || ReferenceEquals(owner, container);
    }

    private static bool IsOwnedByContainer(Visual visual, DesignEditorItem container)
    {
        var current = visual;
        while (current != null)
        {
            if (ReferenceEquals(current, container))
                return true;

            current = current.GetVisualParent();
        }

        return false;
    }

    internal bool ShouldUseContainerInteraction(KeyModifiers modifiers)
    {
        var requiredModifiers = InputGestures.ContainerInteractionModifiers;
        return requiredModifiers != KeyModifiers.None && modifiers.HasFlag(requiredModifiers);
    }

    internal bool ShouldUseAdditiveSelection(KeyModifiers modifiers)
    {
        var requiredModifiers = InputGestures.AdditiveSelectionModifiers;
        return requiredModifiers != KeyModifiers.None && modifiers.HasFlag(requiredModifiers);
    }

    internal bool ShouldStartPan(PointerPointProperties pointerProperties, KeyModifiers modifiers)
    {
        return MatchesModifiers(modifiers, InputGestures.PanModifiers)
               && IsPointerButtonPressed(pointerProperties, InputGestures.PanButton);
    }

    /// <summary>
    /// Определяет, должен ли контейнер уступить нажатие рамке выделения.
    /// </summary>
    /// <param name="container">Контейнер, получивший нажатие. Может быть вложенным.</param>
    /// <param name="viewportPoint">Точка нажатия в координатах редактора.</param>
    /// <param name="modifiers">Модификаторы ввода.</param>
    /// <remarks>
    /// Решение принимает редактор, а не состояние контейнера: политика ввода живёт
    /// в <see cref="InputGestures"/>, и контейнеру знать о ней незачем. Уступив жест,
    /// контейнер не захватывает указатель и не помечает событие обработанным,
    /// поэтому нажатие всплывает до редактора обычным маршрутом.
    /// <para>
    /// Контейнер удерживает жест, если нажат <see cref="DesignEditorInputGestures.ContainerInteractionModifiers"/>,
    /// если контейнер уже выбран целиком, либо если под точкой есть design target.
    /// </para>
    /// </remarks>
    internal bool ShouldDeferPressToMarquee(DesignEditorItem container, Point viewportPoint, KeyModifiers modifiers)
    {
        if (InputGestures.ContainerEmptyAreaDrag != ContainerEmptyAreaDragGesture.Marquee)
            return false;

        if (ShouldUseContainerInteraction(modifiers))
            return false;

        // Уже выбранный контейнер перетаскивается без модификаторов —
        // иначе его нельзя было бы двигать мышью вовсе.
        if (_selectedTargets.Contains(container))
            return false;

        var worldPoint = GetWorldPosition(viewportPoint);
        return !TryResolveSelectionTargetAtPoint(container, worldPoint, out _);
    }

    /// <summary>
    /// Определяет, должна ли действовать привязка при текущих модификаторах.
    /// </summary>
    internal bool ShouldSnap(KeyModifiers modifiers)
    {
        if (!InteractionOptions.IsSnapToGridEnabled)
            return false;

        if (IsSnapBypassed(modifiers))
            return false;

        return ResolveSnapStep() > 0;
    }

    /// <summary>
    /// Определяет, отключил ли пользователь привязку модификатором.
    /// </summary>
    /// <remarks>
    /// Модификатор отключает привязку целиком — и к сетке, и к направляющим.
    /// Обещание одно: «держу нажатым — ставлю куда хочу», и делить его между
    /// двумя видами привязки было бы нечестно.
    /// </remarks>
    private bool IsSnapBypassed(KeyModifiers modifiers)
    {
        var bypass = InputGestures.SnapBypassModifiers;
        return bypass != KeyModifiers.None && modifiers.HasFlag(bypass);
    }

    /// <summary>
    /// Возвращает действующий шаг привязки.
    /// </summary>
    /// <remarks>
    /// Явно заданный <see cref="DesignEditorInteractionOptions.SnapStep"/> имеет приоритет;
    /// иначе шаг берётся у сетки шаблона. Это не даёт сетке рисовать одну структуру,
    /// а привязке использовать другую.
    /// </remarks>
    internal double ResolveSnapStep()
    {
        var configured = InteractionOptions.SnapStep;
        if (!double.IsNaN(configured))
            return configured;

        return _grid?.CellSize ?? 0;
    }

    /// <summary>
    /// Округляет координату до ближайшего узла сетки.
    /// </summary>
    /// <remarks>
    /// Ровно посередине между узлами привязка уходит вверх — всегда и везде.
    /// <see cref="Math.Round(double)"/> здесь не годится: он округляет к чётному,
    /// поэтому на середине направление зависело бы от чётности узла, и при
    /// медленной протяжке край прыгал бы то вперёд, то назад.
    /// </remarks>
    internal double SnapCoordinate(double value)
    {
        var step = ResolveSnapStep();
        return step > 0 ? Math.Floor((value / step) + 0.5) * step : Math.Round(value);
    }

    /// <summary>
    /// Приводит позицию к сетке, если привязка активна.
    /// </summary>
    /// <remarks>
    /// Привязывается именно результат, а не смещение: округление дельты сохранило бы
    /// исходный сдвиг элемента, и на узел сетки он бы так и не встал.
    /// </remarks>
    internal Point SnapPosition(Point position, KeyModifiers modifiers)
    {
        if (!ShouldSnap(modifiers))
            return new Point(Math.Round(position.X), Math.Round(position.Y));

        return new Point(SnapCoordinate(position.X), SnapCoordinate(position.Y));
    }

    /// <summary>
    /// Снимает соседей, к которым будет идти выравнивание, на время жеста.
    /// </summary>
    /// <param name="movingTarget">Target, который ведёт жест.</param>
    /// <remarks>
    /// Соседи снимаются один раз, а не пересчитываются покадрово, и это не
    /// оптимизация: во время жеста они не двигаются, а вот layout-проход внутри
    /// жеста мог бы сдвинуть те самые линии, в которые пользователь целится.
    /// Снимок делает направляющую неподвижной ровно настолько, насколько она
    /// выглядит неподвижной.
    /// </remarks>
    internal void BeginSnapGuides(Control movingTarget)
    {
        _snapGuideNeighbours = InteractionOptions.IsSnapToGuidesEnabled
            ? CollectSnapGuideNeighbours(movingTarget)
            : Array.Empty<Rect>();
    }

    /// <summary>
    /// Закрывает жест: сбрасывает снимок соседей и убирает линии.
    /// </summary>
    internal void EndSnapGuides()
    {
        _snapGuideNeighbours = null;
        PublishSnapGuides(Array.Empty<DesignSnapGuide>());
    }

    /// <summary>
    /// Возвращает позицию перетаскиваемого target'а с учётом направляющих и сетки.
    /// </summary>
    /// <param name="target">Перетаскиваемый target.</param>
    /// <param name="proposed">Позиция, предложенная жестом.</param>
    /// <param name="modifiers">Модификаторы текущего ввода.</param>
    /// <remarks>
    /// Композиция та же, что у остальных правил редактора: направляющая занимает ось,
    /// сетка получает всё остальное. Оси независимы — элемент может встать на край
    /// соседа по X и на узел сетки по Y, и это ровно то, чего ждёт пользователь.
    /// <para>
    /// Как и привязка к сетке, направляющие работают с <b>результатом</b>, а не с
    /// дельтой: смещение считается от предложенной позиции, поэтому элемент встаёт
    /// на линию, а не сохраняет прежний зазор до неё.
    /// </para>
    /// </remarks>
    internal Point ResolveDragPosition(Control target, Point proposed, KeyModifiers modifiers)
    {
        var neighbours = _snapGuideNeighbours;

        if (neighbours is not { Count: > 0 } || IsSnapBypassed(modifiers))
        {
            PublishSnapGuides(Array.Empty<DesignSnapGuide>());
            return SnapPosition(proposed, modifiers);
        }

        var size = GetDesignSize(target);
        var snapToGrid = ShouldSnap(modifiers);

        DesignSnapGuideResolver.TryResolveOffset(
            new Rect(proposed, size),
            neighbours,
            ResolveSnapGuideTolerance(),
            out var offset,
            out var snappedX,
            out var snappedY);

        var x = snappedX ? proposed.X + offset.X : GridCoordinate(proposed.X, snapToGrid);
        var y = snappedY ? proposed.Y + offset.Y : GridCoordinate(proposed.Y, snapToGrid);

        var result = new Point(x, y);
        PublishSnapGuides(DesignSnapGuideResolver.CollectGuides(new Rect(result, size), neighbours));
        return result;

        double GridCoordinate(double value, bool snap) => snap ? SnapCoordinate(value) : Math.Round(value);
    }

    /// <summary>
    /// Определяет, может ли сработать привязка двигающегося края при resize.
    /// </summary>
    /// <remarks>
    /// Спрашивается на входе в блок привязки, чтобы не трогать геометрию там,
    /// где ни сетка, ни направляющие не действуют: в остальном resize оставляет
    /// координаты как есть и не округляет их, в отличие от перетаскивания.
    /// </remarks>
    internal bool CanSnapResizeEdge(KeyModifiers modifiers)
    {
        if (IsSnapBypassed(modifiers))
            return false;

        return ShouldSnap(modifiers) || HasSnapGuideNeighbours;
    }

    private bool HasSnapGuideNeighbours
        => InteractionOptions.IsSnapToGuidesEnabled && _snapGuideNeighbours is { Count: > 0 };

    /// <summary>
    /// Возвращает координату двигающегося края с учётом направляющих и сетки.
    /// </summary>
    /// <param name="edge">Координата края по своей оси.</param>
    /// <param name="xAxis">Признак горизонтальной оси.</param>
    /// <param name="modifiers">Модификаторы текущего ввода.</param>
    /// <remarks>
    /// Та же композиция, что и при перетаскивании: направляющая занимает ось,
    /// сетка получает всё остальное. Разница лишь в том, что здесь снимается
    /// один край, а не позиция целиком, — накапливать тут нечего, потому что
    /// край приходит уже посчитанным от применённой геометрии.
    /// </remarks>
    internal double ResolveResizeEdge(double edge, bool xAxis, KeyModifiers modifiers)
    {
        if (IsSnapBypassed(modifiers))
            return edge;

        if (HasSnapGuideNeighbours &&
            DesignSnapGuideResolver.TryResolveEdge(
                edge, _snapGuideNeighbours!, ResolveSnapGuideTolerance(), xAxis, out var guided))
        {
            return guided;
        }

        return ShouldSnap(modifiers) ? SnapCoordinate(edge) : edge;
    }

    /// <summary>
    /// Публикует направляющие по итоговой геометрии жеста изменения размера.
    /// </summary>
    internal void PublishResizeGuides(Rect bounds)
    {
        PublishSnapGuides(HasSnapGuideNeighbours
            ? DesignSnapGuideResolver.CollectGuides(bounds, _snapGuideNeighbours!)
            : Array.Empty<DesignSnapGuide>());
    }

    /// <summary>
    /// Возвращает радиус захвата направляющей в мировых единицах.
    /// </summary>
    internal double ResolveSnapGuideTolerance()
    {
        var tolerance = InteractionOptions.SnapGuideTolerance;
        if (!(tolerance > 0))
            return 0;

        var zoom = ViewportZoom;
        return zoom > 0 ? tolerance / zoom : tolerance;
    }

    /// <summary>
    /// Собирает прямоугольники, по которым идёт выравнивание.
    /// </summary>
    /// <remarks>
    /// Соседями считается то же, что редактор разрешает выбрать: правило одно,
    /// и выровняться можно ровно по тому, что видно как отдельный элемент.
    /// К ним добавляются границы самой формы — по её краям и центру выравнивают чаще
    /// всего, а отдельным элементом она не является.
    /// </remarks>
    private IReadOnlyList<Rect> CollectSnapGuideNeighbours(Control movingTarget)
    {
        var neighbours = new List<Rect>();
        var host = FindDesignHost(movingTarget);

        if (host == null)
        {
            // Двигают контейнер верхнего уровня: соседи — остальные контейнеры.
            foreach (var container in EnumerateContainers())
            {
                if (IsExcludedFromSnapGuides(container, movingTarget))
                    continue;

                if (TryGetDesignBounds((Control)container, out var containerBounds))
                    neighbours.Add(containerBounds);
            }

            return neighbours;
        }

        foreach (var candidate in EnumerateSelectionCandidates(host))
        {
            if (!IsSelectableTarget(candidate, host))
                continue;

            if (IsExcludedFromSnapGuides(candidate, movingTarget))
                continue;

            if (TryGetDesignBounds(candidate, out var bounds))
                neighbours.Add(bounds);
        }

        // Приведение к Control обязательно: перегрузка для DesignEditorItem
        // вернула бы геометрию его выбранного target'а, а не самой формы.
        if (TryGetDesignBounds((Control)host, out var hostBounds))
            neighbours.Add(hostBounds);

        return neighbours;
    }

    /// <summary>
    /// Определяет, участвует ли кандидат в выравнивании.
    /// </summary>
    /// <remarks>
    /// Исключается всё, что двигается вместе с жестом, — сам target, остальное
    /// выделение при групповом перетаскивании и их родня по дереву. Иначе элемент
    /// выравнивался бы сам по себе и линия висела бы на нём всю протяжку.
    /// </remarks>
    private bool IsExcludedFromSnapGuides(Control candidate, Control movingTarget)
    {
        if (IsSameOrRelated(candidate, movingTarget))
            return true;

        foreach (var selected in _selectedTargets)
        {
            if (IsSameOrRelated(candidate, selected))
                return true;
        }

        return false;
    }

    private static bool IsSameOrRelated(Control first, Control second)
    {
        if (ReferenceEquals(first, second))
            return true;

        foreach (var ancestor in first.GetVisualAncestors())
        {
            if (ReferenceEquals(ancestor, second))
                return true;
        }

        foreach (var ancestor in second.GetVisualAncestors())
        {
            if (ReferenceEquals(ancestor, first))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Публикует набор направляющих, если он действительно изменился.
    /// </summary>
    /// <remarks>
    /// Та же дисциплина, что у <see cref="ApplySelectionSnapshot"/>: метод вызывается
    /// на каждом кадре протяжки, а линии меняются редко. Без сравнения слой
    /// перерисовывался бы каждый кадр впустую.
    /// </remarks>
    private void PublishSnapGuides(IReadOnlyList<DesignSnapGuide> guides)
    {
        if (AreSameGuides(_snapGuides, guides))
            return;

        SnapGuides = guides;
    }

    private static bool AreSameGuides(IReadOnlyList<DesignSnapGuide> first, IReadOnlyList<DesignSnapGuide> second)
    {
        if (ReferenceEquals(first, second))
            return true;

        if (first.Count != second.Count)
            return false;

        for (var i = 0; i < first.Count; i++)
        {
            if (!first[i].Equals(second[i]))
                return false;
        }

        return true;
    }

    internal bool ShouldStartMarquee(PointerPointProperties pointerProperties, KeyModifiers modifiers)
    {
        return MatchesModifiers(modifiers, InputGestures.MarqueeModifiers)
               && IsPointerButtonPressed(pointerProperties, InputGestures.MarqueeButton);
    }

    internal bool ShouldHandleZoom(KeyModifiers modifiers)
    {
        return MatchesModifiers(modifiers, InputGestures.ZoomModifiers);
    }

    private static bool MatchesModifiers(KeyModifiers actual, KeyModifiers required)
    {
        return required == KeyModifiers.None || actual.HasFlag(required);
    }

    private static bool IsPointerButtonPressed(PointerPointProperties pointerProperties, DesignEditorPointerButton button)
    {
        return button switch
        {
            DesignEditorPointerButton.Left => pointerProperties.IsLeftButtonPressed,
            DesignEditorPointerButton.Middle => pointerProperties.IsMiddleButtonPressed,
            DesignEditorPointerButton.Right => pointerProperties.IsRightButtonPressed,
            _ => false
        };
    }

    private static IEnumerable<Control> EnumerateSelectionCandidates(DesignEditorItem item)
    {
        if (item.ContentMode == DesignContentMode.Loaded)
            return EnumerateAuthoredContent(item);

        return EnumerateVisualCandidates(item);
    }

    private static IEnumerable<Control> EnumerateVisualCandidates(DesignEditorItem item)
    {
        foreach (var descendant in item.GetVisualDescendants())
        {
            if (descendant is Control control &&
                !ReferenceEquals(control, item) &&
                IsOwnedByContainer(control, item))
            {
                yield return control;
            }
        }
    }

    /// <summary>
    /// Перечисляет элементы, написанные в разметке, не спускаясь во внутренности контролов.
    /// </summary>
    /// <remarks>
    /// Обход идёт по тем связям, которые автор задал сам: дети панели, ребёнок
    /// декоратора, контент — если это контрол. У кнопки с текстовым контентом
    /// спускаться некуда, поэтому она остаётся листом.
    /// <para>
    /// Проверки <c>TemplatedParent</c> здесь недостаточно: презентер порождает
    /// из строкового контента <c>AccessText</c>, у которого <c>TemplatedParent</c>
    /// пуст, и клик по кнопке выбирал бы её надпись.
    /// </para>
    /// </remarks>
    private static IEnumerable<Control> EnumerateAuthoredContent(DesignEditorItem item)
    {
        var root = (item.Presenter as Control)?.GetVisualChildren().OfType<Control>().FirstOrDefault()
                   ?? item.Content as Control;

        return root == null ? Array.Empty<Control>() : Descend(root);

        static IEnumerable<Control> Descend(Control control)
        {
            yield return control;

            foreach (var child in AuthoredChildren(control))
            {
                foreach (var nested in Descend(child))
                    yield return nested;
            }
        }
    }

    private static IEnumerable<Control> AuthoredChildren(Control control)
    {
        switch (control)
        {
            case Panel panel:
                foreach (var child in panel.Children)
                    yield return child;
                break;

            case Decorator { Child: { } decorated }:
                yield return decorated;
                break;

            case ContentControl { Content: Control content }:
                yield return content;
                break;

            case ContentPresenter { Content: Control presented }:
                yield return presented;
                break;
        }
    }

    /// <summary>
    /// Перечисляет контейнеры редактора на любой глубине вложенности.
    /// </summary>
    /// <remarks>
    /// Во время жеста, снявшего снимок через <see cref="BeginContainerSnapshot"/>,
    /// отдаёт этот снимок вместо нового обхода.
    /// </remarks>
    private IEnumerable<DesignEditorItem> EnumerateContainers() =>
        _containerSnapshot ?? EnumerateContainersCore();

    /// <summary>
    /// Снимает список контейнеров на время жеста.
    /// </summary>
    /// <remarks>
    /// Обход стоит не по числу контейнеров, а по размеру всего макета: <see cref="EnumerateContainersCore"/>
    /// спускается в поддерево каждого контейнера верхнего уровня, а рамка спрашивает его на
    /// каждом кадре протяжки. Замер на 500 контейнерах: 0,44 мс на кадр у форм из двух
    /// контролов и 1,66 мс у форм из тридцати — то есть цена растёт вместе с макетом,
    /// а не с числом форм, и упирается в неё как раз тот проект, который дорос до неё.
    /// <para>
    /// Снимок берётся один раз по той же причине, что и соседи в <see cref="BeginSnapGuides"/>,
    /// и это не только про цену: набор контейнеров внутри жеста меняться не должен, иначе
    /// рамка охватывала бы одно, а применяла к другому. Окно снимка накрывает и
    /// <c>CommitSelection</c> — он вызывается до выхода из состояния, — поэтому
    /// применённое выделение считается по тем же контейнерам, которые рамка измеряла.
    /// </para>
    /// </remarks>
    internal void BeginContainerSnapshot() => _containerSnapshot = EnumerateContainersCore().ToList();

    /// <summary>
    /// Отпускает снимок контейнеров: следующий обход снова идёт по дереву.
    /// </summary>
    internal void EndContainerSnapshot() => _containerSnapshot = null;

    private IEnumerable<DesignEditorItem> EnumerateContainersCore()
    {
        if (Presenter?.Panel == null)
            yield break;

        foreach (var child in Presenter.Panel.Children)
        {
            if (child is not DesignEditorItem container)
                continue;

            yield return container;

            foreach (var descendant in container.GetVisualDescendants())
            {
                if (descendant is DesignEditorItem nested)
                    yield return nested;
            }
        }
    }

    /// <summary>
    /// Возвращает геометрию контейнера в мировых координатах.
    /// </summary>
    /// <remarks>
    /// Для вложенных контейнеров <see cref="DesignEditorItem.Location"/> задан
    /// относительно родительской панели, поэтому позиция берётся из design-геометрии,
    /// а на неё падает fallback только для контейнеров верхнего уровня.
    /// </remarks>
    private bool TryGetContainerWorldBounds(DesignEditorItem container, out Rect bounds)
    {
        if (TryGetDesignBounds((Control)container, out bounds))
            return true;

        if (container.Bounds.Width <= 0 || container.Bounds.Height <= 0)
        {
            bounds = default;
            return false;
        }

        bounds = new Rect(container.Location, container.Bounds.Size);
        return true;
    }

    private static int GetContainerDepth(DesignEditorItem container)
    {
        var depth = 0;
        var current = container.GetVisualParent();

        while (current != null)
        {
            if (current is DesignEditorItem)
                depth++;

            current = current.GetVisualParent();
        }

        return depth;
    }

    private DesignEditorItem? FindContainerAtWorldPoint(Point worldPoint)
    {
        DesignEditorItem? bestMatch = null;
        var bestDepth = -1;

        foreach (var container in EnumerateContainers())
        {
            if (!TryGetContainerWorldBounds(container, out var bounds) || !bounds.Contains(worldPoint))
                continue;

            // Глубочайший контейнер под точкой: вложенный перекрывает владельца.
            var depth = GetContainerDepth(container);
            if (depth > bestDepth)
            {
                bestMatch = container;
                bestDepth = depth;
            }
        }

        return bestMatch;
    }

    private DesignEditorItem? FindContainerForMarquee(Rect bounds)
    {
        // Владельцем рамки становится самый глубокий контейнер, который целиком её
        // содержит: рамка внутри вложенного контейнера работает в его пределах,
        // а рамка, вышедшая за его границы, поднимается к владельцу.
        DesignEditorItem? containing = null;
        var containingDepth = -1;

        DesignEditorItem? bestOverlap = null;
        var bestArea = 0.0;

        foreach (var container in EnumerateContainers())
        {
            if (!TryGetContainerWorldBounds(container, out var containerBounds))
                continue;

            if (containerBounds.Contains(bounds))
            {
                var depth = GetContainerDepth(container);
                if (depth > containingDepth)
                {
                    containing = container;
                    containingDepth = depth;
                }

                continue;
            }

            var intersection = containerBounds.Intersect(bounds);
            if (intersection.Width <= 0 || intersection.Height <= 0)
                continue;

            var area = intersection.Width * intersection.Height;
            if (area > bestArea)
            {
                bestArea = area;
                bestOverlap = container;
            }
        }

        return containing ?? bestOverlap;
    }

    private bool TryCreateGroupResizeOperation(ResizeDirection direction, out GroupResizeOperation? operation)
    {
        operation = null;

        if (!TryGetSelectedDesignBounds(out var selectionBounds, out var selectedCount, out _, out _, out _, out _, out _)
            || selectedCount <= 1)
        {
            return false;
        }

        var targets = new List<GroupResizeTarget>();
        var items = SelectedItems;
        if (items == null)
            return false;

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null)
                continue;

            foreach (var target in ResolveSelectionTargets(container))
            {
                if (!IsResizeAllowed(target, direction))
                    return false;

                if (!TryGetDesignBounds(target, out var bounds))
                    continue;

                SetDesignSize(target, GetDesignSize(target));

                targets.Add(new GroupResizeTarget(target, bounds));
            }
        }

        if (targets.Count <= 1)
            return false;

        operation = new GroupResizeOperation(
            direction,
            selectionBounds,
            targets,
            Math.Max(0.0, InteractionOptions.ResizeMinSize));

        return true;
    }

    /// <summary>
    /// Открывает единицу редактирования. Вызывается на старте жеста, до первой мутации.
    /// </summary>
    /// <summary>
    /// Признак открытой единицы редактирования.
    /// </summary>
    /// <remarks>
    /// По нему состояние перетаскивания понимает, принят жест или отклонён:
    /// <c>e.Handled</c> для этого не годится — редактор ставит его на всех ветках,
    /// включая успешную. Открытая единица есть только на успешной.
    /// </remarks>
    internal bool HasActiveEdit => _activeEdit != null;

    /// <summary>
    /// Открывает единицу редактирования.
    /// </summary>
    /// <remarks>
    /// Осиротевшая единица не затирается, а фиксируется. Раньше здесь стояло простое
    /// присваивание: если предыдущий жест закончился, не закрыв свою единицу, — а он
    /// может, у трёх завершений resize есть ранние выходы до <see cref="CommitEdit"/>, —
    /// то следующий жест молча уничтожал её, и правка пользователя исчезала из undo
    /// без единого признака. Поздняя запись хуже своевременной, но несравнимо лучше
    /// потерянной.
    /// </remarks>
    private void BeginEdit(DesignEditKind kind)
    {
        if (_activeEdit != null)
            CommitEdit();

        _activeEdit = new DesignEditScope(kind);
    }

    /// <summary>
    /// Закрывает единицу редактирования и публикует изменения, если они есть.
    /// </summary>
    private void CommitEdit()
    {
        var scope = _activeEdit;
        _activeEdit = null;

        if (scope == null)
            return;

        var changes = scope.BuildChanges();
        if (changes.Count == 0)
            return;

        EditCompleted?.Invoke(this, new DesignEditCompletedEventArgs(scope.Kind, changes));
    }

    /// <summary>
    /// Отбрасывает единицу редактирования, не публикуя изменения.
    /// </summary>
    private void CancelEdit() => _activeEdit = null;

    /// <summary>
    /// Обновляет диагностический вывод о размещении primary target.
    /// </summary>
    private void UpdatePrimaryPlacementReadout(Control? primaryControl)
    {
        if (primaryControl == null)
        {
            PrimarySelectionPlacement = null;
            PrimarySelectionMovePolicy = ArxisStudio.Attached.MovePolicy.None;
            PrimarySelectionResizePolicy = ArxisStudio.Attached.ResizePolicy.None;
            return;
        }

        PrimarySelectionPlacement = GetPlacementStrategy(primaryControl).Name;
        PrimarySelectionMovePolicy = GetEffectiveMovePolicy(primaryControl);
        PrimarySelectionResizePolicy = GetResizePolicy(primaryControl);
    }

    /// <summary>
    /// Обновляет индикатор точки вставки.
    /// </summary>
    internal void UpdateReorderIndicator(Rect designBounds)
    {
        ReorderIndicator = designBounds;
        IsReordering = true;
    }


    /// <summary>
    /// Сообщает намерение переставить контрол и убирает индикатор.
    /// </summary>
    /// <returns><see langword="true"/>, если перестановку выполнил обработчик.</returns>
    /// <remarks>
    /// Результат не декоративный: им помечается само нажатие. Отказ обработчика
    /// оставляет событие необработанным, и оно всплывает дальше — так же, как
    /// у <see cref="DeleteRequested"/>. Иначе <c>Handled</c> оказался бы
    /// свойством, которое никто не читает.
    /// </remarks>
    internal bool CommitReorder(Control target, int insertBefore)
    {
        var handled = RequestChildIndex(target, insertBefore);
        ClearReorderIndicator();
        return handled;
    }

    /// <summary>
    /// Прерывает перестановку, ничего не сообщая.
    /// </summary>
    internal void CancelReorder() => ClearReorderIndicator();

    private void ClearReorderIndicator()
    {
        IsReordering = false;
        ReorderIndicator = default;
    }

    private void UpdateInteractionOperation(IInteractionOperation operation, Vector worldDelta)
    {
        operation.Update(this, worldDelta);
    }

    private void CompleteInteractionOperation<TOperation>(ref TOperation? operation)
        where TOperation : class, IInteractionOperation
    {
        operation?.Complete(this);
        operation = null;
    }
}
