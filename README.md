# Immortal — вертикальный срез (Unity 6)

Action-RPG в бесшовном мире: один бессмертный герой проходит путь изгой → вождь → царь,
а вокруг него за тысячелетия растёт цивилизация. Полная спецификация — [docs/RPG_SPEC.md](docs/RPG_SPEC.md).

## Как запустить

1. Unity Hub → **Add project from disk** → выберите эту папку. Подойдёт Unity 6 LTS (`6000.0.x`);
   если установлена другая 6000.x — Hub предложит её, соглашайтесь.
2. Первое открытие: Unity поставит пакеты из `Packages/manifest.json` (Input System, Newtonsoft JSON, URP, Test Framework)
   и автоматически выполнит **Immortal → Bootstrap Project**: создаст `Assets/Scenes/Boot.unity`, `PanelSettings`,
   URP-ассет (если конвейер не назначен) и включит обработку ввода «Both». Если Unity попросит перезапуск из-за Input System — перезапустите.
3. Откройте `Assets/Scenes/Boot.unity` и нажмите **Play**. Главное меню → «Новая игра».

> GameRoot создаётся и в любой другой сцене (`RuntimeInitializeOnLoadMethod`), так что Play работает даже из пустой сцены.

## Управление

| Действие | Клавиша |
|---|---|
| Идти / следовать за курсором (удержание) | ЛКМ по земле, WASD |
| Атаковать / говорить / собирать / войти | ЛКМ по врагу, человеку, ресурсу, постройке |
| Умения в сторону курсора | 1–3, **4** — сила эпохи (только в дыму) |
| Рывок | Пробел |
| Основать лагерь / сдать добычу у костра | F |
| Конь (после приручения в степи) | R |
| Снаряжение, атрибуты, омоложение | I |
| Ремесло (поле / Мастерская) | C |
| Карта | M |
| Меню правителя | Tab |
| Меню, сохранения | Esc, F5 / F9 |

## С чего начать игру

Соберите дерево и камень (ЛКМ по деревьям/валунам) → C → «Дубина» → найдите людей в долине и уговорите их
(дар, хитрость, помощь, давление; сперва «Присмотреться») → F — лагерь → Tab → «Стройка»: шалаши и поле →
деревня, вы — вождь → назначьте наместника (Tab → Люди), выставьте приоритет знаний и попробуйте «Время → Удалиться от дел».

## Структура

```
Assets/Game/Core/          Game.Core.asmdef — вся логика, без UnityEngine (noEngineReferences)
  State/ Commands/ World/ Action/ Civilization/ Character/ Narrative/ Time/ Content/
Assets/Game/Presentation/  виды из примитивов, потоковые чанки, камера, ввод, UI Toolkit из C#
Assets/Game/Editor/        ProjectBootstrap, BuildScript
Assets/Game/Tests/         EditMode-тесты (NUnit), включая бота-прохождение
Assets/Resources/Data/     16 JSON-файлов контента
Assets/Resources/Shaders/  Immortal/Lit и Immortal/Transparent (работают в URP и встроенном конвейере)
tools/                     check.sh, screenshots.sh, dotnet/ — запуск ядра и тестов без Unity
docs/                      спецификация, прогресс, идеи, как расширять
```

## Проверки

```bash
tools/check.sh --core
```
Ядро и все EditMode-тесты через обычный .NET SDK, без Unity (около 20 секунд).

```bash
UNITY_EDITOR=/path/to/Unity tools/check.sh
```
Компиляция в Unity + валидатор контента + EditMode-тесты (`-runTests` без `-quit`).

```bash
UNITY_EDITOR=/path/to/Unity tools/screenshots.sh all
```
Standalone-сборка и PNG-снимки сценариев в `tools/out/screenshots`.

Что сделано и что проверено — в [docs/PROGRESS.md](docs/PROGRESS.md). Как добавлять контент — [docs/HOW_TO_EXTEND.md](docs/HOW_TO_EXTEND.md).
