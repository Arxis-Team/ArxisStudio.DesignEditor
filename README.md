# ArxisStudio.DesignEditor

`ArxisStudio.DesignEditor` — это библиотека для Avalonia UI, предназначенная для построения визуальных редакторов, form designer'ов, layout editor'ов и других IDE-подобных инструментов.

Библиотека предоставляет:

- бесконечную поверхность с панорамированием и зумом
- прямоугольное и множественное выделение
- контейнеры элементов с drag-and-drop и resize
- editor-level overlay-слои для рамок выделения, marquee и resize handles
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
- `SelectionOverlayLayer` — рамки выделения и resize handles
- `InteractionOverlayLayer` — marquee-selection и временные interaction overlays

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
            <ResourceInclude Source="avares://ArxisStudio.DesignEditor/Themes/Styles/ResizeAdorner.axaml" />
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
- resize через `ResizeAdorner`, расположенный на `SelectionOverlayLayer`

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

## Что уже сделано

- `DesignEditor` переведен на layered-архитектуру с `ItemsLayer`, `SelectionOverlayLayer` и `InteractionOverlayLayer`
- рамки одиночного и группового выделения вынесены из `DesignEditorItem` на уровень редактора
- `ResizeAdorner` вынесен из шаблона `DesignEditorItem` и теперь живет на `SelectionOverlayLayer`
- `SelectionBounds` считаются по editor-space геометрии выбранного visual target через `Layout`
- editor-level hit-testing вложенных контролов работает по `Layout`-геометрии и не зависит от runtime `IsHitTestVisible`
- nested design target выбирается внутри visual tree `DataTemplate`/`UserControl`, а не только на уровне контейнера
- drag и resize переводятся на выбранный designer target, а `DesignEditorItem` остается host-контейнером и fallback
- `CenterOnItem(...)` и `FitToView(DesignEditorItem)` используют геометрию реального контрола, если он помечен designer-данными
- демо обновлено и показывает `Center`, `Fit`, `Center Sel`, `Fit Sel`, а также текущий `Target`

## Что дальше

Следующий этап развития редактора:

- формализовать публичную модель selected design object, а не только internal target-resolution
- добавить group editing для нескольких nested targets, а не только overlay union
- добавить editor overlays следующего уровня: guides, snap lines, hover outline
- ввести политики редактирования для контролов без явных size metadata и для layout-driven контейнеров
- позже подключить `ArxisStudio.Markup` как источник `$design`-метаданных, не меняя core-архитектуру редактора

## Запуск демо

Из корня репозитория:

```bash
dotnet run --project samples/DesignEditor.Demo
```

В демо-приложении добавлена кнопка `Center`, которая использует `CenterOnItem(...)` для активного элемента.
Также добавлена кнопка `Fit`, которая использует `FitToView(...)` для активного элемента.
Также добавлены кнопки `Center Sel` и `Fit Sel` для навигации по текущему выделению.
Также в верхней панели отображается текущий primary design target, чтобы было видно, какой nested control выбран редактором.

## Сборка

```bash
dotnet build ArxisStudio.DesignEditor.sln
```

## Примечания

- Библиотека таргетит `netstandard2.0`.
- Демо-приложение таргетит `net9.0`.
- Контролы и шаблоны задуманы как основа, которую обычно донастраивают под конкретный продукт и UX-сценарий.
