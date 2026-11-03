# Как расширять Immortal

Почти всё добавляется данными в `Assets/Resources/Data/*.json`. После правок запустите
`tools/check.sh --core`: валидатор (`ContentValidator`) проверит ссылки между файлами и тупики в диалогах,
а тесты — что игра по-прежнему проходится.

## NPC для вербовки — `recruits.json`
```json
{ "id": "rec_new", "name": "Имя", "region": "r_valley", "need": "hide", "character": "proud", "distrust": 1,
  "baseChance": 0.35, "lifespan": 60, "startAge": 25, "combat": 20, "gathering": 20, "speech": 10, "mind": 4,
  "greeting": "Первая реплика", "bio": "Биография спутника",
  "observeHints": ["про нужду", "про недоверие", "про характер"],
  "quest": { "type": "bring", "item": "hide", "amount": 3, "text": "Просьба", "doneText": "Спасибо" },
  "memory": "met_new" }
```
- `character`: naive, wary, clever, timid, proud, kind, greedy — влияют на подходы (хитрость, давление).
- `quest.type`: `bring` (item + amount) или `clear` (`poiKind`: lair, camp…).
- Имя воспоминания для интерфейса — в `chronicle.json`: `"memory:met_new": ["Текст"]`.
- Свой диалог — поле `dialogue` (по умолчанию общий `dlg_recruit`).

## Событие — `events.json`
Диалог — граф узлов: `text`, `speaker`, `effects` (при входе), `options` (`text`, `conds`, `effects`, `next` или `check`).
`next: "end"` закрывает диалог. Подстановки: `{hero} {npc} {companion} {city} {capital} {nation} {leader} {ancestor} {descendant} {year} {need} {chance} {echo}` и др. (см. `Templates.Value`).

Событие связывает триггер и диалог:
```json
{ "id": "ev_new", "trigger": "monthly", "dialogue": "dlg_new", "chance": 0.03, "once": false, "cooldownYears": 5,
  "conds": [ { "minEra": "city_states", "tabooActive": true } ] }
```
Триггеры: `start, monthly, first_essence, camp_founded, stage_<id>, era_<id>, companion_dying, rebellion,
envoy_<соглашение>, envoy_peace, assassins, after_jump, awaken`. Новый триггер — один вызов `EventSystem.Fire(s, "имя", ctx)` в коде.

Условия (`CondDef`) и эффекты (`EffectDef`) — плоские объекты: все заданные поля должны выполниться / применяются по порядку.
Проверка (`check`): `kind` = `recruit` (подход), `attr`, `skill` или `leader`; исходы `full / partial / fail / crit` → узлы.

## Враг — `enemies.json`
```json
{ "id": "new_enemy", "name": "Имя", "kind": "enemy", "behavior": "melee", "hp": 40, "damage": 8, "speed": 4.5,
  "aggroRange": 10, "attackRange": 1.5, "windup": 0.6, "recover": 0.7, "xp": 12, "color": "#884422", "lootTable": "bandit" }
```
`kind`: enemy, animal, spirit (сущность дыма; `essence` — шанс выпадения ресурса), soldier. `behavior`: melee, ranged, flee.
Появляется через `points_of_interest.json` (`enemies`), фауну региона (`regions.json → fauna`) или эпоху (`eras.json → smokeEntities`).

## Умение — `abilities.json`
Базовые типы: `arc` (range, arc), `projectile` (speed, range, radius = область, count), `dash` (range, radius),
`area` (radius, count = волны, windup), `buff` (buffStat: damage/speed/armor/heal, buffValue, duration).
`scaling`: body или mind; `requiresSmoke` — только в дыму; `mystic` — пробивает броню.
Привяжите к оружию в `items.json → weaponTypes` (basic + skills для слотов 1–3) или к эпохе (`eras.json → superpower`, слот 4).

## Технология — `tech.json`
```json
{ "id": "new_tech", "name": "Имя", "branch": "knowledge", "era": "city_states", "cost": 120, "prereqs": ["writing"],
  "unlockBuildings": ["new_building"], "unlockRecipes": [], "bonus": { "science": 0.1 }, "requiresEssence": 0, "randomDiscovery": false }
```
Бонусы: food, science, production, loyalty, influence, military, housing.

## Постройка — `buildings.json`
`cost`, эффекты (`food housing health science influence defense loyalty production`), `size/height/color` для вида,
`service` (storage, workshop, ruler) для взаимодействия, `specialization` — что строит наместник при этой специализации,
`megastructure: true` — влияет на концовки (`EndingSystem.OnMegastructure`).

## Регион — `regions.json`
Центр `cx, cy` (доли мира 0..1), `primary/secondary` местность, шансы воды и гор, `danger` (множитель силы врагов),
`fauna`, `pois` (тип → количество). Новые типы местности и ресурсных узлов — в `terrain.json`.

## Эпоха — `eras.json`
1. Поставьте `implemented: true`, задайте `requiresStage` (ступень из `settlement_stages.json`), `requiresTechs`,
   `superpower` (умение) и `smokeEntities`, `itemQuality` (из `items.json → qualities`), `role`, тексты главы.
2. Добавьте технологии с `"era": "<id>"`, постройки, рецепты и, при желании, ступени поселения.
3. Диалог главы: событие с триггером `era_<id>`.
Код при этом не меняется: эпохи, их требования и сила в дыму читаются из данных.

## Замена примитивов на модели
Виды живут только в `Assets/Game/Presentation/Views`: `UnitViews.Create` (юниты), `WorldView.BuildPoi / BuildCity` (места и постройки),
`WorldView.RebuildNodes` (деревья и камни через инстансинг). Логика ядра о видах не знает — достаточно подменить создание GameObject.
