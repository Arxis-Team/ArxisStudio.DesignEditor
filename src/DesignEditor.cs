using System;
using System.Collections;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Selection;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DesignLayout = ArxisStudio.Attached.Layout;
using ArxisStudio.Controls;
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
    public EditorState CurrentState => _states.Count > 0 ? _states.Peek() : null!;

    /// <summary>
    /// Помещает новое состояние в стек и вызывает его инициализацию.
    /// </summary>
    /// <param name="state">Состояние, которое должно стать активным.</param>
    public void PushState(EditorState state)
    {
        var previous = _states.Count > 0 ? _states.Peek() : null;
        _states.Push(state);
        state.Enter(previous);
    }

    /// <summary>
    /// Завершает текущее состояние и возвращается к предыдущему, если стек содержит более одного состояния.
    /// </summary>
    public void PopState()
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
    /// Идентификатор темы для прямоугольника выделения.
    /// </summary>
    public static readonly StyledProperty<ControlTheme> SelectionRectangleStyleProperty =
        AvaloniaProperty.Register<DesignEditor, ControlTheme>(nameof(SelectionRectangleStyle));

    /// <summary>
    /// Идентификатор темы рамки одиночного выделения.
    /// </summary>
    public static readonly StyledProperty<ControlTheme> SelectionOutlineStyleProperty =
        AvaloniaProperty.Register<DesignEditor, ControlTheme>(nameof(SelectionOutlineStyle));

    /// <summary>
    /// Идентификатор темы рамки группового выделения.
    /// </summary>
    public static readonly StyledProperty<ControlTheme> GroupSelectionOutlineStyleProperty =
        AvaloniaProperty.Register<DesignEditor, ControlTheme>(nameof(GroupSelectionOutlineStyle));

    /// <summary>
    /// Идентификатор темы secondary outline для multi-selection.
    /// </summary>
    public static readonly StyledProperty<ControlTheme> SecondarySelectionOutlineStyleProperty =
        AvaloniaProperty.Register<DesignEditor, ControlTheme>(nameof(SecondarySelectionOutlineStyle));

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
    public static readonly DirectProperty<DesignEditor, IReadOnlyList<SelectionAdornerInfo>> SecondarySelectionAdornersProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, IReadOnlyList<SelectionAdornerInfo>>(
            nameof(SecondarySelectionAdorners),
            o => o.SecondarySelectionAdorners,
            (o, v) => o.SecondarySelectionAdorners = v);

    /// <summary>
    /// Идентификатор количества secondary selection adorner'ов.
    /// </summary>
    public static readonly DirectProperty<DesignEditor, int> SecondarySelectionAdornersCountProperty =
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
    /// Получает или задает тему визуализации рамки выделения.
    /// </summary>
    public ControlTheme SelectionRectangleStyle
    {
        get => GetValue(SelectionRectangleStyleProperty);
        set => SetValue(SelectionRectangleStyleProperty, value);
    }

    /// <summary>
    /// Получает или задает тему рамки одиночного выделения.
    /// </summary>
    public ControlTheme SelectionOutlineStyle
    {
        get => GetValue(SelectionOutlineStyleProperty);
        set => SetValue(SelectionOutlineStyleProperty, value);
    }

    /// <summary>
    /// Получает или задает тему рамки группового выделения.
    /// </summary>
    public ControlTheme GroupSelectionOutlineStyle
    {
        get => GetValue(GroupSelectionOutlineStyleProperty);
        set => SetValue(GroupSelectionOutlineStyleProperty, value);
    }

    /// <summary>
    /// Получает или задает тему secondary outline для каждого target в multi-selection.
    /// </summary>
    public ControlTheme SecondarySelectionOutlineStyle
    {
        get => GetValue(SecondarySelectionOutlineStyleProperty);
        set => SetValue(SecondarySelectionOutlineStyleProperty, value);
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

    private Rect _selectedArea;
    /// <summary>
    /// Получает или задает текущий прямоугольник выделения в мировых координатах.
    /// </summary>
    public Rect SelectedArea
    {
        get => _selectedArea;
        set => SetAndRaise(SelectedAreaProperty, ref _selectedArea, value);
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
    public IReadOnlyList<SelectionAdornerInfo> SecondarySelectionAdorners
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
    public int SecondarySelectionAdornersCount => _secondarySelectionAdornersCount;

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

    #endregion

    #region Internal Helpers

    private Point _lastMousePosition;
    internal KeyModifiers LastInputModifiers { get; private set; }

    private SelectionAdorner? _selectionAdorner;
    private SelectionAdorner? _groupSelectionAdorner;
    private SelectionAdornerLayer? _secondarySelectionAdornerLayer;
    private DesignEditorItem? _primarySelectionItem;
    private Control? _primarySelectionControl;
    private DesignEditorItem? _marqueeSelectionOwner;
    private readonly Dictionary<DesignEditorItem, List<Control>> _selectionTargets = new();
    private readonly HashSet<DesignEditorItem> _containerSelectionTargets = new();
    private GroupResizeSession? _groupResizeSession;
    private GroupDragSession? _groupDragSession;

    private readonly TranslateTransform _translateTransform = new TranslateTransform();
    private readonly ScaleTransform _scaleTransform = new ScaleTransform();
    private readonly TranslateTransform _dpiTranslateTransform = new TranslateTransform();

    private sealed class GroupResizeSession
    {
        public ResizeDirection Direction { get; set; }
        public Rect InitialBounds { get; set; }
        public IReadOnlyList<GroupResizeTargetSnapshot> Targets { get; set; } = Array.Empty<GroupResizeTargetSnapshot>();
        public Vector AccumulatedDelta { get; set; }
    }

    private sealed class GroupResizeTargetSnapshot
    {
        public DesignEditorItem Container { get; set; } = null!;
        public Control Target { get; set; } = null!;
        public Rect InitialBounds { get; set; }
    }

    private sealed class GroupDragSession
    {
        public DesignEditorItem SourceContainer { get; set; } = null!;
        public Control SourceTarget { get; set; } = null!;
        public IReadOnlyList<GroupDragTargetSnapshot> Targets { get; set; } = Array.Empty<GroupDragTargetSnapshot>();
        public Vector AccumulatedDelta { get; set; }
    }

    private sealed class GroupDragTargetSnapshot
    {
        public Control Target { get; set; } = null!;
        public Point InitialPosition { get; set; }
    }
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
        DesignLayout.DesignXProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (control.FindAncestorOfType<DesignEditor>() is { } editor && editor.IsSelectionOverlayControl(control))
                editor.UpdateSelectionOverlayState();
        });
        DesignLayout.DesignYProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (control.FindAncestorOfType<DesignEditor>() is { } editor && editor.IsSelectionOverlayControl(control))
                editor.UpdateSelectionOverlayState();
        });
        BoundsProperty.Changed.AddClassHandler<Control>((control, _) =>
        {
            if (control.FindAncestorOfType<DesignEditor>() is { } editor && editor.IsSelectionOverlayControl(control))
                editor.UpdateSelectionOverlayState();
        });
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

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        return NeedsContainer<DesignEditorItem>(item, out recycleKey);
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return new DesignEditorItem();
    }

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
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (e.Root is TopLevel topLevel) topLevel.ScalingChanged += OnScreenScalingChanged;
        UpdateTransforms();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (e.Root is TopLevel topLevel) topLevel.ScalingChanged -= OnScreenScalingChanged;
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

        var root = this.GetVisualRoot();
        double renderScaling = root?.RenderScaling ?? 1.0;

        _dpiTranslateTransform.X = Math.Round(x * renderScaling) / renderScaling;
        _dpiTranslateTransform.Y = Math.Round(y * renderScaling) / renderScaling;

        var vg = new TransformGroup(); vg.Children.Add(_scaleTransform); vg.Children.Add(_translateTransform);
        SetCurrentValue(ViewportTransformProperty, vg);

        var dg = new TransformGroup(); dg.Children.Add(_scaleTransform); dg.Children.Add(_dpiTranslateTransform);
        SetCurrentValue(DpiScaledViewportTransformProperty, dg);
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
    public void HandleZoom(PointerWheelEventArgs e)
    {
        if (!ShouldHandleZoom(e.KeyModifiers))
            return;

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
                if (ReferenceEquals(selectionTarget, container))
                    containerTargetCount++;
                else
                    nestedTargetCount++;

                perTargetBounds.Add(new SelectionAdornerInfo
                {
                    Container = container,
                    Target = selectionTarget,
                    Bounds = itemBounds,
                    Role = SelectionAdornerRole.Secondary
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
            SelectedDesignTargets = CreateSelectionTargetsSnapshot(primaryItem, primaryControl);
            PrimarySelectionTarget = SelectedDesignTargets.Count > 0 ? SelectedDesignTargets[0] : null;
            return;
        }

        _selectionTargets.Clear();
        _containerSelectionTargets.Clear();
        SelectionBounds = default;
        SecondarySelectionAdorners = Array.Empty<SelectionAdornerInfo>();
        HasSingleSelection = false;
        HasMultipleSelection = false;
        HasMultipleNestedSelection = false;
        HasMultipleContainerSelection = false;
        _primarySelectionItem = null;
        _primarySelectionControl = null;
        SelectedDesignTargets = Array.Empty<DesignSelectionTarget>();
        PrimarySelectionTarget = null;
    }

    internal void CommitSelection(Rect bounds, bool isCtrlPressed)
    {
        if (Presenter?.Panel == null) return;
        var useContainerSelection = ShouldUseContainerInteraction(LastInputModifiers);
        var marqueeOwner = useContainerSelection ? null : (_marqueeSelectionOwner ?? FindContainerForMarquee(bounds));
        if (!useContainerSelection && isCtrlPressed && marqueeOwner != null && !CanAddNestedTargetToContainer(marqueeOwner))
        {
            _marqueeSelectionOwner = null;
            return;
        }

        using (Selection.BatchUpdate())
        {
            if (!isCtrlPressed) Selection.Clear();

            foreach (var child in Presenter.Panel.Children)
            {
                if (child is DesignEditorItem container)
                {
                    if (marqueeOwner != null && !ReferenceEquals(container, marqueeOwner))
                        continue;

                    if (useContainerSelection)
                    {
                        var intersectsContainer = bounds.Intersects(new Rect(container.Location, container.Bounds.Size));
                        if (!intersectsContainer)
                            continue;

                        _selectionTargets.Remove(container);
                        _containerSelectionTargets.Add(container);
                        Selection.Select(IndexFromContainer(container));
                        continue;
                    }

                    var nestedTargets = new List<Control>();
                    foreach (var target in EnumerateSelectionCandidates(container))
                    {
                        if (!HasDesignerLayoutMetadata(target))
                            continue;

                        if (TryGetDesignBounds(target, out var targetBounds) && bounds.Intersects(targetBounds))
                            nestedTargets.Add(target);
                    }

                    if (nestedTargets.Count == 0)
                        continue;

                    if (isCtrlPressed &&
                        _selectionTargets.TryGetValue(container, out var existingTargets) &&
                        existingTargets.Count > 0)
                    {
                        foreach (var target in existingTargets)
                        {
                            if (!nestedTargets.Contains(target))
                                nestedTargets.Add(target);
                        }
                    }

                    _containerSelectionTargets.Remove(container);
                    _selectionTargets[container] = nestedTargets;
                    Selection.Select(IndexFromContainer(container));
                }
            }
        }

        _marqueeSelectionOwner = null;
        UpdateSelectionOverlayState();
    }

    // --- Input Handling ---

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _lastMousePosition = e.GetPosition(this);
        LastInputModifiers = e.KeyModifiers;

        CurrentState.OnPointerPressed(e);

        if (!e.Handled) base.OnPointerPressed(e);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        _lastMousePosition = e.GetPosition(this);
        CurrentState.OnPointerMoved(e);
        base.OnPointerMoved(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        CurrentState.OnPointerReleased(e);
        base.OnPointerReleased(e);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        if (e.Handled) return;
        CurrentState.OnPointerWheelChanged(e);
        e.Handled = true;
    }

    // --- Drag & Drop ---

    private void OnItemsDragStarted(DragStartedEventArgs e)
    {
        _groupDragSession = null;

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

        var sourceTarget = ResolveInteractionTarget(sourceContainer);
        var targets = new List<GroupDragTargetSnapshot>();

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null || !container.IsDraggable)
                continue;

            foreach (var target in ResolveSelectionTargets(container))
            {
                if (ReferenceEquals(container, sourceContainer) && ReferenceEquals(target, sourceTarget))
                    continue;

                targets.Add(new GroupDragTargetSnapshot
                {
                    Target = target,
                    InitialPosition = GetDesignPosition(target)
                });
            }
        }

        if (targets.Count > 0)
        {
            _groupDragSession = new GroupDragSession
            {
                SourceContainer = sourceContainer,
                SourceTarget = sourceTarget,
                Targets = targets,
                AccumulatedDelta = Vector.Zero
            };
        }

        e.Handled = true;
    }

    private void OnItemsDragDelta(DragDeltaEventArgs e)
    {
        if (IsSelecting || CurrentState is EditorPanningState) return;

        var items = SelectedItems;
        if (items == null || items.Count == 0) return;

        if (_groupDragSession != null &&
            e.Source is DesignEditorItem sourceContainer &&
            ReferenceEquals(sourceContainer, _groupDragSession.SourceContainer))
        {
            _groupDragSession.AccumulatedDelta += new Vector(e.HorizontalChange, e.VerticalChange);

            foreach (var snapshot in _groupDragSession.Targets)
                SetDesignPosition(snapshot.Target, snapshot.InitialPosition + _groupDragSession.AccumulatedDelta);

            e.Handled = true;
            UpdateSelectionOverlayState();
            return;
        }

        var delta = new Vector(e.HorizontalChange, e.VerticalChange);
        var source = e.Source as DesignEditorItem;

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null || !container.IsDraggable || ReferenceEquals(container, source))
                continue;

            var target = ResolveInteractionTarget(container);
            var position = GetDesignPosition(target);
            SetDesignPosition(target, position + delta);
        }
        e.Handled = true;
        UpdateSelectionOverlayState();
    }

    private void OnItemsDragCompleted(DragCompletedEventArgs e)
    {
        _groupDragSession = null;
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

        _primarySelectionItem.PushState(new ItemResizingState(_primarySelectionItem, _primarySelectionControl, e.Direction));
        _primarySelectionItem.OnResizeStarted(e.Vector);
        e.Handled = true;
    }

    private void OnSelectionResizeDelta(object? sender, ResizeDeltaEventArgs e)
    {
        if (_primarySelectionItem == null || _primarySelectionControl == null || _primarySelectionItem.CurrentState is not ItemResizingState)
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
        e.Handled = true;
    }

    private void OnSecondarySelectionResizeStarted(object? sender, SelectionAdornerResizeStartedEventArgs e)
    {
        var container = e.AdornerInfo.Container;
        var target = e.AdornerInfo.Target;

        if (container == null || target == null || !HasMultipleNestedSelection)
            return;

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
        e.Handled = true;
    }

    private void OnGroupSelectionResizeStarted(object? sender, ResizeStartedEventArgs e)
    {
        if (!HasMultipleContainerSelection || !TryCreateGroupResizeSession(e.Direction, out var session))
            return;

        _groupResizeSession = session;
        e.Handled = true;
    }

    private void OnGroupSelectionResizeDelta(object? sender, ResizeDeltaEventArgs e)
    {
        if (_groupResizeSession == null)
            return;

        _groupResizeSession.AccumulatedDelta += NormalizeResizeDelta(e.Delta);
        var nextBounds = CalculateResizedBounds(
            _groupResizeSession.InitialBounds,
            _groupResizeSession.Direction,
            _groupResizeSession.AccumulatedDelta,
            Math.Max(0.0, InteractionOptions.ResizeMinSize));
        var initialBounds = _groupResizeSession.InitialBounds;
        var scaleX = initialBounds.Width > 0 ? nextBounds.Width / initialBounds.Width : 1.0;
        var scaleY = initialBounds.Height > 0 ? nextBounds.Height / initialBounds.Height : 1.0;

        foreach (var snapshot in _groupResizeSession.Targets)
        {
            var initialTargetBounds = snapshot.InitialBounds;
            var newX = nextBounds.X + ((initialTargetBounds.X - initialBounds.X) * scaleX);
            var newY = nextBounds.Y + ((initialTargetBounds.Y - initialBounds.Y) * scaleY);
            var minSize = Math.Max(0.0, InteractionOptions.ResizeMinSize);
            var newWidth = Math.Max(minSize, initialTargetBounds.Width * scaleX);
            var newHeight = Math.Max(minSize, initialTargetBounds.Height * scaleY);

            SetDesignSize(snapshot.Target, new Size(newWidth, newHeight));
            SetDesignPosition(snapshot.Target, new Point(newX, newY));
        }

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
        if (_groupResizeSession == null)
            return;

        _groupResizeSession = null;
        UpdateSelectionOverlayState();
        e.Handled = true;
    }

    internal void UpdateSelectionTargetFromSource(DesignEditorItem container, Visual? source)
    {
        _containerSelectionTargets.Remove(container);
        var target = ResolveSelectionTarget(container, source);

        if (ReferenceEquals(target, container))
            _selectionTargets.Remove(container);
        else
            _selectionTargets[container] = new List<Control> { target };

        UpdateSelectionOverlayState();
    }

    internal void UpdateSelectionTargetFromPoint(DesignEditorItem container, Point screenPoint, KeyModifiers modifiers)
    {
        if (ShouldUseContainerInteraction(modifiers))
        {
            _selectionTargets.Remove(container);
            _containerSelectionTargets.Add(container);
            UpdateSelectionOverlayState();
            return;
        }

        _containerSelectionTargets.Remove(container);
        var worldPoint = GetWorldPosition(screenPoint);
        var target = ResolveSelectionTargetAtPoint(container, worldPoint);
        var isAdditive = ShouldUseAdditiveSelection(modifiers);
        if (isAdditive && !CanAddNestedTargetToContainer(container))
            return;

        if (ReferenceEquals(target, container))
        {
            if (!isAdditive)
                _selectionTargets.Remove(container);
        }
        else if (isAdditive)
        {
            if (_selectionTargets.TryGetValue(container, out var existingTargets) && existingTargets.Count > 0)
            {
                if (existingTargets.Contains(target))
                {
                    if (existingTargets.Count > 1)
                        existingTargets.Remove(target);
                }
                else
                {
                    existingTargets.Add(target);
                }
            }
            else
            {
                _selectionTargets[container] = new List<Control> { target };
            }
        }
        else
        {
            if (_selectionTargets.TryGetValue(container, out var existingTargets) &&
                existingTargets.Count > 1 &&
                existingTargets.Contains(target))
            {
                // Обычный клик по уже выбранному nested target внутри группы
                // не должен схлопывать multi-selection. Переносим target в начало,
                // чтобы он стал primary selection target.
                var index = existingTargets.IndexOf(target);
                if (index > 0)
                {
                    existingTargets.RemoveAt(index);
                    existingTargets.Insert(0, target);
                }
            }
            else
            {
                _selectionTargets[container] = new List<Control> { target };
            }
        }

        UpdateSelectionOverlayState();
    }

    internal Control ResolveInteractionTarget(DesignEditorItem container)
    {
        if (_containerSelectionTargets.Contains(container) || ShouldUseContainerInteraction(LastInputModifiers))
            return container;

        return ResolveSelectionTarget(container);
    }

    internal void SetLastInputModifiers(KeyModifiers modifiers)
    {
        LastInputModifiers = modifiers;
    }

    internal void BeginMarqueeSelection(Point screenPoint, KeyModifiers modifiers)
    {
        _marqueeSelectionOwner = ShouldUseContainerInteraction(modifiers)
            ? null
            : FindContainerAtWorldPoint(GetWorldPosition(screenPoint));
    }

    internal Point GetDesignPosition(Control control)
    {
        if (control is DesignEditorItem item)
            return item.Location;

        if (TryGetDesignBounds(control, out var bounds))
            return bounds.Position;

        return new Point(DesignLayout.GetDesignX(control), DesignLayout.GetDesignY(control));
    }

    internal void SetDesignPosition(Control control, Point position)
    {
        if (control is DesignEditorItem item)
        {
            item.Location = position;
            return;
        }

        EnsureTracked(control);
        DesignLayout.SetDesignX(control, position.X);
        DesignLayout.SetDesignY(control, position.Y);
    }

    internal Size GetDesignSize(Control control)
    {
        var width = double.IsNaN(control.Width) ? control.Bounds.Width : control.Width;
        var height = double.IsNaN(control.Height) ? control.Bounds.Height : control.Height;
        return new Size(width, height);
    }

    internal void SetDesignSize(Control control, Size size)
    {
        control.Width = size.Width;
        control.Height = size.Height;
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

    private bool TryGetDesignBounds(Control control, out Rect bounds)
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
            if (editor._containerSelectionTargets.Contains(item))
                return item;

            if (editor._selectionTargets.TryGetValue(item, out var explicitTargets) &&
                explicitTargets.Count > 0)
            {
                for (var i = explicitTargets.Count - 1; i >= 0; i--)
                {
                    var explicitTarget = explicitTargets[i];
                    if (IsOwnedByContainer(explicitTarget, item))
                        return explicitTarget;
                }
            }
        }

        return ResolveDefaultSelectionTarget(item);
    }

    private static Control ResolveDefaultSelectionTarget(DesignEditorItem item)
    {
        foreach (var control in EnumerateSelectionCandidates(item))
        {
            if (HasDesignerLayoutMetadata(control))
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

    private Control ResolveSelectionTargetAtPoint(DesignEditorItem item, Point worldPoint)
    {
        var fallback = ResolveSelectionTarget(item);
        Control? bestMatch = null;
        Rect bestBounds = default;
        var bestDepth = -1;

        foreach (var control in EnumerateSelectionCandidates(item))
        {
            if (!HasDesignerLayoutMetadata(control))
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

        return bestMatch ?? fallback;
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

    private static bool HasDesignerLayoutMetadata(Control control)
    {
        return DesignLayout.GetIsTracked(control)
            || !double.IsNaN(DesignLayout.GetX(control))
            || !double.IsNaN(DesignLayout.GetY(control));
    }

    private static void EnsureTracked(Control control)
    {
        if (!DesignLayout.GetIsTracked(control))
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

    private bool IsSelectionOverlayControl(Control control)
    {
        if (_primarySelectionControl != null && ReferenceEquals(_primarySelectionControl, control))
            return true;

        var items = SelectedItems;
        if (items == null || items.Count == 0)
            return false;

        foreach (var item in items)
        {
            var container = ContainerFromItem(item) as DesignEditorItem;
            if (container == null && item is DesignEditorItem directItem)
                container = directItem;

            if (container == null)
                continue;

            foreach (var selectionTarget in ResolveSelectionTargets(container))
            {
                if (ReferenceEquals(selectionTarget, control))
                    return true;
            }
        }

        return false;
    }

    private void CleanupSelectionTargets()
    {
        if (_selectionTargets.Count == 0 && _containerSelectionTargets.Count == 0)
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

        var staleContainers = new List<DesignEditorItem>();
        foreach (var pair in _selectionTargets)
        {
            if (!selectedContainers.Contains(pair.Key) ||
                pair.Value.Count == 0)
            {
                staleContainers.Add(pair.Key);
                continue;
            }

            pair.Value.RemoveAll(control => !IsOwnedByContainer(control, pair.Key));
            if (pair.Value.Count == 0)
                staleContainers.Add(pair.Key);
        }

        foreach (var container in staleContainers)
            _selectionTargets.Remove(container);

        _containerSelectionTargets.RemoveWhere(container => !selectedContainers.Contains(container));
    }

    private IReadOnlyList<Control> ResolveSelectionTargets(DesignEditorItem item)
    {
        if (_containerSelectionTargets.Contains(item))
            return new[] { (Control)item };

        if (_selectionTargets.TryGetValue(item, out var explicitTargets) && explicitTargets.Count > 0)
        {
            explicitTargets.RemoveAll(control => !IsOwnedByContainer(control, item));
            if (explicitTargets.Count > 0)
                return explicitTargets;
        }

        return new[] { ResolveDefaultSelectionTarget(item) };
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

    private DesignEditorItem? FindContainerAtWorldPoint(Point worldPoint)
    {
        if (Presenter?.Panel == null)
            return null;

        DesignEditorItem? bestMatch = null;

        foreach (var child in Presenter.Panel.Children)
        {
            if (child is not DesignEditorItem container || container.Bounds.Width <= 0 || container.Bounds.Height <= 0)
                continue;

            var bounds = new Rect(container.Location, container.Bounds.Size);
            if (!bounds.Contains(worldPoint))
                continue;

            bestMatch = container;
        }

        return bestMatch;
    }

    private DesignEditorItem? FindContainerForMarquee(Rect bounds)
    {
        if (Presenter?.Panel == null)
            return null;

        DesignEditorItem? bestMatch = null;
        var bestArea = 0.0;

        foreach (var child in Presenter.Panel.Children)
        {
            if (child is not DesignEditorItem container || container.Bounds.Width <= 0 || container.Bounds.Height <= 0)
                continue;

            var containerBounds = new Rect(container.Location, container.Bounds.Size);
            var intersection = containerBounds.Intersect(bounds);
            if (intersection.Width <= 0 || intersection.Height <= 0)
                continue;

            var area = intersection.Width * intersection.Height;
            if (area > bestArea)
            {
                bestArea = area;
                bestMatch = container;
            }
        }

        return bestMatch;
    }

    private bool TryCreateGroupResizeSession(ResizeDirection direction, out GroupResizeSession? session)
    {
        session = null;

        if (!TryGetSelectedDesignBounds(out var selectionBounds, out var selectedCount, out _, out _, out _, out _, out _)
            || selectedCount <= 1)
        {
            return false;
        }

        var targets = new List<GroupResizeTargetSnapshot>();
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
                if (!TryGetDesignBounds(target, out var bounds))
                    continue;

                SetDesignSize(target, GetDesignSize(target));

                targets.Add(new GroupResizeTargetSnapshot
                {
                    Container = container,
                    Target = target,
                    InitialBounds = bounds
                });
            }
        }

        if (targets.Count <= 1)
            return false;

        session = new GroupResizeSession
        {
            Direction = direction,
            InitialBounds = selectionBounds,
            Targets = targets
        };

        return true;
    }

    private static Rect CalculateResizedBounds(Rect initialBounds, ResizeDirection direction, Vector delta, double minSize)
    {
        var newX = initialBounds.X;
        var newY = initialBounds.Y;
        var newWidth = initialBounds.Width;
        var newHeight = initialBounds.Height;

        switch (direction)
        {
            case ResizeDirection.Right:
                newWidth += delta.X;
                break;
            case ResizeDirection.Bottom:
                newHeight += delta.Y;
                break;
            case ResizeDirection.Left:
                newWidth -= delta.X;
                newX += delta.X;
                break;
            case ResizeDirection.Top:
                newHeight -= delta.Y;
                newY += delta.Y;
                break;
            case ResizeDirection.BottomRight:
                newWidth += delta.X;
                newHeight += delta.Y;
                break;
            case ResizeDirection.BottomLeft:
                newWidth -= delta.X;
                newX += delta.X;
                newHeight += delta.Y;
                break;
            case ResizeDirection.TopRight:
                newWidth += delta.X;
                newHeight -= delta.Y;
                newY += delta.Y;
                break;
            case ResizeDirection.TopLeft:
                newWidth -= delta.X;
                newX += delta.X;
                newHeight -= delta.Y;
                newY += delta.Y;
                break;
        }

        var initialRight = initialBounds.Right;
        var initialBottom = initialBounds.Bottom;

        newWidth = Math.Max(minSize, newWidth);
        newHeight = Math.Max(minSize, newHeight);

        if (direction is ResizeDirection.Left or ResizeDirection.TopLeft or ResizeDirection.BottomLeft)
            newX = initialRight - newWidth;

        if (direction is ResizeDirection.Top or ResizeDirection.TopLeft or ResizeDirection.TopRight)
            newY = initialBottom - newHeight;

        return new Rect(newX, newY, newWidth, newHeight);
    }
}
