# Архитектура Immortal

## Слои

```
Game.Core (без UnityEngine)         Game.Presentation (Unity)                 Ассеты (Resources)
─────────────────────────────       ────────────────────────────────          ─────────────────────
GameState  ← сериализуется          GameRoot: кадр, автомат экранов           Data/*.json      — правила и контент
GameSession: Execute(cmd), Tick()   Views: WorldView, UnitViews, Markers,     Presentation/*.json — визуал и звук
Системы (static): Combat, AI,         Ambience  ← читают GameState            Models/ Fx/ Textures/ Audio/
  City, Nation, Army, Narrative…    Assets: VisualCatalog, ViewFactory,       Portraits/ Icons/
Events: очередь GameEvent             UnitAnimation  ← модели или примитивы
                                    Audio: AudioService, SynthClips
                                    UI: Hud, Screens (UI Toolkit из C#)
```

- **Core ничего не знает о Unity.** Тесты гоняют его обычным .NET (`tools/check.sh --core`).
- **Presentation только читает состояние и шлёт команды.** Любая игровая логика — в Core.
- **Вид не знает, модель у него или примитив**: всё создаёт `ViewFactory`, ассеты подменяются без кода (см. [ASSETS.md](ASSETS.md)).

## Кадр

1. `GameRoot.Update` выбирает экран по состоянию: `GameOver → End`, `Narrative.Active → Dialogue`, `ChronicleOpen → Chronicle`.
2. В мире (`Screen.World`) ввод превращается в команды (`MoveTo`, `AttackUnit`, `Interact`, `UseAbility`…), затем фиксированные тики 20 Гц
   (`Session.Tick()`), ускорение ×5 по T.
3. После каждого тика `ProcessEvents()` разбирает `Session.Events`: числа урона, вспышки, анимации, звуки, тряска камеры, уведомления.
4. Виды синхронизируются с интерполяцией `PrevPos → Pos`, затем HUD.
5. В меню, диалогах и хронике тики не идут: время стоит.

Внутри `Tick()`: герой → движение → взаимодействия → ИИ → бой → добыча; раз в 5 тиков зрение; раз в 10 — разворачивание/сворачивание
юнитов у точек интереса; раз в секунду — армии, зоны, цели; раз в неделю/месяц — города, наука, нации, дипломатия, события, концовки.
Прыжок во времени гоняет те же недельные/месячные шаги в цикле.

## Как добавить…

**Новое игровое событие для вида** (звук/эффект/анимация):
1. значение в `GameEventType` (`Core/GameEvents.cs`);
2. `s.Emit(new GameEvent { Type = …, UnitId, Pos, Id, Text })` в системе Core;
3. ветка в `GameRoot.ProcessEvents` — звук `Sfx("ключ", pos)`, эффект `_units.Burst("ключ", …)`, анимация `_units.OnStrike(...)`.
   Ключи сразу становятся слотами для ассетов.

**Новую команду игрока**: класс `IGameCommand` в `Core/Commands/Commands.cs`; вызов из UI — `_g.Exec(new MyCommand(...))`.

**Новый экран**: значение в `UI.Screen`, слой в конструкторе `Screens` (`Layer(...)`), метод `BuildX()` и ветка в `Show`.
Клавиша — `GameInput.Key` + `Toggle(...)` в `GameRoot.HandleGlobalKeys`.

**Новую систему Core**: статический класс с `StepMonth(GameSession)` или `Tick(...)`, вызов из `WorldSimulation` / `GameSession.Tick`.
Состояние — поля в `GameState` (публичные, с инициализаторами), чтобы старые сохранения загружались.

**Новую механику контента** (условие/эффект диалога): поле в `CondDef`/`EffectDef` (`ContentDtos.cs`) и строка в
`Conditions.Pass` / `Effects.Apply` (`DialogueEngine.cs`). Диалоги генерируются скриптом `tools/gen_events.py` → `events.json`
(можно править и JSON напрямую).

## Сохранения и совместимость
`GameState` сериализуется Newtonsoft целиком. Новые поля с инициализаторами не ломают старые сохранения:
отсутствующее в файле поле получает значение по умолчанию. Переименование или смена типа поля — ломающее изменение;
для него увеличьте `GameState.Version` и добавьте преобразование в `SaveLoad.Load`.

## Детерминизм
Одинаковые seed и команды → одинаковое состояние (`CoreTests.SameSeedAndCommandsGiveSameState`). Правила:
случайность только через `s.Rng` (состояние хранится в `GameState`), никаких `string.GetHashCode`, `DateTime`, `UnityEngine.Random` в Core;
обход словарей — только там, где порядок не влияет на результат. Visual-only вещи (жители, погода) живут в Presentation и на симуляцию не влияют.

## Тесты
`Assets/Game/Tests` — EditMode (NUnit). Помощники: `TestUtil.NewGame`, `Ticks`, `TickUntil`, `SettlementTests.WithCamp`.
`ScriptedPlaythroughTest` — бот, проходящий игру через команды: если меняете баланс основного пути, он первым скажет, что путь сломан.
