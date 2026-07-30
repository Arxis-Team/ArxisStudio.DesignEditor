---
name: avalonia-api
description: Выяснить, что есть в конкретной версии Avalonia из NuGet — какие TFM поддерживаются, какие пакеты вышли для мажора, чем заменён исчезнувший API. Использовать при обновлении версии Avalonia, при ошибках CS1061/CS0122 после апгрейда, при вопросах «что вместо X в новой версии» и «какая версия сейчас актуальна». Отвечать по памяти нельзя — версии выходят чаще, чем обновляется knowledge cutoff.
---

# Сверка API Avalonia по пакетам NuGet

Источник истины — сам пакет, а не память и не документация в вебе. Внутри `.nupkg` лежат XML-доки со всеми публичными сигнатурами, по ним ищется замена исчезнувшему API.

Работать во временном каталоге (scratchpad), не в репозитории.

## 1. Какие версии есть

```bash
curl -s https://api.nuget.org/v3-flatcontainer/avalonia/index.json | tr ',' '\n' | tail -20
```

Имя пакета в URL — в нижнем регистре. Так же проверяется, вышел ли пакет для нужного мажора: у `avalonia.diagnostics`, например, последний релиз 11.3.18 — для 12.x его нет.

Отличить официальные пакеты от однотипных подделок:

```bash
curl -s "https://azuresearch-usnc.nuget.org/query?q=Avalonia&prerelease=false&take=100" | python -c "
import sys,json
for x in json.load(sys.stdin)['data']:
    if 'Avalonia Team' in str(x.get('authors','')): print(x['id'], x['version'])
"
```

В выдаче по «avalonia devtools» всплывают `AvaDiagnostics12`, `AvaDevTools`, `ProDiagnostics` с правдоподобными версиями — это **не** пакеты команды Avalonia.

## 2. Какие TFM и зависимости

```bash
curl -s -o av.nupkg https://api.nuget.org/v3-flatcontainer/avalonia/12.1.1/avalonia.12.1.1.nupkg
unzip -l av.nupkg | grep 'lib/'          # поддерживаемые TFM
unzip -p av.nupkg Avalonia.nuspec        # зависимости по группам TFM
```

Именно так выясняется, что Avalonia 12 поставляет только `net8.0` и `net10.0` — отсюда требование к `src` таргетить минимум `net8.0`.

## 3. Чем заменён исчезнувший API

Распаковать XML-доки и искать по doc-ID:

```bash
unzip -o -q av.nupkg "lib/net10.0/Avalonia.Base.xml" "lib/net10.0/Avalonia.Controls.xml" -d x

# все extension-методы VisualTree
grep -oE 'M:Avalonia\.VisualTree\.VisualExtensions\.[A-Za-z]+' x/lib/net10.0/Avalonia.Base.xml | sort -u

# где вообще живёт нужное свойство
grep -oE '[MPTEF]:Avalonia\.[A-Za-z0-9_.]*\.RenderScaling' x/lib/net10.0/*.xml | sort -u

# прочитать описание конкретного члена
grep -A8 '"M:Avalonia.Controls.TopLevel.GetTopLevel' x/lib/net10.0/Avalonia.Controls.xml
```

Префиксы doc-ID: `T:` тип, `M:` метод, `P:` свойство, `E:` событие, `F:` поле.

Сообщения `[Obsolete]` тоже попадают в доки и часто прямо называют замену — читать их стоит целиком, а не переименовывать член механически.

## 4. Главная оговорка

**Присутствие символа в XML-доках не означает, что он доступен.** Доки содержат и то, что стало `internal`: `BindingPlugins` в 12.1.1 документирован полностью, но помечен internal, и обращение к нему даёт `CS0122`. Доки отвечают на вопрос «как теперь называется», а доступность подтверждается только компиляцией.

Поэтому порядок такой: нашли кандидата в доках → правим код → `dotnet build` → если `CS0122`, ищем другой путь.

## 5. После обновления версии

Прогнать демо: компиляция не ловит регрессии рендера, тем и разрешения ресурсов. См. скилл `run-demo`.

Известные точки разрыва при переходе 11 → 12 записаны в корневом `CLAUDE.md`, раздел «Заметки по Avalonia 12».
