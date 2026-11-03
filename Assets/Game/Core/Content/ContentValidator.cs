using System.Collections.Generic;

namespace Immortal.Core.Content
{
    /// <summary>Проверяет ссылки между контентными файлами и тупики в диалоговых графах.</summary>
    public static class ContentValidator
    {
        public static List<string> Validate(ContentDatabase db)
        {
            var errors = new List<string>();
            void Need(bool ok, string msg) { if (!ok) errors.Add(msg); }
            bool Item(string id) => id != null && db.Items.ContainsKey(id);

            foreach (var t in db.TerrainFile.terrains)
                foreach (var n in t.nodes) Need(db.Nodes.ContainsKey(n.node), $"terrain {t.id}: нет узла {n.node}");
            foreach (var n in db.TerrainFile.nodes)
                foreach (var y in n.yields.Keys) Need(Item(y), $"node {n.id}: нет предмета {y}");

            foreach (var r in db.Regions)
            {
                Need(db.Terrain.ContainsKey(r.primary), $"region {r.id}: нет местности {r.primary}");
                Need(r.secondary == null || db.Terrain.ContainsKey(r.secondary), $"region {r.id}: нет местности {r.secondary}");
                foreach (var f in r.fauna) Need(db.Enemies.ContainsKey(f), $"region {r.id}: нет фауны {f}");
                foreach (var p in r.pois.Keys) Need(db.Poi.ContainsKey(p), $"region {r.id}: нет POI {p}");
            }

            foreach (var p in db.PoiDefs)
            {
                foreach (var e in p.enemies) Need(db.Enemies.ContainsKey(e.id), $"poi {p.id}: нет врага {e.id}");
                foreach (var k in p.rewards.Keys) Need(Item(k), $"poi {p.id}: нет предмета {k}");
                Need(p.dialogue == null || db.Dialogues.ContainsKey(p.dialogue), $"poi {p.id}: нет диалога {p.dialogue}");
                Need(p.lootTable == null || db.LootTables.ContainsKey(p.lootTable), $"poi {p.id}: нет таблицы {p.lootTable}");
            }

            foreach (var e in db.Eras)
            {
                Need(e.superpower == null || db.Abilities.ContainsKey(e.superpower), $"era {e.id}: нет умения {e.superpower}");
                foreach (var s in e.smokeEntities) Need(db.Enemies.ContainsKey(s), $"era {e.id}: нет сущности {s}");
                Need(e.requiresStage == null || db.StageIndex.ContainsKey(e.requiresStage), $"era {e.id}: нет ступени {e.requiresStage}");
                Need(e.itemQuality == null || db.Qualities.ContainsKey(e.itemQuality), $"era {e.id}: нет качества {e.itemQuality}");
            }

            foreach (var t in db.TechFile.techs)
            {
                Need(db.Era.ContainsKey(t.era), $"tech {t.id}: нет эпохи {t.era}");
                foreach (var p in t.prereqs) Need(db.Tech.ContainsKey(p), $"tech {t.id}: нет технологии {p}");
                foreach (var b in t.unlockBuildings) Need(db.Buildings.ContainsKey(b), $"tech {t.id}: нет постройки {b}");
                foreach (var r in t.unlockRecipes) Need(db.Recipes.ContainsKey(r), $"tech {t.id}: нет рецепта {r}");
                Need(t.requiresBuilding == null || db.Buildings.ContainsKey(t.requiresBuilding), $"tech {t.id}: нет постройки {t.requiresBuilding}");
            }

            foreach (var r in db.ItemsFile.recipes)
            {
                Need(Item(r.result), $"recipe {r.id}: нет результата {r.result}");
                foreach (var k in r.inputs.Keys) Need(Item(k), $"recipe {r.id}: нет сырья {k}");
                Need(r.fieldQuality == null || db.Qualities.ContainsKey(r.fieldQuality), $"recipe {r.id}: нет качества {r.fieldQuality}");
            }
            foreach (var w in db.ItemsFile.weaponTypes)
            {
                Need(db.Abilities.ContainsKey(w.basic), $"weaponType {w.id}: нет умения {w.basic}");
                foreach (var s in w.skills) Need(db.Abilities.ContainsKey(s), $"weaponType {w.id}: нет умения {s}");
            }
            foreach (var i in db.ItemsFile.items)
                Need(i.weaponType == null || db.WeaponTypes.ContainsKey(i.weaponType), $"item {i.id}: нет типа оружия {i.weaponType}");
            foreach (var l in db.ItemsFile.lootTables)
            {
                foreach (var e in l.entries) Need(Item(e.item), $"loot {l.id}: нет предмета {e.item}");
                foreach (var e in l.equipPool) Need(Item(e), $"loot {l.id}: нет снаряжения {e}");
            }

            foreach (var r in db.RecruitsFile.recruits)
            {
                Need(db.RegionById.ContainsKey(r.region), $"recruit {r.id}: нет региона {r.region}");
                Need(db.Dialogues.ContainsKey(r.dialogue), $"recruit {r.id}: нет диалога {r.dialogue}");
                Need(r.need == null || Item(r.need), $"recruit {r.id}: нет предмета нужды {r.need}");
                if (r.quest != null && r.quest.type == "bring") Need(Item(r.quest.item), $"recruit {r.id}: квест — нет предмета {r.quest.item}");
            }

            foreach (var e in db.EnemyList)
                Need(e.lootTable == null || db.LootTables.ContainsKey(e.lootTable), $"enemy {e.id}: нет таблицы {e.lootTable}");

            foreach (var b in db.BuildingList)
            {
                Need(db.Era.ContainsKey(b.era), $"building {b.id}: нет эпохи {b.era}");
                Need(b.minStage == null || db.StageIndex.ContainsKey(b.minStage), $"building {b.id}: нет ступени {b.minStage}");
                foreach (var k in b.cost.Keys) Need(Item(k), $"building {b.id}: нет ресурса {k}");
                Need(b.tech == null || db.Tech.ContainsKey(b.tech), $"building {b.id}: нет технологии {b.tech}");
            }
            foreach (var s in db.Stages)
            {
                foreach (var b in s.requiredBuildings) Need(db.Buildings.ContainsKey(b), $"stage {s.id}: нет постройки {b}");
                foreach (var k in s.cost.Keys) Need(Item(k), $"stage {s.id}: нет ресурса {k}");
                Need(s.unlocksEra == null || db.Era.ContainsKey(s.unlocksEra), $"stage {s.id}: нет эпохи {s.unlocksEra}");
            }
            foreach (var n in db.NationDefs)
                Need(n.region == null || db.RegionById.ContainsKey(n.region), $"nation {n.id}: нет региона {n.region}");

            foreach (var ev in db.EventsFile.events)
                Need(db.Dialogues.ContainsKey(ev.dialogue), $"event {ev.id}: нет диалога {ev.dialogue}");

            foreach (var d in db.EventsFile.dialogues) ValidateDialogue(db, d, errors);
            var objIds = new HashSet<string>();
            foreach (var o in db.Objectives)
            {
                Need(objIds.Add(o.id), $"objective {o.id}: повтор id");
                Need(!string.IsNullOrEmpty(o.title), $"objective {o.id}: нет заголовка");
                foreach (var set in o.done) foreach (var c in set)
                {
                    Need(c.minEra == null || db.Era.ContainsKey(c.minEra), $"objective {o.id}: нет эпохи {c.minEra}");
                    Need(c.cityStageMin == null || db.StageIndex.ContainsKey(c.cityStageMin), $"objective {o.id}: нет ступени {c.cityStageMin}");
                    Need(c.item == null || db.Items.ContainsKey(c.item), $"objective {o.id}: нет предмета {c.item}");
                }
            }
            return errors;
        }

        static void ValidateDialogue(ContentDatabase db, DialogueDef d, List<string> errors)
        {
            var ids = new HashSet<string>();
            foreach (var n in d.nodes) if (!ids.Add(n.id)) errors.Add($"dialogue {d.id}: повтор узла {n.id}");
            if (!ids.Contains(d.start)) errors.Add($"dialogue {d.id}: нет стартового узла {d.start}");

            void Ref(string id, string from)
            {
                if (id != null && id != "end" && !ids.Contains(id)) errors.Add($"dialogue {d.id}: узел {from} ссылается на несуществующий {id}");
            }

            foreach (var n in d.nodes)
            {
                // Тупик: у узла нет вариантов вовсе, или все варианты условны и нет безусловного выхода.
                if (n.options.Count == 0) { errors.Add($"dialogue {d.id}: узел {n.id} — тупик (нет вариантов)"); continue; }
                bool hasUnconditional = false;
                foreach (var o in n.options)
                {
                    if (o.conds == null || o.conds.Count == 0) hasUnconditional = true;
                    Ref(o.next, n.id);
                    if (o.check != null)
                    {
                        Ref(o.check.full, n.id); Ref(o.check.partial, n.id); Ref(o.check.fail, n.id); Ref(o.check.crit, n.id);
                        if (o.check.full == null || o.check.fail == null) errors.Add($"dialogue {d.id}: проверка в {n.id} без исходов");
                    }
                    else if (o.next == null) errors.Add($"dialogue {d.id}: вариант в {n.id} без next");
                    foreach (var e in o.effects) ValidateEffect(db, d.id, e, errors);
                }
                foreach (var e in n.effects) ValidateEffect(db, d.id, e, errors);
                if (!hasUnconditional) errors.Add($"dialogue {d.id}: узел {n.id} — возможен тупик (нет безусловного варианта)");
            }

            // Достижимость конца из каждого узла.
            var canEnd = new HashSet<string>();
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var n in d.nodes)
                {
                    if (canEnd.Contains(n.id)) continue;
                    foreach (var o in n.options)
                    {
                        bool ok = false;
                        if (o.effects.Exists(e => e.endDialogue)) ok = true;
                        if (o.check != null)
                            ok |= Ends(o.check.full, canEnd) || Ends(o.check.fail, canEnd);
                        else ok |= Ends(o.next, canEnd);
                        if (ok) { canEnd.Add(n.id); changed = true; break; }
                    }
                }
            }
            foreach (var n in d.nodes)
                if (!canEnd.Contains(n.id)) errors.Add($"dialogue {d.id}: из узла {n.id} нельзя выйти");
        }

        static bool Ends(string next, HashSet<string> canEnd) => next == "end" || (next != null && canEnd.Contains(next));

        static void ValidateEffect(ContentDatabase db, string did, EffectDef e, List<string> errors)
        {
            if (e.item != null && !db.Items.ContainsKey(e.item)) errors.Add($"dialogue {did}: эффект — нет предмета {e.item}");
            if (e.spawn != null && !db.Enemies.ContainsKey(e.spawn)) errors.Add($"dialogue {did}: эффект — нет врага {e.spawn}");
            if (e.startDialogue != null && !db.Dialogues.ContainsKey(e.startDialogue)) errors.Add($"dialogue {did}: эффект — нет диалога {e.startDialogue}");
        }
    }
}
