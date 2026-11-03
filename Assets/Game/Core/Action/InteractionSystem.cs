using System;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using Immortal.Core.Narrative;

namespace Immortal.Core.Action
{
    /// <summary>Подойти и взаимодействовать: NPC, спутники, лидеры, точки интереса, постройки, ресурсы.</summary>
    public static class InteractionSystem
    {
        public static void Begin(GameSession s, PendingAction a)
        {
            var hero = s.HeroUnit; if (hero == null) return;
            var h = s.State.Hero;
            h.Pending = a; h.GatherProgress = 0;
            hero.TargetId = 0;
            if (Vec2.Distance(hero.Pos, a.Pos) > s.Balance.hero.interactRange) MovementSystem.SetPath(s, hero, a.Pos);
        }

        public static void Tick(GameSession s)
        {
            var h = s.State.Hero; var hero = s.HeroUnit;
            if (h.Pending == null || hero == null || hero.Ai == AiState.Dead) return;
            var a = h.Pending;
            if (a.Kind == "gather")
            {
                if (hero.Path == null && Vec2.Distance(hero.Pos, a.Pos) > s.Balance.hero.interactRange + 0.8f) MovementSystem.SetPath(s, hero, a.Pos);
                return;
            }
            float range = s.Balance.hero.interactRange;
            if (a.Kind == "unit")
            {
                var u = s.FindUnit(a.UnitId);
                if (u == null || u.Ai == AiState.Dead) { h.Pending = null; return; }
                a.Pos = u.Pos; range += u.Radius;
                if (s.State.Tick % 10 == 0 && Vec2.Distance(hero.Pos, a.Pos) > range) MovementSystem.SetPath(s, hero, a.Pos);
            }
            else if (a.Kind == "poi") range = Math.Max(range, Math.Min(4f, (s.Poi(a.Id)?.Radius ?? 2f)));
            else if (a.Kind == "building") range += 1.5f;
            if (Vec2.Distance(hero.Pos, a.Pos) > range)
            {
                if (hero.Path == null) MovementSystem.SetPath(s, hero, a.Pos);
                return;
            }
            hero.Path = null;
            h.Pending = null;
            switch (a.Kind)
            {
                case "unit": InteractUnit(s, s.FindUnit(a.UnitId)); break;
                case "poi": InteractPoi(s, s.Poi(a.Id)); break;
                case "building": InteractBuilding(s, s.City(a.Id), a.Tile); break;
            }
        }

        public static void InteractUnit(GameSession s, Unit u)
        {
            if (u == null) return;
            var hero = s.HeroUnit;
            if (u.Kind == UnitKind.Enemy || u.Kind == UnitKind.Spirit || u.Kind == UnitKind.Animal || u.Kind == UnitKind.Soldier || (u.Team == Teams.Wild))
            {
                if (hero != null) hero.TargetId = u.Id;
                return;
            }
            if (u.Kind == UnitKind.Leader)
            {
                if (hero != null && s.IsHostile(u.Team, hero.Team)) { hero.TargetId = u.Id; return; }
                DialogueEngine.Start(s, "dlg_leader", new DialogueState { NationId = u.NationId });
                return;
            }
            if (u.CompanionId != null)
            {
                DialogueEngine.Start(s, "dlg_companion", new DialogueState { CompanionId = u.CompanionId });
                return;
            }
            var npc = s.Npc(u.NpcId);
            if (npc == null) return;
            if (npc.Hostile) { if (hero != null) hero.TargetId = u.Id; return; }
            if (npc.IsDescendant && !npc.Met)
            {
                npc.Met = true;
                DialogueEngine.Start(s, "dlg_descendant", new DialogueState { NpcId = npc.Id, CompanionId = npc.AncestorCompanionId });
                return;
            }
            var def = RecruitmentSystem.Def(s, npc);
            DialogueEngine.Start(s, def?.dialogue ?? "dlg_recruit", new DialogueState { NpcId = npc.Id });
        }

        public static void InteractPoi(GameSession s, PoiState p)
        {
            if (p == null) return;
            var def = s.Content.Poi[p.DefId];
            var h = s.State.Hero;
            switch (def.kind)
            {
                case "ruins":
                case "cache":
                    if (p.Visited) { s.Message($"{p.Name}: здесь больше ничего нет."); return; }
                    p.Visited = true; p.Cleared = true;
                    foreach (var kv in def.rewards) LootSystem.Drop(s, p.Pos, kv.Key, kv.Value);
                    if (def.lootTable != null) LootSystem.DropTable(s, def.lootTable, p.Pos);
                    if (def.science > 0)
                    {
                        if (s.PlayerNation != null) { TechSystem.AddScience(s, s.PlayerNation, def.science); s.Message($"Находки ускорили открытия (+{def.science:0} науки)."); }
                        else { h.Add("lore", (int)Math.Max(1, def.science / 10)); s.Message("Вы унесли записи древних. Сдайте их в своём лагере."); }
                    }
                    HeroSystem.GainXp(s, (int)def.xp);
                    if (def.memory != null) HeroSystem.AddMemory(s, def.memory);
                    TechSystem.TryRandomDiscovery(s, 0.2f);
                    if (def.dialogue != null) DialogueEngine.Start(s, def.dialogue, new DialogueState { PoiId = p.Id });
                    else s.Message($"{p.Name}: {def.description}");
                    break;
                case "horses":
                    if (p.Cleared && h.HasHorse) { s.Message("Табун ушёл."); return; }
                    LootSystem.TryTameHorse(s, p);
                    break;
                case "grave":
                    DialogueEngine.Start(s, "dlg_grave", new DialogueState { CompanionId = p.Data, PoiId = p.Id });
                    break;
                case "oddity":
                case "strange":
                    if (!p.Visited) { p.Visited = true; HeroSystem.GainXp(s, (int)def.xp); if (def.memory != null) HeroSystem.AddMemory(s, def.memory); }
                    DialogueEngine.Start(s, def.dialogue ?? "dlg_oddity", new DialogueState { PoiId = p.Id });
                    break;
                case "zone":
                    s.Message($"{p.Name}: {def.description}");
                    break;
                default:
                    s.Message(p.Cleared ? $"{p.Name}: здесь тихо." : $"{p.Name}: {def.description}");
                    break;
            }
        }

        public static void InteractBuilding(GameSession s, CityState c, int slot)
        {
            if (c == null) return;
            BuildingState b = null;
            foreach (var x in c.Buildings) if (x.Slot == slot) { b = x; break; }
            if (b == null) return;
            var def = s.Content.Buildings[b.DefId];
            if (c.NationId != s.State.PlayerNationId)
            {
                var n = s.Nation(c.NationId);
                s.Message($"{def.name} — город {c.Name} ({n?.Name ?? "вольный город"}).");
                if (c.NationId == null || c.AutonomyState != Autonomy.Loyal)
                    s.Emit(new GameEvent { Type = GameEventType.OpenScreen, Id = "persuade", Text = c.Id });
                return;
            }
            switch (def.service)
            {
                case "storage":
                    Deposit(s, c);
                    break;
                case "ruler":
                    Deposit(s, c);
                    s.Emit(new GameEvent { Type = GameEventType.OpenScreen, Id = "ruler", Text = c.Id });
                    break;
                case "workshop":
                    s.Emit(new GameEvent { Type = GameEventType.OpenScreen, Id = "craft_workshop", Text = c.Id });
                    break;
                default:
                    s.Emit(new GameEvent { Type = GameEventType.OpenScreen, Id = "interior", Text = b.DefId + "|" + c.Id });
                    s.Message($"{def.name}: {def.description}");
                    break;
            }
        }

        /// <summary>Сдать добытое в общий запас нации (записи древних превращаются в науку).</summary>
        public static void Deposit(GameSession s, CityState c)
        {
            var pn = s.PlayerNation; var h = s.State.Hero;
            if (pn == null) return;
            int total = 0;
            var keys = new System.Collections.Generic.List<string>(h.Inventory.Keys);
            foreach (var k in keys)
            {
                if (!s.Content.Items.TryGetValue(k, out var d) || !d.resource) continue;
                int n = h.Count(k);
                if (k == "lore") { TechSystem.AddScience(s, pn, n * 10); h.Add(k, -n); total += n; continue; }
                pn.Add(k == "berries" || k == "meat" ? "food" : k, n); h.Add(k, -n); total += n;
            }
            if (total > 0) s.Message($"Сдано в запасы ({c.Name}): {total} ед.");
        }
    }
}
