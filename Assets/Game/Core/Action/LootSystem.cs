using System;
using System.Collections.Generic;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using Immortal.Core.Content;

namespace Immortal.Core.Action
{
    /// <summary>Добыча с врагов и тайников, подбор, сбор ресурсов, ремесло в поле и в Мастерской, конь.</summary>
    public static class LootSystem
    {
        public static void Tick(GameSession s, float dt)
        {
            var hero = s.HeroUnit;
            var list = s.State.Loot;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var l = list[i];
                l.Life -= dt;
                if (hero != null && hero.Ai != AiState.Dead && Vec2.SqrDistance(hero.Pos, l.Pos) < 2.6f)
                {
                    Pickup(s, l);
                    list.RemoveAt(i);
                    continue;
                }
                if (l.Life <= 0) list.RemoveAt(i);
            }
            GatherTick(s, dt);
        }

        static void Pickup(GameSession s, LootDrop l)
        {
            var h = s.State.Hero;
            if (l.Item != null)
            {
                h.Items.Add(l.Item);
                AutoEquip(s, l.Item);
                s.Emit(new GameEvent { Type = GameEventType.Pickup, Pos = l.Pos, Text = ItemLabel(s, l.Item) });
                return;
            }
            h.Add(l.Resource, l.Amount);
            s.Emit(new GameEvent { Type = GameEventType.Pickup, Pos = l.Pos, Text = $"+{l.Amount} {s.Content.ItemName(l.Resource)}", Id = l.Resource });
            if (l.Resource == "essence") HeroSystem.OnEssencePicked(s);
        }

        public static void Drop(GameSession s, Vec2 pos, string resource, int amount)
        {
            if (amount <= 0) return;
            var off = Vec2.FromAngle(s.Rng.Range(0, 6.283f)) * s.Rng.Range(0.2f, 1.2f);
            s.State.Loot.Add(new LootDrop { Id = s.State.NextId++, Pos = s.Map.NearestWalkable(pos + off, 2), Resource = resource, Amount = amount });
        }

        public static void DropItem(GameSession s, Vec2 pos, ItemInstance item)
        {
            s.State.Loot.Add(new LootDrop { Id = s.State.NextId++, Pos = s.Map.NearestWalkable(pos, 2), Item = item, Life = 300 });
        }

        public static void DropTable(GameSession s, string tableId, Vec2 pos)
        {
            if (!s.Content.LootTables.TryGetValue(tableId, out var t)) return;
            foreach (var e in t.entries)
                if (s.Rng.Chance(e.chance)) Drop(s, pos, e.item, s.Rng.Range(e.min, e.max + 1));
            if (t.equipPool.Count > 0 && s.Rng.Chance(t.equipChance))
                DropItem(s, pos, RollItem(s, t.equipPool[s.Rng.NextInt(t.equipPool.Count)], CurrentQuality(s, false)));
        }

        /// <summary>Качество снаряжения привязано к эпохе нации игрока.</summary>
        public static string CurrentQuality(GameSession s, bool workshop)
        {
            var era = s.PlayerEraDef;
            string q = era.itemQuality ?? "field";
            if (!workshop && s.PlayerNation == null) q = "field";
            return q;
        }

        public static ItemInstance RollItem(GameSession s, string defId, string quality)
        {
            var item = new ItemInstance { Id = s.State.NewId("it"), DefId = defId, Quality = quality };
            int affixes = s.Content.Qualities.TryGetValue(quality, out var q) ? q.affixes : 0;
            var pool = s.Content.ItemsFile.affixes;
            if (s.Rng.Chance(0.4f)) affixes++;
            for (int i = 0; i < affixes && pool.Count > 0; i++)
            {
                var a = pool[s.Rng.NextInt(pool.Count)];
                float v = (float)Math.Round(s.Rng.Range(a.min, a.max), 2);
                item.Affixes[a.stat] = (item.Affixes.TryGetValue(a.stat, out var old) ? old : 0) + v;
            }
            return item;
        }

        public static string ItemLabel(GameSession s, ItemInstance i)
        {
            string name = s.Content.ItemName(i.DefId);
            string q = i.Quality != null && s.Content.Qualities.TryGetValue(i.Quality, out var qd) ? qd.name : "";
            return $"{name} ({q})";
        }

        public static string SlotOf(GameSession s, ItemInstance i) => s.Content.Items.TryGetValue(i.DefId, out var d) ? d.slot : null;

        static float Score(GameSession s, ItemInstance i)
        {
            if (!s.Content.Items.TryGetValue(i.DefId, out var d)) return 0;
            float sc = (d.damage + d.armor * 2) * HeroSystem.QualityMult(s, i);
            foreach (var kv in i.Affixes) sc += kv.Value * (kv.Key == "hp" ? 0.2f : kv.Key == "damage" || kv.Key == "armor" ? 1f : 10f);
            return sc;
        }

        public static void AutoEquip(GameSession s, ItemInstance item)
        {
            var h = s.State.Hero;
            string slot = SlotOf(s, item);
            var cur = slot == "weapon" ? h.WeaponId : slot == "armor" ? h.ArmorId : slot == "trinket" ? h.TrinketId : null;
            var curItem = HeroSystem.Item(h, cur);
            if (slot != null && (curItem == null || Score(s, item) > Score(s, curItem))) Equip(s, item.Id);
        }

        public static bool Equip(GameSession s, string itemId)
        {
            var h = s.State.Hero;
            var it = HeroSystem.Item(h, itemId);
            if (it == null) return false;
            switch (SlotOf(s, it))
            {
                case "weapon": h.WeaponId = it.Id; break;
                case "armor": h.ArmorId = it.Id; break;
                case "trinket": h.TrinketId = it.Id; break;
                default: return false;
            }
            HeroSystem.RefreshUnitStats(s);
            return true;
        }

        // ---------- Сбор ----------
        public static void StartGather(GameSession s, int tile)
        {
            var h = s.State.Hero;
            var node = s.Map.NodeAt(tile);
            if (node == null) return;
            h.Pending = new PendingAction { Kind = "gather", Tile = tile, Pos = s.Map.TileCenter(tile) };
            h.GatherProgress = 0;
        }

        static void GatherTick(GameSession s, float dt)
        {
            var h = s.State.Hero; var hero = s.HeroUnit;
            if (h.Pending == null || h.Pending.Kind != "gather" || hero == null) return;
            if (Vec2.Distance(hero.Pos, h.Pending.Pos) > s.Balance.hero.interactRange + 0.8f) return;
            hero.Path = null;
            float speed = 1f + HeroSystem.Skill(h, "gathering") / 50f;
            h.GatherProgress += dt * speed;
            if (h.GatherProgress < s.Balance.hero.gatherTime) return;
            int tile = h.Pending.Tile;
            var node = s.Map.NodeAt(tile);
            h.Pending = null; h.GatherProgress = 0;
            if (node == null) return;
            float mult = 1f + HeroSystem.Skill(h, "gathering") / 60f;
            var parts = new List<string>();
            foreach (var kv in node.yields)
            {
                int n = Math.Max(1, (int)Math.Round(kv.Value * mult));
                h.Add(kv.Key, n);
                parts.Add($"+{n} {s.Content.ItemName(kv.Key)}");
            }
            s.State.WorldMods.Depleted[tile] = node.regrowMonths < 0 ? -1 : s.State.Months + node.regrowMonths;
            s.State.WorldMods.Revision++;
            HeroSystem.GainSkill(s, "gathering");
            s.Emit(new GameEvent { Type = GameEventType.Gathered, Pos = s.Map.TileCenter(tile), Value = tile, Text = string.Join(", ", parts) });
        }

        // ---------- Ремесло ----------
        public static bool CanCraft(GameSession s, RecipeDef r, bool workshop, out string reason)
        {
            reason = null;
            var h = s.State.Hero; var pn = s.PlayerNation;
            if (r.tech != null && (pn == null || !pn.Researched.Contains(r.tech))) { reason = "нужна технология"; return false; }
            if (s.Content.EraOrder(r.era) > s.Content.EraOrder(s.PlayerEra)) { reason = "не та эпоха"; return false; }
            if (workshop && WorkshopNearby(s) == null) { reason = "нужна Мастерская рядом"; return false; }
            foreach (var kv in r.inputs)
            {
                int have = h.Count(kv.Key) + (workshop && pn != null ? (int)pn.Get(kv.Key) : 0);
                if (have < kv.Value) { reason = $"нужно {kv.Value} {s.Content.ItemName(kv.Key)}"; return false; }
            }
            return true;
        }

        public static CityState WorkshopNearby(GameSession s)
        {
            var hero = s.HeroUnit; if (hero == null) return null;
            foreach (var c in s.State.Cities)
            {
                if (c.NationId != s.State.PlayerNationId) continue;
                foreach (var b in c.Buildings)
                    if (b.DefId == "workshop" && Vec2.Distance(b.Pos, hero.Pos) < 10f) return c;
            }
            return null;
        }

        /// <summary>Поле: вещь сразу, но низкого качества. Мастерская: качество эпохи, можно тратить запасы нации.</summary>
        public static ItemInstance Craft(GameSession s, string recipeId, bool workshop)
        {
            if (!s.Content.Recipes.TryGetValue(recipeId, out var r)) return null;
            if (!CanCraft(s, r, workshop, out var reason)) { s.Message($"Нельзя изготовить: {reason}."); return null; }
            var h = s.State.Hero; var pn = s.PlayerNation;
            foreach (var kv in r.inputs)
            {
                int fromHero = Math.Min(kv.Value, h.Count(kv.Key));
                h.Add(kv.Key, -fromHero);
                int rest = kv.Value - fromHero;
                if (rest > 0) pn.Add(kv.Key, -rest);
            }
            var def = s.Content.Items[r.result];
            if (def.resource) { h.Add(def.id, 1); s.Message($"Изготовлено: {def.name}"); return null; }
            string quality = workshop ? Better(s, r.workshopQuality ?? "settlement", CurrentQuality(s, true)) : (r.fieldQuality ?? "field");
            var item = RollItem(s, r.result, quality);
            h.Items.Add(item);
            AutoEquip(s, item);
            HeroSystem.GainSkill(s, "gathering", 0.5f);
            s.Emit(new GameEvent { Type = GameEventType.Crafted, Text = ItemLabel(s, item) });
            s.Message($"Изготовлено: {ItemLabel(s, item)}");
            return item;
        }

        static string Better(GameSession s, string a, string b)
        {
            int oa = s.Content.Qualities.TryGetValue(a, out var qa) ? qa.order : 0;
            int ob = s.Content.Qualities.TryGetValue(b, out var qb) ? qb.order : 0;
            return oa >= ob ? a : b;
        }

        // ---------- Конь ----------
        public static void TryTameHorse(GameSession s, PoiState poi)
        {
            var h = s.State.Hero;
            if (h.HasHorse) { s.Message("У вас уже есть конь."); return; }
            float chance = 0.3f + HeroSystem.Skill(h, "gathering") * 0.006f + HeroSystem.EffBody(s) * 0.03f + (h.Count("berries") >= 5 ? 0.1f : 0);
            if (h.Count("berries") >= 5) h.Add("berries", -5);
            if (s.Rng.Chance(chance))
            {
                h.HasHorse = true; h.Mounted = true;
                poi.Cleared = true;
                s.Message("Вы приручили коня! Клавиша R — сесть/спешиться.");
                Narrative.Chronicle.Add(s, "horse", null);
                HeroSystem.GainXp(s, 30);
            }
            else
            {
                var hero = s.HeroUnit;
                if (hero != null) { hero.Hp -= 8; s.Emit(new GameEvent { Type = GameEventType.Hit, UnitId = hero.Id, Value = 8, Pos = hero.Pos }); }
                s.Message($"Конь сбросил вас (шанс был {chance:P0}). Попробуйте ещё; ягоды помогают.");
            }
        }
    }
}
