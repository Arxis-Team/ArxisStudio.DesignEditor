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

// Свойства зависимостей, их обёртки и публичные события.
// Часть DesignEditor; общее описание типа — в DesignEditor.cs.
public partial class DesignEditor
{
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
    /// <summary>
    /// Идентификатор свойства пользовательских направляющих.
    /// </summary>
    public static readonly StyledProperty<IEnumerable<DesignGuide>?> GuidesProperty =
        AvaloniaProperty.Register<DesignEditor, IEnumerable<DesignGuide>?>(nameof(Guides));

    /// <summary>
    /// Идентификатор свойства снимка пользовательских направляющих.
    /// </summary>
    internal static readonly DirectProperty<DesignEditor, IReadOnlyList<DesignGuide>> UserGuidesProperty =
        AvaloniaProperty.RegisterDirect<DesignEditor, IReadOnlyList<DesignGuide>>(
            nameof(UserGuides),
            o => o.UserGuides);

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
    private static readonly DirectProperty<DesignEditor, int> SecondarySelectionAdornersCountProperty =
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

    private IReadOnlyList<DesignGuide> _userGuides = Array.Empty<DesignGuide>();

    /// <summary>
    /// Получает или задает пользовательские направляющие.
    /// </summary>
    /// <remarks>
    /// Набором владеет хост: редактор его читает, показывает и притягивает к нему элементы,
    /// но не создаёт и не удаляет записи сам — как и с деревом контролов.
    /// <para>
    /// Коллекция, реализующая <see cref="System.Collections.Specialized.INotifyCollectionChanged"/>,
    /// отслеживается: добавленная направляющая появляется и в отрисовке, и в притяжении
    /// без переприсваивания свойства.
    /// </para>
    /// </remarks>
    public IEnumerable<DesignGuide>? Guides
    {
        get => GetValue(GuidesProperty);
        set => SetValue(GuidesProperty, value);
    }

    /// <summary>
    /// Получает снимок пользовательских направляющих для шаблона.
    /// </summary>
    /// <remarks>
    /// Отдельное свойство нужно по той же причине, что и у выделения: привязка
    /// перевычисляется только при смене идентичности значения, а хост вправе держать
    /// одну и ту же коллекцию и менять её содержимое.
    /// </remarks>
    internal IReadOnlyList<DesignGuide> UserGuides
    {
        get => _userGuides;
        private set => SetAndRaise(UserGuidesProperty, ref _userGuides, value);
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
    private int SecondarySelectionAdornersCount => _secondarySelectionAdornersCount;

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
}
