# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Команды

```bash
dotnet build ArxisStudio.DesignEditor.sln      # сборка решения
dotnet test tests/ArxisStudio.DesignEditor.Tests/ArxisStudio.DesignEditor.Tests.csproj
dotnet run --project samples/DesignEditor.Demo # запуск демо
```

Один тест: `dotnet test --filter "FullyQualifiedName~SelectionTests.Click_On_Nested_Control_Selects_It_As_Nested_Target"`.

Тесты — headless-UI на `Avalonia.Headless.XUnit`. Важно: этот пакет завязан на **xunit v3**, не на v2. Атрибуты `[AvaloniaFact]` / `[AvaloniaTheory]` вместо `[Fact]` / `[Theory]`: они поднимают headless-приложение из `TestAppBuilder` и загоняют тело теста в UI-поток.

`Avalonia.Headless` умеет настоящий ввод — `MouseDown`/`MouseUp`/`MouseWheel`/`KeyPress` на `Window` — и `SetRenderScaling` для проверки DPI-путей. Поэтому клик-поведение выделения проверяется автоматически, без запуска окна.

Тесты писать **на наблюдаемое поведение** (`SelectedDesignTargets`, `PrimarySelectionTarget`, `SelectionBounds`, счётчики), а не на внутренние структуры выбора: `_selectionTargets` будет переписан при переходе к рекурсивной вложенности, а ожидания должны пережить рефакторинг.

`EditorHarness.Create` выключает привязку к сетке (`snapToGrid: false`), хотя в боевой конфигурации она включена. Тесты перетаскивания и контракта изменений проверяют механику, а не привязку, и не должны ломаться от «сдвинули на 60 — получили 70». Сам факт включённости по умолчанию закреплён отдельно в `SnapToGridTests`; там же harness создаётся с `snapToGrid: true`. `NestedContainerTests` выключает её в своём `Create`.

Контейнеры реализуются только после layout-прохода — в тестах его прогоняет `EditorHarness.RunLayout()` через `GetLayoutManager()`. Без него `Bounds` пустые и выделение не резолвится.

Библиотека открывает internals для тестовой сборки через `InternalsVisibleTo` в `src/Properties/AssemblyInfo.cs`.

Демо остаётся для визуальной проверки рендера и тем — этого headless не покрывает. Скиллы в `.claude/skills/`: `run-demo` (запуск демо, скриншот, клик, drag с модификаторами и зум по координатам окна) и `avalonia-api` (сверка API между версиями Avalonia по пакетам NuGet).

В демо три карточки — три семейства раскладки: `Login` на `StackPanel`, `Dashboard` на `AbsolutePanel`, `Grid Form` на `Grid`. Верхняя панель показывает `Layout:` и действующую политику перемещения выбранного target'а — это первое, что стоит посмотреть, если жест не срабатывает.

Шаг сетки в верхней панели меняется через ресурс `DesignEditor.Grid.CellSize`, а не через свойство редактора: так его подхватывает `ControlTheme` сетки, и привязка следует за ним сама — `SnapStep` по умолчанию `NaN`, то есть «брать шаг у сетки». Одна настройка меняет и то, что нарисовано, и то, к чему притягивается; задать их порознь демо намеренно не позволяет.

Верхняя панель демо показывает геометрию **контейнера** (`ActiveItem`), а не выбранного вложенного target. Позицию вложенного контрола видно только внутри карточки Dashboard — там выведены `DesignX / DesignY / X / Y` для `TextBlock1`. Для проверок, где важно точное положение вложенного элемента, тянуть надо именно его.

Сборка должна проходить с **0 предупреждений**. У библиотеки включён `GenerateDocumentationFile`, поэтому любой новый публичный член без XML-doc даёт CS1591.

Если сборка падает с `MSB3021`/`MSB3027` («файл используется другим процессом») на `ArxisStudio.DesignEditor.dll` — это **не ошибка кода**, а блокировка от `Avalonia.Designer.HostApp` (XAML-превьюер Rider). Компиляция при этом проходит; ломается только копирование в выходной каталог демо. Лечится закрытием вкладки превью или `taskkill /PID <pid> /F`. Проверить код в обход блокировки:

```bash
dotnet build src/ArxisStudio.DesignEditor.csproj
```

## Состав решения

| Проект | TFM | Назначение |
|---|---|---|
| `src/ArxisStudio.DesignEditor.csproj` | `net8.0` | библиотека контролов, `RootNamespace = ArxisStudio` |
| `samples/DesignEditor.Demo` | `net10.0` | демо-приложение, `AvaloniaUseCompiledBindingsByDefault = true` |

Avalonia UI **12.1.1**. `net8.0` у библиотеки — это минимальный TFM, поддерживаемый Avalonia 12 (netstandard2.0 больше не поставляется); понижать нельзя.

`.external/` содержит `nodify` и `nodify-avalonia` как справочный материал — они не входят в решение и не собираются.

## Архитектура

### Три системы координат

Это главный источник ошибок в этом коде. Не путать:

1. **Координаты контрола** — `e.GetPosition(editor)`, пиксели viewport'а.
2. **Мировые координаты** — `editor.GetWorldPosition(p) == p / ViewportZoom + ViewportLocation`. В них считаются marquee, group drag delta и hit-testing.
3. **Design-координаты** — `Layout.DesignX` / `Layout.DesignY`, положение относительно поверхности дизайна. В них живут `SelectionBounds`, adorner'ы и результат drag/resize.

`UpdateTransforms()` строит **два** transform group'а: `ViewportTransform` (точный) и `DpiScaledViewportTransform` (со смещением, округлённым до физического пикселя через `TopLevel.GetTopLevel(this)?.RenderScaling`). Второй нужен фону и сетке, чтобы линии не размывались. Менять один без другого нельзя.

Group drag намеренно считается по **накопленной world-space delta** от начальной точки, а не по текущей layout-позиции source target — иначе смещение «плывёт» при ненулевом зуме.

### Двухуровневое выделение

`DesignEditor : SelectingItemsControl`, поэтому штатная модель Avalonia (`Selection`, `SelectedItems`) оперирует **контейнерами** `DesignEditorItem`. Поверх неё редактор ведёт собственный слой выбора вложенных контролов:

- `_selectionTargets: Dictionary<DesignEditorItem, List<Control>>` — nested targets по контейнерам
- `_containerSelectionTargets: HashSet<DesignEditorItem>` — контейнеры, выбранные целиком

Отсюда следует, что **`SelectedItems.Count` ≠ `SelectedDesignTargetsCount`**: один выбранный контейнер может содержать несколько выбранных nested targets. Публичный контракт выбора — `DesignSelectionTarget` (`Container`, `Target`, `Scope`, `DisplayName`); `Scope` различает `Container` и `NestedTarget`.

Два слоя выбора обязаны меняться вместе. `_selectedTargets` держит design target'ы, а `Selection` — индексы item'ов, и оверлей обходит именно `SelectedItems`, спрашивая у каждого его targets. Контейнер, попавший в `Selection` без собственной записи в `_selectedTargets`, подменяется вложенным target'ом по умолчанию — и группа контейнеров молча превращается в смешанную. Именно так и ломался `Ctrl + Shift + Click`: ветка контейнера всегда заменяла выбор, второй контейнер вытеснял первый, а первый возвращался в оверлей уже своим вложенным контролом. Правки в одном слое без другого делать нельзя — см. `SyncContainerItemSelection`.

В режиме `Annotated` nested target'ом может стать только контрол с designer-метаданными — см. `HasDesignerLayoutMetadata` (наличие `Layout.X/Y` или `Layout.IsTracked`); отбор идёт через `IsSelectableTarget`, который знает режим контейнера. Клик по «нетрекаемой» области внутри контейнера сбрасывает nested target и переводит выбор на уровень контейнера; fallback-выбора «первого tracked контрола» больше нет — это осознанное поведение, не баг.

### Контейнер как хост загруженной формы

`DesignEditorItem.ContentMode` различает два происхождения содержимого.

`Annotated` (по умолчанию) — шаблон написан вместе с приложением, и автор сам решает, что редактируется: target'ом становится только контрол с `Layout.IsTracked` или `Layout.X/Y`.

`Loaded` — форма пришла целиком, например из `.axaml`. Размечать её некому, поэтому редактируется вся авторская разметка, а содержимое перестаёт реагировать на ввод.

Два следствия, оба выяснены замером:

- **Обход идёт по авторским связям, а не по визуальному дереву.** `EnumerateAuthoredContent` спускается только по детям панели, ребёнку декоратора и контенту, если это контрол. Проверки `TemplatedParent` **недостаточно**: презентер порождает из строкового контента `AccessText`, у которого `TemplatedParent` пуст, и клик по кнопке выбирал бы её надпись.
- **Ввод гасится на `PART_ContentPresenter`** стилем `^[ContentMode=Loaded]`, а не обходом дерева. Выделение от этого не страдает: попадание считается по прямоугольникам в мировых координатах (`TryResolveSelectionTargetAtPoint`), а у самого контейнера фон `Transparent` на всю площадь, поэтому нажатие достаётся ему. Без гашения выделения не возникает вообще — живая кнопка обрабатывает нажатие сама, и до контейнера оно не доходит.

Стратегии размещения с загруженной формой работают без изменений: корень разметки — обычная панель Avalonia, и редактор сам определяет, двигать ребёнка, переставлять или не трогать.

Вся пересборка состояния выделения проходит через `UpdateSelectionOverlayState()` → `TryGetSelectedDesignBounds(...)`. Это единственная точка, где обновляются `SelectionBounds`, `SecondarySelectionAdorners`, `HasSingle/Multiple*Selection` и `SelectedDesignTargets`.

### Две независимые машины состояний

Обе — стек с `PushState` / `PopState`, и они не связаны друг с другом:

- **Уровень редактора** — `EditorState` (`States/`): `EditorIdleState`, `EditorPanningState`, `EditorSelectingState`. `DesignEditor.OnPointer*` делегирует в `CurrentState`.
- **Уровень контейнера** — `DesignEditorItemState` (`States/States.cs`): `ItemIdleState`, `ItemDraggingState`, `ItemResizingState`, `ItemReorderingState`. У этого класса есть `ReEnter(...)` — вызывается при возврате из вложенного состояния.

Групповые операции вынесены из состояний в `GroupDragOperation` / `GroupResizeOperation` (оба реализуют внутренний `IInteractionOperation`). Правила для nested-группы централизованы в снимке `SelectionInteractionCapabilities` — в частности `CanMoveNestedGroup` и `HasMixedMovePolicies` (смешанная группа locked/unlocked не двигается вообще).

### Слои шаблона

Шаблон `DesignEditor` (`Themes/Styles/DesignEditor.axaml`) разделён на слои. Самый нижний — `PART_Grid` (`DesignGrid`), остальные три — Canvas:

- `PART_ItemsLayer` — содержимое (`PART_ItemsPresenter` + `AbsolutePanel`)
- `PART_SelectionOverlayLayer` — `PART_SelectionAdorner` (primary), `PART_GroupSelectionAdorner`, `PART_SecondarySelectionAdorners` (`SelectionAdornerLayer`)
- `PART_InteractionOverlayLayer` — временные оверлеи на время действия пользователя (marquee и индикатор точки вставки при перестановке; сюда же планируются snap lines и guides)

`DesignGrid` — единственный слой **без** `ViewportTransform`. Он получает `ViewportLocation` и `ViewportZoom` напрямую и рисует в экранных координатах: только так толщина линий остаётся пиксельной на любом масштабе, а уровни детализации могут скрывать мелкую сетку. Не пытаться навесить на него трансформацию — это сломает оба свойства.

`DesignEditorItem` **не рисует** рамку выделения и ручки — всё это editor-level. Не возвращать item-level selection-свойства в контейнер.

`SelectionAdornerLayer` не должен менять visual tree во время `Measure`/`Arrange` — это уже приводило к пропадающим secondary overlays при `Shift + Click` (см. коммит 98c8f0d). Перестроение делать вне layout-прохода.

### Attached-свойства `Layout`

`ArxisStudio.Attached.Layout` двусторонне синхронизирует локальные `X`/`Y` (относительно родителя) и глобальные `DesignX`/`DesignY`:

- `X`/`Y` или `IsTracked` включают подписку на `LayoutUpdated` → пересчёт `DesignX`/`DesignY`
- запись в `DesignX`/`DesignY` идёт обратно в локальные координаты
- рекурсия гасится внутренним `IsUpdatingPosition`
- если контрол ещё не в visual tree, пересчёт откладывается до `AttachedToVisualTree`

Два уточнения, которые важны при отладке:

- `UpdateDesignPosition` **всегда** идёт через `Dispatcher.UIThread.Post(..., DispatcherPriority.Render)`. То есть `DesignX`/`DesignY` не «иногда», а гарантированно отстают на один проход диспетчера от фактического layout. Читать их сразу после установки `X`/`Y` бессмысленно.
- В `OnDesignPositionChanged` из результата `TranslatePoint` вычитается `control.Margin`. `Layout.X`/`Y` — это позиция слота в родительской панели, а `TranslatePoint` возвращает визуальный top-left, уже включающий margin. Без вычитания отступ переприменялся бы на каждом следующем arrange.

### События выделения

`ApplySelectionSnapshot` — единственная точка публикации выделения. Она сравнивает новый снимок с текущим и при совпадении не делает **ничего**: ни присвоения свойств, ни события.

Это не оптимизация, а условие корректности. `UpdateSelectionOverlayState` вызывается из двух десятков мест, включая каждый кадр перетаскивания, и пересобирает снимок всегда. Без проверки `DesignSelectionChanged` срабатывало бы десятки раз за жест, а `SelectedDesignTargets` переприсваивался бы новым списком, заставляя все привязки пересчитываться.

Сравнение идёт по `DesignSelectionTarget.Target`, а не по обёрткам: `CreateSelectionTargetsSnapshot` создаёт их заново при каждом вызове, поэтому сравнение по ссылке на обёртку всегда даёт «изменилось». Порядок значим — первый элемент это primary.

Имя события намеренно не `SelectionChanged`: оно уже занято `SelectingItemsControl` и работает на другом уровне.

### Клавиатура

`OnKeyDown` в `DesignEditor` пропускает уже обработанные нажатия — иначе стрелки и Delete отбирались бы у вложенного редактируемого контрола, если фокус в нём.

Неочевидное: `FocusableProperty.OverrideDefaultValue<DesignEditor>(true)` фокус **не даёт**, он лишь разрешает его получить. Без явного `Focus()` в `OnPointerPressed` клавиатура до редактора не доходит вообще. Вызов обёрнут в проверку `IsKeyboardFocusWithin`, чтобы не отбирать фокус у вложенного контрола.

Нюдж идёт через тот же `BeginEdit`/`CommitEdit`, что и перетаскивание: одно нажатие — одна запись. Фильтр no-op в `DesignEditScope` сам отсекает случай, когда все targets заблокированы политикой.

Delete не удаляет, а поднимает `DeleteRequested`: коллекция принадлежит хосту. Если обработчика нет или он не выставил `Handled`, нажатие остаётся необработанным и всплывает дальше.

### Стратегии размещения

Главное архитектурное решение библиотеки после сетки. `src/Placement/`, namespace `ArxisStudio.Placement`, **всё internal** — форму рано фиксировать публично, тест `Placement_Strategies_Are_Not_Public` это сторожит.

Таблица возможностей снята с реальной Avalonia 12 в `LayoutHonourProbeTests` и определяет всё остальное:

| | явный `Width`/`Height` | `Layout.X`/`Y` |
|---|---|---|
| `AbsolutePanel` | honours | **honours** |
| `Canvas`, `StackPanel`, `Grid`, `DockPanel`, `WrapPanel`, `Border` | honours | игнорирует |

Два следствия, оба неочевидные:

- **Размер honours любая панель, включая `HorizontalAlignment="Stretch"`.** Явный размер применяется до выравнивания. Поэтому resize осмыслен везде, и выводить `ResizePolicy` из раскладки не нужно — в стратегию размер не входит вовсе. Ограничивают размер `Min`/`Max` контрола и границы формы, а не родитель.
- **`Layout.X`/`Y` читает единственное место во всей библиотеке** — `AbsolutePanel.ArrangeOverride`. Даже `Canvas` их игнорирует, ему нужны `Canvas.Left`/`Top`.

`DesignMoveSemantics`: `Reposition` (позицию можно задать), `Reorder` (нельзя, но осмысленна перестановка среди соседей), `None` (раскладка владеет положением полностью). `DesignPlacementResolver.Resolve` — `GetVisualParent()` и switch по типу, без кеша.

`AbsolutePlacementStrategy` обслуживает и контрол **без родителя**: на нём стоят `GroupResizeOperationTests`, которые держат `Border`'ы вне дерева ради чистой арифметики. Семантика для detached обязана совпадать с прежней — иначе падают пять snap/group-тестов.

Композиция политик — одно правило: **`effective = user & layout`**. Раскладка задаёт потолок, политика пользователя сужает, ни одна не расширяет другую. Иначе редактор снова начал бы предлагать жест, который ничего не делает. Все интеракционные точки спрашивают `GetEffectiveMovePolicy`; сырые `GetMovePolicy`/`GetResizePolicy` остались для контракта attached-свойств.

Отсечка стоит на **шве**: `SetDesignPosition` отбрасывает target, позицией которого владеет раскладка. Это единственная точка записи, значит только там можно гарантировать, что в контракт изменений не попадёт перемещение, которого не произошло. До этого drag контрола в `StackPanel` не двигал ничего и при этом писал `Move` в стек отмены.

### Перестановка среди соседей

Там, где `MoveSemantics` равна `Reorder`, перетаскивание меняет **порядок детей** — единственное, что вообще меняет их положение в потоковой раскладке. Состояние `ItemReorderingState`, выбор состояния делает `ItemIdleState`.

Перестановка применяется **на отпускании**, а не покадрово: если двигать ребёнка вживую, панель переливается под курсором и элемент прыгает. Во время протяжки рисуется индикатор точки вставки в `PART_InteractionOverlayLayer` — слой ровно под это и заводился.

Толщина индикатора **нулевая в мировых координатах**, видимую задаёт шаблон через `MinWidth`/`MinHeight` внутри обратного трансформа. Так линия остаётся одинаково тонкой на любом масштабе.

Ограничение, о котором надо помнить: библиотека правит живое визуальное дерево, порождённое `DataTemplate`. При включённом переиспользовании контейнеров порядок потеряется — хост обязан персистить его по `EditCompleted`, как уже делает для геометрии.

### Дельта resize отсчитывается от ручки

Самая неочевидная вещь в этом коде, и она уже один раз стоила прыгающей рамки.

`Thumb.DragDelta` в Avalonia отдаёт смещение **относительно самой ручки**, а не от точки нажатия. Замер в `ThumbDeltaProbeTests`:

| | дельты за три шага по 10 |
|---|---|
| ручка неподвижна | 10, 20, 30 |
| ручка едет следом | 10, 10, 10 |

Ручка стоит на краю выделения, то есть на **применённой** геометрии. Значит всё, что съели привязка к сетке, `Max` или границы формы, вернётся в следующей дельте повторно.

Отсюда правило: **дельты resize накапливать нельзя.** `ItemResizingState` и `GroupResizeOperation` считают от уже применённой геометрии и прибавляют пришедшую дельту, а применённое запоминают у себя — читать его обратно через `GetDesignPosition` нельзя, оно отстаёт на layout-проход. Раньше оба накапливали дельты от исходного размера: пока ничто не ограничивало resize, это совпадало, но вместе с привязкой и ограничением по форме остаток начал складываться сам с собой, и ширина шла `110, 130, 110, 130`.

Неподвижный край тоже берётся из текущей геометрии, а не из исходной: тогда он остаётся на месте весь жест, даже когда размер во что-нибудь упёрся.

Масштаб группы при этом считается от **исходной** рамки, а не от текущей — иначе округления копились бы от кадра к кадру и группа расползалась.

### Ограничение размера контейнером

`InteractionOptions.IsResizeContainedToParent`, включено по умолчанию. Границей выбран **владеющий `DesignEditorItem`**, а не прямой родитель: панель, которая растёт по содержимому, границей быть не может — ограничивать ребёнка высотой, которую он же задаёт, это круг. У контейнера верхнего уровня владельца нет, и он не ограничен.

Три правила:

- ограничивается **только та ось, которую тянут** — иначе перетаскивание одного края задним числом ужимало бы уже вылезший контрол по другой;
- ограничение применяется **до** `Min`/`Max`, чтобы минимум побеждал и контрол не схлопывался у края формы;
- **у группы ограничивается рамка целиком**, как и при привязке к сетке.

`CoerceDesignSize` читает `MinWidth`/`MaxWidth`/`MinHeight`/`MaxHeight`, которых редактор раньше не видел вовсе: он писал 429 там, где раскладка выдавала 60, и дальше считал от несуществующей величины. Порядок как в Avalonia — сначала максимум, потом минимум, поэтому при конфликте побеждает минимум.

### Привязка к сетке

Включена по умолчанию — иначе сетка остаётся декорацией. Точка входа одна: `ShouldSnap` / `ResolveSnapStep` / `SnapCoordinate` / `SnapPosition` в `DesignEditor`.

Шаг берётся у `PART_Grid.CellSize`, если `InteractionOptions.SnapStep` равен `NaN` (значение по умолчанию). Это не «удобство», а страховка: иначе редактор мог бы рисовать одну структуру, а привязывать к другой. Явный шаг перекрывает сетку. Нет шаблона — нет сетки — `ResolveSnapStep()` возвращает 0 и `ShouldSnap` даёт `false`; поэтому `GroupResizeOperationTests` с голым `new DesignEditor()` продолжают проверять чистую арифметику.

Три правила, каждое закрыто тестом:

- **Привязывается результат, а не дельта.** `SnapPosition(_elementStartLocation + appliedTotalDelta, ...)`. Округление дельты сохранило бы исходный сдвиг элемента относительно сетки, и на узел он бы никогда не встал.
- **При resize привязывается двигающийся край**, а не размер, и **до** клампа по минимуму. Порядок важен: сначала край на узел, потом ограничение размера — иначе кламп сбивал бы уже привязанное значение.
- **У группы привязывается рамка целиком**, а не каждый target. `GroupResizeOperation.SnapBounds` работает с `nextBounds` до вычисления `scaleX`/`scaleY`; привязка каждого target'а по отдельности стянула бы соседей к общим узлам.

Групповое перетаскивание получает уже привязанное смещение источника (`effectiveDelta = snapped - _elementStartLocation`), поэтому взаимное расположение внутри группы сохраняется.

`SnapCoordinate` считает через `Math.Floor(value / step + 0.5) * step`, а **не** `Math.Round`. `Math.Round` округляет к чётному, поэтому ровно посередине между узлами направление зависело бы от чётности узла (310 → 320, но 290 → 280) и край дёргался бы назад при медленной протяжке.

Resize читает модификатор из `editor.LastInputModifiers`: в `ResizeDeltaEventArgs` модификаторов нет.

Нюдж стрелками привязку **не** использует сознательно: она исправляет неточный ввод указателем, а клавиатура задаёт смещение точно. Иначе `NudgeStep` меньше шага сетки перестал бы работать вовсе.

Design-координаты вложенного контрола считаются относительно `DesignSurface` (см. `TryGetDesignBounds`), то есть совпадают с мировыми. Поэтому вложенный ребёнок садится на ту же глобальную сетку, которую видно на фоне, даже если его контейнер стоит мимо узлов — конвертация координат для привязки не нужна.

### Контракт изменений

Вся геометрия пишется ровно через два метода — `SetDesignPosition` и `SetDesignSize`. Это единственный шов, и держать его единственным важно: на нём стоит запись изменений для undo. Прямые присваивания `Location`/`Width`/`Height` остались только в fallback-ветках состояний, где редактора нет вообще.

`DesignEditScope` копит изменения в пределах жеста. Он записывает **задаваемые значения**, а не перечитывает состояние: иначе итог зависел бы от того, успел ли пройти layout к моменту фиксации. Исходное состояние снимается один раз, при первом обращении к target.

Границы жестов — четыре пары в `DesignEditor`: `OnItemsDragStarted`/`OnItemsDragCompleted` и три пары resize (primary, secondary, group). `BeginEdit` вызывается **после** всех guard-проверок, но **до** первой мутации: `ItemResizingState.Enter` и `TryCreateGroupResizeOperation` фиксируют текущий размер, и это должно попасть в «до», а не потеряться.

`ApplyGeometry`, `ApplyOrder`, `Revert` и `Reapply` подавляют запись через `_suppressEditRecording` — иначе отмена порождала бы новую запись и стек никогда не пустел бы.

Порядок перекрытия идёт тем же путём: `SetDesignZIndex` — шов рядом с `SetDesignPosition`/`SetDesignSize`. Четвёртый и последний — `SetDesignChildIndex`, позиция среди детей панели; он отличается от `SetDesignZIndex` уровнем: тот меняет перекрытие, этот — порядок в коллекции, и в потоковой раскладке положение меняет только второе. Изменения наследуют общий `DesignChange`, чтобы хост не разбирал типы.

Важное про тему: в `DesignEditorItem.axaml` **нельзя** возвращать `ZIndex` в стиль `:selected`. Раньше выбранный элемент поднимался на 99 ради своей рамки, но рамки давно на уровне редактора, а такой подъём молча перебивает порядок, заданный `BringToFront`/`SendToBack`. На это есть тест.

### Политики редактирования

`ArxisStudio.Attached.DesignInteraction` — `ResizePolicy` (флаги сторон) и `MovePolicy` (оси). Проверяются в `ApplyMovePolicy` / `IsResizeAllowed` и применяются одинаково к одиночному target, nested-группе и групповым операциям. Полностью заблокированный target (`None`/`None`) рисуется locked-визуалом, ручки становятся неинтерактивными.

### Конфигурация ввода

Жесты и числовые параметры не захардкожены, а вынесены в объекты-конфиги, доступные из AXAML/стилей/биндингов:

- `DesignEditorInputGestures` — `PanButton/PanModifiers`, `MarqueeButton/MarqueeModifiers`, `ZoomModifiers`, `ContainerInteractionModifiers`, `AdditiveSelectionModifiers`, `LargeNudgeModifiers`, `SnapBypassModifiers`
- `DesignEditorInteractionOptions` — `ZoomStep`, `DragStartThreshold`, `ResizeMinSize`, `NudgeStep`, `LargeNudgeStep`, `IsSnapToGridEnabled`, `SnapStep`, `IsResizeContainedToParent`

Решения о жестах принимать через `ShouldStartPan` / `ShouldStartMarquee` / `ShouldHandleZoom` / `ShouldUseContainerInteraction` / `ShouldUseAdditiveSelection` / `ShouldDeferPressToMarquee`, а не сравнением `KeyModifiers` на месте. Политику интерпретирует редактор — состояния контейнера её не читают, а спрашивают.

Область действия рамки (`MarqueeScope`) вычисляется из **текущего прямоугольника** на каждом шаге протяжки одним правилом `FindContainerForMarquee`, и то же правило применяется в `CommitSelection`. Ничего не латчится: латченный владелец приводил к тому, что рамка навсегда оставалась привязанной к контейнеру, где было нажатие, а её визуальный охват обещал выборку, которой не происходило. Латчится только **режим** (container-level), и делает это `EditorSelectingState` — жест не должен менять смысл посреди протяжки.

`ShouldDeferPressToMarquee` разрешает конфликт жестов на пустой области контейнера. Механика передачи важна: контейнер, уступая жест, **не захватывает указатель и не ставит `Handled`**, поэтому нажатие всплывает до редактора обычным маршрутом и `EditorIdleState` запускает рамку по `!e.Handled`. Никакого проталкивания состояний между двумя машинами — они остаются независимыми.

`ContainerInteractionModifiers` и `AdditiveSelectionModifiers` продублированы плоскими свойствами на `DesignEditor` ради совместимости — при правке менять обе стороны, они синхронизируются вручную в сеттерах.

### Темы

Единая точка входа — `Themes/ArxisStudioDesignEditorTheme.axaml`; приложение подключает только её. Внутри: `Themes/Resources/DesignEditorResources.axaml` (lightweight-ресурсы) и `Themes/Styles/*.axaml` (`ControlTheme` контролов).

Кисти определяются через `ThemeDictionaries`, поэтому Light/Dark различаются без дублирования `ControlTheme`. `SelectionAdorner` использует ресурсные ключи по ролям и состояниям (`DesignEditor.SelectionAdorner.Primary*` / `Secondary*` / `Group*` / `Handle*` / `Locked*`), а роль задаётся свойством `SelectionAdorner.Role`. Кастомизация внешнего вида должна идти через ресурсы, а не копированием шаблонов.

## Публичная поверхность закреплена

`PublicSurfaceTests` сверяет `GetExportedTypes()` со списком из 40 типов. Новый публичный тип роняет тест — его нужно либо внести в список осознанно, либо сделать `internal`. Отдельные тесты запрещают утечку машин состояний и деталей overlay.

Машины состояний (`ArxisStudio.States.*`), стратегии размещения (`ArxisStudio.Placement.*`), `SelectionAdornerLayer`, `SelectionAdornerInfo`, `DesignSurface` и свойства `SecondarySelectionAdorners` — **internal**. Вместе с ними internal стали `CurrentState`, `PushState`, `PopState` на обоих контролах.

Это работает потому, что AXAML библиотеки компилируется в ту же сборку: internal-типы в шаблонах и `TemplateBinding` к internal-свойствам разрешаются нормально. Прецедент был давно — `DesignSurface` в `ItemsPanelTemplate`.

Компилятор Avalonia добавляет публичные `CompiledAvaloniaXaml.*`; тест их отфильтровывает — они не наши.

## Хрупкие места

Не ломает компиляцию — падает молча, в рантайме.

**`UnscaleTransformConverter` жёстко берёт `transformGroup.Children[0]`** и инвертирует именно его. Работает только потому, что и конструктор `DesignEditor`, и `UpdateTransforms()` кладут `ScaleTransform` первым, а `TranslateTransform` вторым. Поменяется порядок — оверлеи поедут без единой ошибки.

**Адорнеры позиционируются двумя независимыми механизмами.** Primary/group — через `Canvas.Left/Top` + `MultiBinding` со `ScaleDoubleConverter` + обратный `RenderTransform` в шаблоне. Secondary — через `SelectionAdornerLayer` с собственными `MeasureOverride`/`ArrangeOverride` и своим inverse-`ScaleTransform` на каждого ребёнка. Правку геометрии выделения нужно вносить в оба места.

## Выглядит ошибкой, но верно

Два места, которые уже один раз были ошибочно опознаны как дефекты. Прежде чем «чинить» — прочитать здесь.

**`SelectionAdornerLayer.MeasureOverride` смешивает единицы**: позиции мировые, размеры умножены на зум. Это конвенция слоя. Каждому ребёнку `UpdateChildTransforms` вешает обратный масштаб `1/zoom`, чтобы ручки не росли при приближении, а родительский Canvas масштабирует слой целиком: один зум гасится обратным трансформом, второй даёт итоговый размер. Уберёшь умножение — ручки поедут. Сам экстент ни на что не влияет: слой не обрезает содержимое.

**`Layout.UpdateDesignPosition` пересчитывает `DesignX`/`DesignY` из фактического положения и «затирает» заданное.** Так и должно быть: где позицией распоряжается панель, design-координата обязана показывать, где контрол на самом деле. Драки за координату нет — стратегия размещения отсекает запись на шве. Флаг `IsUpdatingPosition` при этом настоящий, но работает **ниже** dispatcher-поста: в отложенном замыкании он не может быть истинным никогда.

## Известные дефекты

- **`OnPointerWheelChanged` ставит `e.Handled = true` безусловно** — даже когда `ShouldHandleZoom` вернул `false` (заданы `ZoomModifiers`, но не нажаты). Колесо не доходит до внешнего `ScrollViewer`.

## Состояние и что дальше

Тестов — 203, все зелёные; сборка с 0 предупреждений. Метод работы, который себя оправдал и которого стоит держаться: **сначала характеризующий тест или замер, потом реализация**, и обязательная проверка, что правка нагружена — временно откатить её и убедиться, что падают именно нужные тесты. Он несколько раз менял постановку задачи: вложенные контейнеры не давали выделения вовсе (а не «не тот scope»), проблема рамки была в латченном владельце (а не в обрезке), событий выделения было 22 за жест, а виртуализация оказалась не нужна вообще — 1.28 мс на кадр при 500 элементах.

Сделано по дорожной карте: сетка, контракт изменений под undo/redo, клавиатура, события выделения, z-order, фиксация публичной поверхности, привязка к сетке, осведомлённость о раскладке (стратегии размещения, ограничение по контейнеру, перестановка среди соседей).

Осталось:

- **Snap lines / smart guides** — направляющие по краям и центрам соседей во время перетаскивания. `PART_InteractionOverlayLayer` под них и задумывался, но пока держит только marquee.
- **Пользовательские направляющие** — линейки и вытягиваемые guides, к которым тоже идёт привязка.

## Соглашения

- `.editorconfig`: 4 пробела, CRLF, Allman-скобки, `System.*` в using'ах первыми.
- XML-doc и комментарии — на русском; идентификаторы — на английском.
- Коммиты — Conventional Commits на английском (`fix(selection): ...`, `chore(deps): ...`).
- Работа идёт напрямую в `main`.

## Заметки по Avalonia 12

Проект недавно мигрировал с 11.3.9 (коммит c1823d5). При переносе кода из 11.x-примеров учитывать:

- `GetVisualRoot()` удалён вместе с публичным `IRenderRoot`; `RenderScaling` берётся с `TopLevel.GetTopLevel(this)`
- `Visual.VisualRoot` — `protected`, снаружи использовать `IsAttachedToVisualTree()`
- `VisualTreeAttachmentEventArgs.Root` устарел → `RootVisual`
- `TextBox.Watermark` → `PlaceholderText`
- `BindingPlugins` стал internal; валидация DataAnnotations теперь opt-in через `AppBuilder.WithDataAnnotationsValidation()`
- `Avalonia.Diagnostics` (DevTools) для 12.x не выпущен. Одноимённые пакеты 12.x в NuGet (`AvaDiagnostics12`, `AvaDevTools`, `ProDiagnostics`) — не от команды Avalonia, не подключать.
