# ArxisStudio.DesignEditor

`ArxisStudio.DesignEditor` — это библиотека для Avalonia UI, предназначенная для построения визуальных редакторов, form designer'ов, layout editor'ов и других IDE-подобных инструментов.

Библиотека предоставляет:

- бесконечную поверхность с панорамированием и зумом
- прямоугольное и множественное выделение
- контейнеры элементов с drag-and-drop и resize
- editor-level overlay-слои для рамок выделения, marquee и selection handles
- систему attached-свойств для позиционирования
- DPI-aware трансформации для фона, сетки и оверлеев
- демо-приложение с типовым сценарием интеграции

## Структура решения

- `src/` — библиотека контролов
- `samples/DesignEditor.Demo/` — демонстрационное Avalonia-приложение
- `ArxisStudio.DesignEditor.sln` — solution

## Основные компоненты

### `DesignEditor`

Главный контрол редактора. Наследуется от `SelectingItemsControl` и отвечает за:

- позицию viewport через `ViewportLocation`
- масштаб через `ViewportZoom`, `MinZoom`, `MaxZoom`
- выделение через `Selection` и `SelectedItems`
- overlay-систему редактора поверх содержимого
- состояния взаимодействия: idle, selecting, panning
- групповое перемещение выбранных элементов
- навигацию viewport через `CenterOn(...)` и `CenterOnItem(...)`
- вписывание области или элемента через `FitToView(...)`

Текущий template `DesignEditor` уже разделен на слои:

- `ItemsLayer` — реальное содержимое редактора и `DesignEditorItem`
- `SelectionOverlayLayer` — `SelectionAdorner`, secondary outlines, group outline и selection handles
- `InteractionOverlayLayer` — временные interaction overlays, которые живут только во время действия пользователя

`PART_InteractionOverlayLayer` предназначен не для постоянного editor chrome, а для временной визуализации процессов:

- marquee selection rectangle
- snap lines и alignment guides
- drag / resize preview
- insertion markers
- hover preview и временные измерительные подсказки

Сейчас на нем уже живет прямоугольник marquee-selection. В дальнейшем этот слой будет точкой расширения для guides, snapping и preview overlays.

### `DesignEditorInputGestures`

Объект конфигурации input gestures редактора. Позволяет настраивать горячие клавиши и модификаторы взаимодействия:

- из AXAML
- через `Style`
- через code-behind
- через binding / MVVM

Сейчас в нем уже доступен:

- `PanButton` / `PanModifiers` — кнопка мыши и модификаторы для старта панорамирования
- `MarqueeButton` / `MarqueeModifiers` — кнопка мыши и модификаторы для старта marquee-selection по пустой области
- `ZoomModifiers` — модификаторы для wheel-zoom
- `ContainerInteractionModifiers` — модификаторы, которые принудительно переключают selection, drag и resize на уровень `DesignEditorItem`
- `AdditiveSelectionModifiers` — модификаторы, которые включают additive selection

### `DesignEditorInteractionOptions`

Объект runtime-параметров взаимодействия редактора, которые не относятся к gesture policy:

- `ZoomStep` — шаг wheel-zoom
- `DragStartThreshold` — порог старта drag в пикселях
- `ResizeMinSize` — минимальный размер при resize

### `DesignSelectionTarget`

Публичный контракт выбранного target в редакторе:

- `Container` — `DesignEditorItem`, которому принадлежит target
- `Target` — фактически выбранный `Control`
- `Scope` — уровень выбора (`Container` или `NestedTarget`)
- `DisplayName` — диагностическое имя target для UI/логов

`DesignEditor` предоставляет:

- `PrimarySelectionTarget` — текущий primary target
- `SelectedDesignTargets` — снимок всех выбранных targets
- `SelectedDesignTargetsCount` — количество выбранных targets

### `DesignInteraction`

`ArxisStudio.Attached.DesignInteraction` предоставляет attached-политики редактирования для designer targets:

- `DesignInteraction.ResizePolicy` — какие стороны/направления разрешены для resize (`None`, `Left`, `Top`, `Right`, `Bottom`, `Horizontal`, `Vertical`, `All`)
- `DesignInteraction.MovePolicy` — по каким осям разрешено перемещение (`None`, `X`, `Y`, `Both`)

Политики применяются как к одиночному target, так и к group interaction:

- если направление запрещено `ResizePolicy`, соответствующие handles неактивны и resize не выполняется
- если `MovePolicy` ограничивает оси, drag сохраняет только разрешенные компоненты delta
- если `MovePolicy = None`, target не перемещается

Что это дает в конструкторе программ:

- блокировку resize/drag для системных или layout-driven контролов без кастомной логики в каждом шаблоне
- ограничение перемещения по одной оси (`X` или `Y`) для splitters, линий, панелей и других специализированных элементов
- ограничение resize по сторонам (`Horizontal`, `Vertical` или отдельные края) для предсказуемого form-designer UX
- единый контракт ограничений для одиночного выбора, nested multi-selection и групповых операций
- снижение риска случайного редактирования критичных узлов в сложных формах

### `DesignEditorItem`

Контейнер элемента редактора, который создается автоматически для каждого item'а. Добавляет:

- состояние выделения
- перетаскивание
- привязку позиции через `Location`
- визуальные состояния `:selected`, `:dragging`, `:resizing`

Начиная с текущей версии `DesignEditorItem` больше не рисует selection frame и resize handles внутри собственного шаблона. Эти editor overlays вынесены на уровень `DesignEditor`.

### `Layout`

`ArxisStudio.Attached.Layout` предоставляет attached-свойства позиционирования:

- `Layout.X` / `Layout.Y` — локальные координаты относительно непосредственного родителя
- `Layout.DesignX` / `Layout.DesignY` — глобальные координаты относительно поверхности дизайна
- `Layout.IsTracked` — принудительное постоянное отслеживание позиции

### `AbsolutePanel`

Панель компоновки, используемая поверхностью редактора. Поддерживает:

- абсолютное позиционирование через `Layout.X` / `Layout.Y`
- fallback на `HorizontalAlignment` / `VerticalAlignment`, если координаты не заданы
- вычисление `Extent` для всех дочерних элементов

## Быстрый старт

### 1. Подключите библиотеку

Добавьте `ProjectReference` или `PackageReference` на `ArxisStudio.DesignEditor`.

### 2. Подключите темы контролов

В ресурсах приложения:

```xml
<Application.Resources>
    <ResourceDictionary>
        <ResourceDictionary.MergedDictionaries>
            <ResourceInclude Source="avares://ArxisStudio.DesignEditor/Themes/Styles/DesignEditor.axaml" />
            <ResourceInclude Source="avares://ArxisStudio.DesignEditor/Themes/Styles/DesignEditorItem.axaml" />
            <ResourceInclude Source="avares://ArxisStudio.DesignEditor/Themes/Styles/SelectionAdorner.axaml" />
        </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
</Application.Resources>
```

### 3. Привяжите редактор к вашей коллекции элементов

```xml
<design:DesignEditor ItemsSource="{Binding Nodes}"
                     SelectedItems="{Binding SelectedNodes}"
                     SelectionMode="Multiple"
                     ViewportZoom="{Binding Zoom, Mode=TwoWay}">
    <design:DesignEditor.InputGestures>
        <design:DesignEditorInputGestures PanButton="Middle"
                                          PanModifiers="None"
                                          MarqueeButton="Left"
                                          MarqueeModifiers="None"
                                          ZoomModifiers="None"
                                          ContainerInteractionModifiers="Control"
                                          AdditiveSelectionModifiers="Shift" />
    </design:DesignEditor.InputGestures>
    <design:DesignEditor.InteractionOptions>
        <design:DesignEditorInteractionOptions ZoomStep="1.1"
                                              DragStartThreshold="3"
                                              ResizeMinSize="10" />
    </design:DesignEditor.InteractionOptions>

    <design:DesignEditor.Styles>
        <Style Selector="design|DesignEditorItem">
            <Setter Property="Location" Value="{Binding Location, Mode=TwoWay}" />
            <Setter Property="Width" Value="{Binding Width, Mode=TwoWay}" />
            <Setter Property="Height" Value="{Binding Height, Mode=TwoWay}" />
            <Setter Property="HorizontalAlignment" Value="Left" />
            <Setter Property="VerticalAlignment" Value="Top" />
        </Style>
    </design:DesignEditor.Styles>
</design:DesignEditor>
```

### 4. Используйте простую ViewModel элемента

```csharp
public class DesignNodeViewModel
{
    public Point Location { get; set; }
    public double Width { get; set; } = 240;
    public double Height { get; set; } = 160;
}
```

## Модель взаимодействия

По умолчанию редактор поддерживает:

- зум колесиком мыши
- панорамирование средней кнопкой мыши
- прямоугольное выделение левой кнопкой по пустому месту
- выделение кликом по элементу
- множественное выделение через модель выбора Avalonia
- drag выбранных элементов
- resize через `SelectionAdorner`, расположенный на `SelectionOverlayLayer`

Дополнительно редактор поддерживает переключение на уровень контейнера через `InputGestures.ContainerInteractionModifiers`:

- обычный клик работает с nested design target
- клик с `ContainerInteractionModifiers` выбирает `DesignEditorItem`
- drag с теми же модификаторами перемещает весь контейнер целиком

Additive selection управляется отдельно через `InputGestures.AdditiveSelectionModifiers`:

- `Ctrl + Click` — exclusive container selection
- `Ctrl + Shift + Click` — additive container selection
- `Ctrl + Shift + marquee` — additive групповое выделение контейнеров
- `Shift + Click` по уже выбранному nested control в группе снимает его из группы selection targets
- `Shift + Click` по другому nested control добавляет его в группу выделения
- `Shift + Click` по nested control из другого `DesignEditorItem` не объединяет группы между контейнерами и ничего не меняет в текущем owner
- обычный `Click` по уже выбранному nested control в группе не схлопывает группу и делает этот control primary target
- обычный `Click` по nested control вне текущей группы выполняет exclusive selection этого target внутри текущего owner
- drag/resize выбранных targets учитывают `DesignInteraction.MovePolicy` и `DesignInteraction.ResizePolicy`

Обычное marquee-selection без `Ctrl` работает в пределах одного owner `DesignEditorItem`:

- nested controls выбираются только внутри одного UI-контейнера
- controls из других `DesignEditorItem` в группу не попадают
- это защищает от случайного group edit между разными документными узлами

## Навигация по viewport

`DesignEditor` теперь предоставляет базовый API для центрирования viewport:

- `CenterOn(Point worldPoint)` — центрирует видимую область на указанной мировой точке
- `CenterOnItem(DesignEditorItem item)` — центрирует видимую область на конкретном элементе
- `CenterOnSelection()` — центрирует видимую область на общей области текущего выделения
- `FitToView(Rect bounds)` — подбирает масштаб и позицию viewport так, чтобы область целиком поместилась в окне
- `FitToView(DesignEditorItem item)` — вписывает конкретный элемент в видимую область

Пример:

```csharp
if (editor.ContainerFromItem(viewModel.ActiveItem) is DesignEditorItem container)
{
    editor.CenterOnItem(container);
    editor.FitToView(container);
}
```

Оба метода:

- не меняют `ViewportZoom`
- изменяют только `ViewportLocation`
- подходят для навигации к активному элементу, выделению или заданной координате

Методы `FitToView(...)`:

- изменяют и `ViewportLocation`, и `ViewportZoom`
- ограничивают масштаб значениями `MinZoom` и `MaxZoom`
- добавляют внутренний padding вокруг целевой области

Навигационные методы `CenterOnItem(...)` и `FitToView(DesignEditorItem)` теперь используют не только `DesignEditorItem.Location`, но и геометрию реального visual target через `Layout`, если внутри контейнера присутствует контрол с designer-метаданными.

Начиная с текущего этапа drag и resize также применяются к выбранному nested design target, если он найден в visual tree элемента и имеет designer-метаданные `Layout`.

При multi-selection редактор использует профессиональную схему overlay:

- если выбрано несколько nested controls внутри одного `DesignEditorItem`, над каждым selected target рисуется собственный интерактивный `SelectionAdorner` с ручками
- resize через ручки влияет только на тот nested control, на котором начато действие
- drag любого selected nested control перемещает всю группу без дополнительных модификаторов и сохраняет относительные расстояния между target'ами
- group drag для nested controls рассчитывается по world-space delta и не зависит от промежуточного layout source target, поэтому остается стабильным при любом `ViewportZoom`
- общий group `SelectionAdorner` в таком сценарии не показывается
- если выбрано несколько `DesignEditorItem`, редактор использует один общий group `SelectionAdorner` для манипуляции контейнерами на поверхности редактора

## Пример использования `Layout`

Для вложенного контента внутри шаблона элемента можно использовать `Layout` напрямую:

```xml
<controls:AbsolutePanel>
    <TextBlock attached:Layout.X="200"
               attached:Layout.Y="100"
               attached:Layout.IsTracked="True"
               Text="Dashboard" />
</controls:AbsolutePanel>
```

Это удобно, когда внутреннему содержимому шаблона нужны designer-координаты и редактор должен уметь строить overlay над вложенным контролом, а не только над `DesignEditorItem`.

`Layout.DesignX` / `Layout.DesignY` поддерживаются автоматически и дают геометрию элемента в координатах `DesignEditor`.

Пример ограничения редактирования nested target:

```xml
<TextBlock attached:Layout.X="200"
           attached:Layout.Y="100"
           attached:Layout.IsTracked="True"
           attached:DesignInteraction.MovePolicy="X"
           attached:DesignInteraction.ResizePolicy="Horizontal"
           Text="Dashboard" />
```

## Что уже сделано

- `DesignEditor` переведен на layered-архитектуру с `ItemsLayer`, `SelectionOverlayLayer` и `InteractionOverlayLayer`
- рамки одиночного и группового выделения вынесены из `DesignEditorItem` на уровень редактора
- `ResizeAdorner` заменен на более общий `SelectionAdorner`
- `SelectionAdorner` используется для primary selection, group selection и secondary outlines
- `SelectionBounds` считаются по editor-space геометрии выбранного visual target через `Layout`
- editor-level hit-testing вложенных контролов работает по `Layout`-геометрии и не зависит от runtime `IsHitTestVisible`
- nested design target выбирается внутри visual tree `DataTemplate`/`UserControl`, а не только на уровне контейнера
- drag и resize переводятся на выбранный designer target, а `DesignEditorItem` остается host-контейнером и fallback
- реализован group drag для multi-selection nested targets с zoom-stable смещением
- group drag для nested controls переведен на accumulated world-space delta вместо чтения текущей layout-позиции source target
- для multi-selection nested controls используется form-designer UX:
- у каждого selected target свой интерактивный `SelectionAdorner`
- group resize для nested controls отключен
- общий group adorner сохранен только для multi-selection `DesignEditorItem`
- обычное marquee-selection ограничено одним owner `DesignEditorItem`
- input-policy вынесен в публичный API `DesignEditorInputGestures`
- runtime numeric policy вынесен в отдельный API `DesignEditorInteractionOptions`
- selection target API вынесен в явный публичный контракт `DesignSelectionTarget`
- editing policy API вынесен в attached-контракт `DesignInteraction.ResizePolicy` / `DesignInteraction.MovePolicy`
- контейнерный режим взаимодействия настраивается через `InputGestures.ContainerInteractionModifiers`
- additive selection настраивается через `InputGestures.AdditiveSelectionModifiers`
- cross-container additive nested selection работает как `no-op` (owner не меняется)
- `CenterOnItem(...)` и `FitToView(DesignEditorItem)` используют геометрию реального контрола, если он помечен designer-данными
- демо обновлено и показывает `Center`, `Fit`, `Center Sel`, `Fit Sel`, а также текущий `Target`

## Roadmap

Следующий этап развития редактора:

1. Расширить selection API событиями и командами высокого уровня.
Ввести явные события изменения primary target и набора selected targets, чтобы интеграции не зависели от внутренних overlay-обновлений.

2. Довести nested multi-selection interaction.
Нужно унифицировать поведение primary target, ограничения resize и visual feedback для группы nested controls без возврата к графическому UX.

3. Развить `PART_InteractionOverlayLayer`.
Следующие кандидаты:
- snap lines
- alignment guides
- drag / resize preview
- hover outline
- distance / spacing overlays

4. Ввести editing policies для разных типов контролов.
Особенно для:
- layout-driven controls
- controls без явного size metadata
- контейнеров, которые должны вести себя как host, а не как свободно ресайзимый target

5. Очистить публичную поверхность библиотеки.
Скрыть internal state machine и overlay implementation details, оставив стабильный API редактора, viewport navigation, gestures и design target interaction.

6. Подготовить интеграцию с `ArxisStudio.Markup`.
Подключить `$design`-метаданные как источник designer-only координат и editor flags, не меняя core-архитектуру `DesignEditor`.

7. Расширить `DesignEditorInteractionOptions` и определить финальный публичный контракт runtime-настроек.
Решение на следующий этап:
- оставить только options-объект
- или добавить плоские свойства-алиасы на `DesignEditor`

## Запуск демо

Из корня репозитория:

```bash
dotnet run --project samples/DesignEditor.Demo
```

В демо-приложении добавлена кнопка `Center`, которая использует `CenterOnItem(...)` для активного элемента.
Также добавлена кнопка `Fit`, которая использует `FitToView(...)` для активного элемента.
Также добавлены кнопки `Center Sel` и `Fit Sel` для навигации по текущему выделению.
Также в верхней панели отображается текущий primary design target и количество выбранных targets.
Конфигурация interaction policy в демо задается через `DesignEditor.InputGestures` и `DesignEditor.InteractionOptions`.

## Сборка

```bash
dotnet build ArxisStudio.DesignEditor.sln
```

## Примечания

- Библиотека таргетит `netstandard2.0`.
- Демо-приложение таргетит `net9.0`.
- Контролы и шаблоны задуманы как основа, которую обычно донастраивают под конкретный продукт и UX-сценарий.
