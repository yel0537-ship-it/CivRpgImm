# Модели, анимации, текстуры и звук

Игра сейчас рисуется примитивами и звучит синтезированными заглушками, но **каждый объект можно заменить
своим ассетом без правки кода**. Достаточно положить файл с правильным именем в `Assets/Resources/...`.
Окно **Immortal → Покрытие ассетов** покажет все слоты: что уже своё (✓), что ещё заглушка, и куда класть файл.

## 1. Модели (префабы)

Соглашение имён: `Assets/Resources/<папка>/<id>.prefab`.

| Что | Папка | id | Пример |
|---|---|---|---|
| Герой, спутники, NPC, враги, звери, солдаты | `Models/Units` | `hero`, `companion`, `npc`, `leader`, `horse`, id врага из `enemies.json`, id вербуемого из `recruits.json`, `leader_<nation>` | `Models/Units/wolf.prefab` |
| Постройки | `Models/Buildings` | id из `buildings.json`, `wall_stone`, `wall_wood` | `Models/Buildings/hut.prefab` |
| Деревья, кусты, камни | `Models/Nodes` | id узла из `terrain.json` (`tree`, `berry_bush`, `stone`, …) или его `shape` | `Models/Nodes/tree.prefab` |
| Точки интереса | `Models/Pois` | id или `kind` из `points_of_interest.json` | `Models/Pois/ruins.prefab` |
| Оружие в руке | `Models/Items` | id предмета | `Models/Items/bronze_sword.prefab` |
| Добыча на земле | `Models/Loot` | id предмета, `equipment`, `resource` | `Models/Loot/essence.prefab` |
| Снаряды | `Models/Projectiles` | id умения, `enemy_shot`, `mystic`, `default` | `Models/Projectiles/power_fireball.prefab` |
| Эффекты | `Fx` | `hit`, `death`, `explosion`, `explosion_<умение>`, `slash_<умение>`, `levelup`, `rejuvenate`, `smoke_zone` | `Fx/explosion.prefab` |
| Маркеры на земле | `Fx` | `area_marker` (зона удара, масштаб = радиус), `hover_ring`, `click_marker` | `Fx/area_marker.prefab` |
| Погода | `Fx` | `weather_<регион>` (`weather_r_tundra`…), `weather_smoke` — следует за героем | `Fx/weather_r_tundra.prefab` |
| Жители городов | `Models/Units` | `citizen` (иначе `npc`) — бродят между постройками, число растёт с населением | `Models/Units/citizen.prefab` |

Запасные цепочки: спутник ищет `rec_ayna`, потом `companion`; правитель — `leader_arkhan`, потом `leader`;
юнит без модели остаётся капсулой. Поэтому можно начать с одной общей модели (`companion`) и добавлять уникальные позже.

**Требования к модели юнита**
- Pivot в ногах, «вперёд» — ось +Z, масштаб 1 юнит = 1 м (герой ≈ 1.8 м). Иначе поправьте `scale`, `yOffset`, `rotationY` в `visuals.json`.
- Кость для оружия: `hand_r`, `Hand_R`, `RightHand` или `mixamorig:RightHand` (или своё имя в `handBone`).
- Высота полоски здоровья считается по габаритам; можно задать `height`.

**Узлы ресурсов** рисуются инстансингом: в префабе берутся все `MeshFilter + MeshRenderer` (без скриптов и аниматоров),
материалы копируются с включённым GPU Instancing. Тысячи деревьев не создают тысяч объектов.

**Дым** (`Fx/smoke_zone`): префаб масштабируется на радиус зоны (модель радиуса 1 м с центром в pivot).

### visuals.json — явные настройки
`Assets/Resources/Presentation/visuals.json`:
```json
{
  "folders": { "unit": "Models/Units" },
  "entries": {
    "unit:hero":      { "prefab": "Characters/Hero/HeroPrefab", "scale": 1.0, "yOffset": 0, "rotationY": 180, "handBone": "R_Hand" },
    "unit:soldier":   { "prefab": "Characters/Soldier", "tintByNation": true },
    "building:palace":{ "prefab": "Buildings/Palace_v2", "scale": 1.4 },
    "fx:explosion":   { "prefab": "VFX/BigBoom", "life": 3 }
  }
}
```
`tintByNation` перекрашивает материал в цвет нации (свойства `_BaseColor` и `_Color`). Папки в `folders` меняют соглашение по категориям.

## 2. Анимации

Если в префабе есть `Animator`, игра управляет им через стандартные параметры (все необязательны, лишние игнорируются):

| Параметр | Тип | Когда |
|---|---|---|
| `Speed` | float | скорость движения, м/с |
| `Windup` | bool | замах перед ударом (показанный удар врага) |
| `Dead` | bool | мёртв |
| `Gather` | bool | герой собирает ресурс |
| `Mounted` | bool | герой верхом |
| `InCombat` | bool | недавно был бой |
| `Attack` | trigger | удар (конец замаха, удар умением) |
| `Cast` | trigger | мистическое умение, усиление, область |
| `Hit` | trigger | получил урон |
| `Death` | trigger | умер |
| `Revive` | trigger | очнулся после долгого сна |

Свой контроллер анимации — компонент на префабе, реализующий `Immortal.Presentation.IUnitAnimator`; тогда стандартный мост не используется.
Без Animator модель анимируется процедурно (покачивание, наклоны, выпады) — как сейчас примитивы.

## 3. Текстуры местности

`Assets/Resources/Textures/Terrain/<id>.png`, где `id` — тип местности из `terrain.json`:
`grass forest steppe sand swamp snow hills water mountain field road`.
Любой размер и формат (пережимается в слой 256×256 массива текстур), бесшовные, повтор — раз в 4 м.
Для отсутствующих типов рисуется процедурная текстура в цвет из `terrain.json`. Туман войны, оттенок эпохи и времени года
накладываются шейдером `Immortal/Terrain` поверх.

Модели со своими материалами (URP Lit и т.п.) освещаются солнцем, которое создаёт игра.

## 4. Звук

### Эффекты
`Assets/Resources/Audio/Sfx/<ключ>.wav|ogg` и варианты `<ключ>_1 … <ключ>_4` (выбирается случайный).

| Ключ | Событие |
|---|---|
| `hit`, `hit_hero` | попадание по врагу / по своим |
| `death` | смерть |
| `windup`, `strike` | начало и конец замаха врага |
| `swing`, `shoot`, `cast`, `dash` | умения героя по типу |
| `ability_<id>` | конкретное умение (иначе по типу) |
| `explosion`, `explosion_<id>` | взрыв (общий / умения) |
| `pickup`, `essence`, `gather` | подбор, мистический ресурс, сбор |
| `footstep`, `hoof` | шаги, копыта |
| `build`, `recruit`, `sad`, `levelup`, `objective` | стройка, вербовка, смерть спутника, уровень, цель |
| `zone_enter`, `era`, `timejump`, `dialogue` | вход в дым, новая эпоха, прыжок во времени, диалог |
| `ui_click`, `ui_open` | интерфейс |

### Музыка и фон
`Audio/Music/<ключ>`: `menu`, `world`, `combat`, `smoke`, `chronicle`, `region_<id>`, `era_<id>`.
Выбор по ситуации: меню → `menu`; хроника → `chronicle`; дым → `smoke`; бой рядом → `combat`;
иначе `region_<регион>`, потом `era_<эпоха>`, потом `world`. Смена — с перекрёстным затуханием.
`Audio/Ambient/<ключ>`: `smoke`, `region_<id>`, `wind`.

### audio.json
`Assets/Resources/Presentation/audio.json`: громкость, разброс высоты тона, 2D/3D, перезарядка, запасной ключ, явные пути:
```json
{ "sfx": { "hit": { "clips": ["Sounds/Impacts/flesh_01", "Sounds/Impacts/flesh_02"], "volume": 0.7, "pitchMin": 0.9, "pitchMax": 1.1 } },
  "music": { "region:r_forest": "Music/ForestTheme" },
  "ambient": { "smoke": "Ambience/WhisperLoop" } }
```
Заглушки-синтез можно выключить в «Настройках» (тогда без файлов будет тишина).

## 5. Портреты и иконки

| Что | Путь | Где видно |
|---|---|---|
| Портрет собеседника | `Resources/Portraits/<id>` — `rec_ayna`, `leader_arkhan`, `leader`, `companion`, `npc`, `descendant`, `narrator`, `hero` | слева в окне диалога |
| Иконки умений | `Resources/Icons/Abilities/<id умения>`, `dash` | панель умений 1–4 |
| Иконки предметов | `Resources/Icons/Items/<id предмета>` | снаряжение (I) |
| Иконки построек | `Resources/Icons/Buildings/<id постройки>` | Tab → Стройка |

Любой формат картинки, импорт как Default/Sprite (2D). Нет файла — элемент просто без картинки.

## 6. Интерфейс
UI собирается из C# (`Assets/Game/Presentation/UI`). Цвета и шрифт — в `UiKit.cs` (`Ui.Bg`, `Ui.Gold`, `Ui.CyrillicFont`).
Свой шрифт: положите `Resources/UI/Font.ttf` — он будет использован вместо системного (с поддержкой кириллицы).
