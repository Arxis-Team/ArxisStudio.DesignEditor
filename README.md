# ArxisStudio.DesignEditor

`ArxisStudio.DesignEditor` — это библиотека для Avalonia UI, предназначенная для построения визуальных редакторов, form designer'ов, layout editor'ов и других IDE-подобных инструментов.

Библиотека предоставляет:

- бесконечную поверхность с панорамированием и зумом
- прямоугольное и множественное выделение
- контейнеры элементов с drag-and-drop и resize
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
- состояния взаимодействия: idle, selecting, panning
- групповое перемещение выбранных элементов

### `DesignEditorItem`

Контейнер элемента редактора, который создается автоматически для каждого item'а. Добавляет:

- состояние выделения
- перетаскивание
- ручки изменения размера
- привязку позиции через `Location`
- визуальные состояния `:selected`, `:dragging`, `:resizing`

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
- resize через `ResizeAdorner`

## Пример использования `Layout`

Для вложенного контента внутри шаблона элемента можно использовать `Layout` напрямую:

```xml
<controls:AbsolutePanel>
    <TextBlock attached:Layout.DesignX="200"
               attached:Layout.DesignY="100"
               Text="Dashboard" />
</controls:AbsolutePanel>
```

Это удобно, когда внутреннему содержимому шаблона нужны координаты относительно всей поверхности дизайна.

## Запуск демо

Из корня репозитория:

```bash
dotnet run --project samples/DesignEditor.Demo
```

## Сборка

```bash
dotnet build ArxisStudio.DesignEditor.sln
```

## Примечания

- Библиотека таргетит `netstandard2.0`.
- Демо-приложение таргетит `net9.0`.
- Контролы и шаблоны задуманы как основа, которую обычно донастраивают под конкретный продукт и UX-сценарий.
