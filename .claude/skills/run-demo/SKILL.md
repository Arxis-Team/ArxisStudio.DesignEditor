---
name: run-demo
description: Собрать, запустить и подвигать DesignEditor.Demo, чтобы визуально проверить правку в редакторе. Умеет скриншот, клик и зум колесом по координатам окна. Использовать, когда нужно убедиться, что изменение работает в живом приложении — выделение, адорнеры, drag/resize, viewport, темы — а не только компилируется. Компиляции недостаточно: это UI-библиотека, большинство регрессий видно только в интерактиве.
---

# Запуск и проверка демо

Тестового проекта в решении нет, поэтому единственный способ проверить правку — запустить демо и потыкать. Этот скилл убирает ручную Win32-обвязку.

## Порядок

### 1. Собрать

```bash
dotnet build ArxisStudio.DesignEditor.sln
```

Ожидается **0 ошибок, 0 предупреждений** (у библиотеки включён `GenerateDocumentationFile` → CS1591 на недокументированный публичный член).

Если падает `MSB3021`/`MSB3027` («файл используется другим процессом») на `ArxisStudio.DesignEditor.dll` — **это не ошибка кода**, а блокировка от `Avalonia.Designer.HostApp` (XAML-превьюер Rider). Компиляция при этом проходит, ломается только копирование. Варианты:

- закрыть вкладку превью в Rider;
- `taskkill //PID <pid> //F` — pid берётся из текста ошибки;
- проверить код в обход: `dotnet build src/ArxisStudio.DesignEditor.csproj`.

### 2. Запустить

```bash
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action start
```

Ждёт появления окна и печатает pid. Если процесс упал — печатает stderr. Логи лежат в `%TEMP%\designeditor-demo\`.

### 3. Снять скриншот

```bash
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action shot -Out "$TMP/demo.png"
```

Затем прочитать PNG инструментом Read — картинка видна напрямую.

### 4. Потыкать

**Координаты — относительно окна, ровно как на скриншоте.** Скрипт сам добавляет позицию окна на экране. Это главная ловушка: окно почти никогда не в (0,0), и клик по «экранным» координатам уходит мимо.

```bash
# выбрать вложенный контрол (пиксель со скриншота)
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action click -X 706 -Y 453

# зум колесом: + приближает, - отдаляет
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action wheel -X 300 -Y 200 -Notches 4

# контекстное меню — оно должно открыться в точке клика
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action rightclick -X 500 -Y 400
```

`-Action status` печатает pid, responding, заголовок и прямоугольник окна.

### 5. Закрыть

```bash
powershell -ExecutionPolicy Bypass -File .claude/skills/run-demo/scripts/demo.ps1 -Action stop
```

Закрывать обязательно: живой процесс держит `ArxisStudio.DesignEditor.dll` и следующая сборка упадёт с той же MSB3021.

## Что проверять

Верхняя панель демо — готовый индикатор состояния, по ней читается результат без догадок:

| Поле | Что подтверждает |
|---|---|
| `N items` | `SelectedElements.Count` — счёт **контейнеров**, не вложенных targets |
| `Target:` | тип primary target (`TextBlock`, `DesignEditorItem`, …) |
| `Targets:` | `SelectedDesignTargetsCount` — вложенные targets |
| `X / Y / W / H` | геометрия выделения в design-координатах |
| `Viewport` / `%` | `ViewportLocation` и `ViewportZoom` |
| `Center / Fit / Center Sel / Fit Sel` | появляются только при непустом выделении |

Внутри Dashboard-элемента выводятся `DesignX / DesignY / X / Y` для `TextBlock1` — прямая проверка двусторонней синхронизации `Layout` (глобальные ↔ локальные координаты).

Минимальный прогон после правок в редакторе:

1. приложение стартует, окно есть, stderr пуст;
2. элементы отрисованы, сетка выровнена;
3. клик по заголовку внутри карточки → `1 items`, `Target: TextBlock`, адорнер с 8 хэндлами точно по контролу;
4. колесо → зум меняется, сетка остаётся DPI-чёткой, адорнер не разъезжается с контролом;
5. клик по пустому месту → выделение снимается.

Пункт 4 важен отдельно: адорнеры позиционируются в мировых координатах с обратным масштабом, поэтому расхождение видно только на зуме, отличном от 100%.

## Ограничения

- Только Windows: используется `user32.dll` (`mouse_event`, `GetWindowRect`, `CopyFromScreen`).
- Скрипт двигает реальный курсор и поднимает окно на передний план.
- Drag пока не реализован — для него нужна отдельная последовательность down/move/up.
