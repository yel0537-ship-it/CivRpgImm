using System;
using System.Collections.Generic;
using Immortal.Core.Character;
using Immortal.Core.Content;
using Immortal.Core.Narrative;

namespace Immortal.Core.Civilization
{
    public struct CityTotals
    {
        public float Food, Housing, Health, Science, Influence, Defense, Loyalty, Production;
    }

    /// <summary>
    /// Поселения: основание, постройки в мире, физический рост ступенями, сытость/обеспеченность/здоровье,
    /// наместники и специализация, лояльность и автономия, новые города.
    /// </summary>
    public static class CitySystem
    {
        public static StageDef Stage(GameSession s, CityState c) => s.Content.Stages[MathUtil.Clamp(c.StageIndex, 0, s.Content.Stages.Count - 1)];

        public static CityState Found(GameSession s, NationState n, Vec2 pos, string name, int stageIndex, bool capital)
        {
            pos = s.Map.NearestWalkable(pos);
            var c = new CityState
            {
                Id = s.State.NewId("city"), Name = name, NationId = n?.Id, OriginalNationId = n?.Id, Pos = pos,
                StageIndex = stageIndex, FoundedMonths = s.State.Months, IsCapital = capital, Loyalty = 80, Population = 1
            };
            s.State.Cities.Add(c);
            if (n != null) n.CitiesFounded++;
            if (n != null && capital)
            {
                n.CapitalCityId = c.Id;
                if (n.OriginalCapitalId == null) n.OriginalCapitalId = c.Id;
            }
            PlaceBuilding(s, c, "fire_pit", free: true);
            if (n != null)
            {
                var other = s.NearestCity(pos, x => x != c && x.NationId == n.Id);
                if (other != null) BuildRoad(s, other, c);
            }
            s.Map.RebuildBuildingBlockers(s.Content, s.State);
            TerritorySystem.Recompute(s);
            s.Emit(new GameEvent { Type = GameEventType.CityFounded, Id = c.Id, Pos = pos, Text = name });
            Chronicle.Add(s, "city_founded", new Dictionary<string, string> { ["city"] = name, ["nation"] = n?.Name ?? "вольные люди" });
            return c;
        }

        public static void BuildRoad(GameSession s, CityState a, CityState b)
        {
            var path = s.Paths.FindPath(a.Pos, b.Pos, 40000);
            if (path == null || path.Count == 0) return;
            var road = new RoadState { FromCity = a.Id, ToCity = b.Id };
            Vec2 prev = a.Pos;
            road.Points.Add(prev);
            foreach (var p in path)
            {
                float d = Vec2.Distance(prev, p);
                int steps = Math.Max(1, (int)(d / s.Map.TileSize));
                for (int i = 1; i <= steps; i++)
                {
                    var q = Vec2.Lerp(prev, p, i / (float)steps);
                    int t = s.Map.TileAt(q);
                    var tid = s.Map.TerrainAt(t).id;
                    if (tid != "water" && tid != "mountain" && tid != "road") s.Map.SetTerrain(t, "road");
                }
                road.Points.Add(p);
                prev = p;
            }
            s.State.Roads.Add(road);
        }

        public static Vec2 SlotPos(CityState c, int slot)
        {
            float ang = slot * 2.39996f;
            float dist = 4.5f + (float)Math.Sqrt(slot) * 3.6f;
            return c.Pos + Vec2.FromAngle(ang) * dist;
        }

        public static bool Unlocked(GameSession s, NationState n, BuildingDef d)
        {
            if (n == null) return d.era == "primitive" && d.tech == null;
            if (s.Content.EraOrder(d.era) > s.Content.EraOrder(n.EraId)) return false;
            if (d.tech != null && !n.Researched.Contains(d.tech)) return false;
            return true;
        }

        public static bool CanBuild(GameSession s, CityState c, string id, out string reason)
        {
            reason = null;
            if (!s.Content.Buildings.TryGetValue(id, out var d)) { reason = "нет такой постройки"; return false; }
            var n = s.Nation(c.NationId);
            if (!Unlocked(s, n, d)) { reason = d.tech != null ? $"нужна технология «{s.Content.Tech[d.tech].name}»" : "не та эпоха"; return false; }
            if (d.minStage != null && c.StageIndex < s.Content.StageIndex[d.minStage]) { reason = $"нужна ступень «{s.Content.Stages[s.Content.StageIndex[d.minStage]].name}»"; return false; }
            if (c.CountBuilding(id) >= d.max) { reason = "уже построено"; return false; }
            if ((d.service == "ruler" && id != "fire_pit" && id != "chief_house" || d.megastructure) && !c.IsCapital) { reason = "только в столице"; return false; }
            int used = 0;
            foreach (var b in c.Buildings) { var bd0 = s.Content.Buildings[b.DefId]; if (b.DefId != "field" && bd0.housing <= 0) used++; }
            if (id != "field" && d.housing <= 0 && used >= Stage(s, c).slots) { reason = "нет места — нужна новая ступень"; return false; }
            if (d.housing > 0 && c.Buildings.Count - used >= Stage(s, c).slots) { reason = "жилью нет места — перестраивайте дома"; return false; }
            if (id == "field" && c.CountBuilding("field") >= 2 + (int)(c.Population / 5)) { reason = "полей достаточно для этого населения"; return false; }
            foreach (var kv in d.cost)
            {
                float have = n?.Get(kv.Key) ?? 0;
                if (have < kv.Value) { reason = $"нужно {kv.Value} {s.Content.ItemName(kv.Key)} (есть {have:0})"; return false; }
            }
            return true;
        }

        public static BuildingState PlaceBuilding(GameSession s, CityState c, string id, bool free = false)
        {
            var d = s.Content.Buildings[id];
            var n = s.Nation(c.NationId);
            if (!free)
            {
                if (!CanBuild(s, c, id, out _)) return null;
                foreach (var kv in d.cost) n.Add(kv.Key, -kv.Value);
            }
            int slot = 0;
            var used = new HashSet<int>();
            foreach (var b in c.Buildings) used.Add(b.Slot);
            Vec2 pos = c.Pos;
            if (id != "fire_pit")
            {
                for (slot = 1; slot < 200; slot++)
                {
                    if (used.Contains(slot)) continue;
                    pos = SlotPos(c, slot);
                    if (!s.Map.Walkable(pos)) continue;
                    bool clash = false;
                    foreach (var b in c.Buildings)
                    {
                        var bd = s.Content.Buildings[b.DefId];
                        if (Vec2.Distance(b.Pos, pos) < (bd.size + d.size) * 0.5f + 0.8f) { clash = true; break; }
                    }
                    if (!clash) break;
                }
            }
            var bs = new BuildingState { DefId = id, Slot = slot, Pos = pos, BuiltMonths = s.State.Months };
            c.Buildings.Add(bs);
            if (id == "field") ConvertToFields(s, pos, 2);
            if (d.convertTerrain != null) ConvertTerrain(s, pos, d.convertTerrain, c);
            s.Map.RebuildBuildingBlockers(s.Content, s.State);
            s.Emit(new GameEvent { Type = GameEventType.BuildingBuilt, Id = id, Pos = pos, Text = c.Id });
            if (d.megastructure)
            {
                Chronicle.Add(s, "megastructure", new Dictionary<string, string> { ["name"] = d.name, ["city"] = c.Name });
                EndingSystem.OnMegastructure(s, c, id);
            }
            return bs;
        }

        public static void ConvertToFields(GameSession s, Vec2 pos, int radiusTiles)
        {
            s.Map.TileXY(pos, out int cx, out int cy);
            for (int dy = -radiusTiles; dy <= radiusTiles; dy++)
            for (int dx = -radiusTiles; dx <= radiusTiles; dx++)
            {
                if (!s.Map.InBounds(cx + dx, cy + dy)) continue;
                int t = s.Map.TileIndex(cx + dx, cy + dy);
                var td = s.Map.TerrainAt(t);
                if (td.fertile) s.Map.SetTerrain(t, "field");
            }
        }

        /// <summary>Мир меняется руками людей: осушенное болото, вырубленный лес.</summary>
        public static int ConvertTerrain(GameSession s, Vec2 pos, TerrainConversion conv, CityState c)
        {
            if (!s.Content.Terrain.ContainsKey(conv.to)) return 0;
            s.Map.TileXY(pos, out int cx, out int cy);
            int n = 0, r = conv.radius;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r * r || !s.Map.InBounds(cx + dx, cy + dy)) continue;
                int t = s.Map.TileIndex(cx + dx, cy + dy);
                if (s.Map.TerrainAt(t).id != conv.from) continue;
                s.Map.SetTerrain(t, conv.to);
                n++;
            }
            if (n > 0)
            {
                s.Emit(new GameEvent { Type = GameEventType.WorldChanged, Pos = pos, Value = n });
                Chronicle.AddText(s, $"Год {s.Year}. У {c.Name} изменилась земля: {s.Content.Terrain[conv.from].name.ToLower()} уступили место — {s.Content.Terrain[conv.to].name.ToLower()} ({n} участков).", "land");
            }
            return n;
        }

        public static CityTotals Totals(GameSession s, CityState c)
        {
            var t = new CityTotals { Housing = s.Balance.city.baseHousing };
            foreach (var b in c.Buildings)
            {
                var d = s.Content.Buildings[b.DefId];
                t.Food += d.food; t.Housing += d.housing; t.Health += d.health; t.Science += d.science;
                t.Influence += d.influence; t.Defense += d.defense; t.Loyalty += d.loyalty; t.Production += d.production;
            }
            var n = s.Nation(c.NationId);
            float tb(string k) => n != null ? TechSystem.Bonus(s, n, k) : 0f;
            t.Food *= 1 + tb("food") + (c.Specialization == "economy" ? 0.15f : 0);
            t.Housing *= 1 + tb("housing");
            t.Science *= 1 + tb("science") + (c.Specialization == "science" ? 0.4f : 0);
            t.Influence *= 1 + tb("influence") + (c.Specialization == "admin" ? 0.3f : 0);
            t.Production *= 1 + tb("production") + (c.Specialization == "economy" ? 0.2f : 0);
            t.Loyalty += tb("loyalty") * 100 + (c.Specialization == "admin" ? 10 : 0);
            t.Defense *= 1 + tb("military") + (c.Specialization == "military" ? 0.4f : 0);
            return t;
        }

        /// <summary>Экономика города за долю месяца (вызывается раз в неделю и в прыжках).</summary>
        public static void Step(GameSession s, CityState c, float months)
        {
            var cb = s.Balance.city;
            var t = Totals(s, c);
            var n = s.Nation(c.NationId);
            float pop = Math.Max(1, c.Population);
            float prod = pop * cb.gatherPerPop + t.Food;
            float cons = pop * cb.foodPerPop;
            c.FoodStock += (prod - cons) * months;
            float cap = 30 + pop * 3 + c.CountBuilding("granary") * 60;
            if (c.FoodStock > cap) c.FoodStock = cap;
            c.Satiety = cons > 0 ? prod / cons : 1;
            c.Provision = t.Housing / pop;
            c.Health = MathUtil.Clamp(1f + t.Health * 0.05f - Math.Max(0, 1 - c.Provision) * 0.6f - (c.Hungry ? 0.3f : 0), 0.2f, 1.5f);
            if (c.FoodStock < 0 && n != null && n.Get("food") > 0)
            {
                float take = Math.Min(n.Get("food"), -c.FoodStock + pop * 0.5f);
                n.Add("food", -take); c.FoodStock += take;
            }
            c.Hungry = c.FoodStock < 0;

            if (c.Hungry)
            {
                c.FoodStock = 0;
                float loss = pop * cb.starvationRate * months;
                c.Population = Math.Max(1, c.Population - loss);
                c.Refugees += (int)Math.Ceiling(loss);
            }
            else if ((pop >= cb.selfGrowthPop || (n != null && NationPopulation(s, n) >= cb.selfGrowthPop * 1.5f)) && c.Satiety >= 1f && pop < t.Housing)
            {
                float maxPop = Stage(s, c).maxPop;
                float logistic = Math.Max(0f, 1f - pop / maxPop);
                if (logistic > 0) c.Population = Math.Min(Math.Min(t.Housing, maxPop), c.Population + Math.Max(0.15f, pop * cb.growthRate * c.Health * logistic) * months);
            }
            else if (c.Satiety >= 1.2f && pop < t.Housing)
            {
                c.Population = Math.Min(t.Housing, c.Population + 0.05f * months); // в малой общине тоже рождаются дети
            }

            float garrisonCap = pop * 0.3f + t.Defense * 4 + 4;
            c.Garrison = Math.Min(garrisonCap, c.Garrison + (0.4f + pop * 0.01f) * months * (c.Specialization == "military" ? 1.6f : 1f));

            if (n == null) return;
            float prodMul = 1f + t.Production * 0.05f;
            n.Add("wood", pop * cb.woodPerPop * prodMul * months);
            n.Add("stone", pop * cb.stonePerPop * prodMul * months);
            if (c.HasBuilding("mine")) n.Add("ore", (1.5f + pop * 0.02f) * months);
            if (c.HasBuilding("forge")) { float conv = Math.Min(n.Get("ore"), 2f * months); n.Add("ore", -conv); n.Add("bronze", conv * 0.8f); }
            if (c.FoodStock > cap * 0.8f) { float surplus = (c.FoodStock - cap * 0.8f) * 0.5f; c.FoodStock -= surplus; n.Add("food", surplus); }
            float science = (pop * cb.sciencePerPop + t.Science) * months;
            TechSystem.AddScience(s, n, science);
            if (s.Content.EraOrder(n.EraId) >= 1)
            {
                float inf = (pop * cb.influencePerPop + t.Influence) * months;
                n.Influence += inf; n.InfluenceTotal += inf;
            }
            n.MilitaryPool += pop * 0.012f * months * (1 + TechSystem.Bonus(s, n, "military")) * (c.Specialization == "military" ? 1.5f : 1f);
        }

        /// <summary>Ежемесячные решения: рост ступени, автостроительство наместника, лояльность, автономия, эмиграция.</summary>
        public static void StepMonth(GameSession s, CityState c)
        {
            TryAdvanceStage(s, c);
            var n = s.Nation(c.NationId);
            // К огню прибиваются странники, пока община мала.
            if (n != null && n.IsPlayer && c.IsCapital && c.HasBuilding("hut") && c.Population < s.Balance.city.selfGrowthPop
                && c.Satiety >= 1f && Totals(s, c).Housing > c.Population + 1 && s.Rng.Chance(0.25f))
            {
                c.Population += 1;
                if (!s.InTimeJump) s.Message($"К костру {c.Name} прибился странник.");
            }
            bool auto = n != null && (!n.IsPlayer || c.GovernorId != null || c.BuildQueue.Count > 0);
            if (auto) AutoBuild(s, c, n);
            UpdateLoyalty(s, c, n);
            Emigration(s, c, n);
        }

        public static bool TryAdvanceStage(GameSession s, CityState c)
        {
            if (c.StageIndex + 1 >= s.Content.Stages.Count) return false;
            var next = s.Content.Stages[c.StageIndex + 1];
            if (c.Population < next.minPop) return false;
            foreach (var b in next.requiredBuildings) if (!c.HasBuilding(b)) return false;
            var n = s.Nation(c.NationId);
            if (n == null) return false;
            foreach (var kv in next.cost) if (n.Get(kv.Key) < kv.Value) return false;
            foreach (var kv in next.cost) n.Add(kv.Key, -kv.Value);
            c.StageIndex++;
            s.Emit(new GameEvent { Type = GameEventType.StageChanged, Id = c.Id, Pos = c.Pos, Text = next.name });
            Chronicle.Add(s, "stage", new Dictionary<string, string> { ["city"] = c.Name, ["stage"] = next.name, ["nation"] = n.Name });
            if (n.IsPlayer)
            {
                s.Message($"{c.Name} вырос: теперь это {next.name}.");
                if (next.role != null) HeroSystem.SetRole(s, next.role);
                EventSystem.Fire(s, "stage_" + next.id, new DialogueState { CityId = c.Id });
            }
            TerritorySystem.Recompute(s);
            EraSystem.Check(s, n);
            return true;
        }

        public static string NextHousing(GameSession s, NationState n)
        {
            foreach (var id in new[] { "insula", "house", "hut" })
                if (s.Content.Buildings.TryGetValue(id, out var d) && Unlocked(s, n, d)) return id;
            return "hut";
        }

        static void AutoBuild(GameSession s, CityState c, NationState n)
        {
            string pick = null;
            if (c.BuildQueue.Count > 0)
            {
                if (CanBuild(s, c, c.BuildQueue[0], out _)) { pick = c.BuildQueue[0]; c.BuildQueue.RemoveAt(0); }
                else if (n.IsPlayer) return; // ждём ресурсов для очереди
            }
            if (pick == null)
            {
                var t = Totals(s, c);
                bool urgentHousing = t.Housing - c.Population < 2;
                bool urgentFood = c.Satiety < 1.08f;
                var urgent = new List<string>();
                if (urgentHousing && TryUpgradeHousing(s, c, n)) return;
                if (urgentHousing) urgent.Add(NextHousing(s, n));
                if (urgentFood) { urgent.Add("field"); urgent.Add("pen"); urgent.Add("granary"); }
                foreach (var id in urgent) if (CanBuild(s, c, id, out _)) { pick = id; break; }
                if (pick == null)
                {
                    // Требования следующей ступени: строим или копим на них, не распыляясь.
                    bool reserve = false;
                    if (c.StageIndex + 1 < s.Content.Stages.Count)
                    {
                        var next = s.Content.Stages[c.StageIndex + 1];
                        bool allBuilt = true;
                        foreach (var b in next.requiredBuildings)
                        {
                            if (c.HasBuilding(b)) continue;
                            allBuilt = false;
                            var bd = s.Content.Buildings[b];
                            bool reachable = Unlocked(s, n, bd) && (bd.minStage == null || c.StageIndex >= s.Content.StageIndex[bd.minStage]);
                            if (!reachable) continue;
                            if (CanBuild(s, c, b, out _)) { pick = b; break; }
                            reserve = true;
                            // Нечем платить: сначала строим то, что производит недостающее (руда → рудник, бронза → литейня).
                            foreach (var kv in bd.cost)
                            {
                                if (n.Get(kv.Key) >= kv.Value) continue;
                                string producer = kv.Key == "ore" ? "mine" : kv.Key == "bronze" ? (c.HasBuilding("mine") ? "forge" : "mine") : null;
                                if (producer != null && !c.HasBuilding(producer) && CanBuild(s, c, producer, out _)) { pick = producer; break; }
                            }
                            if (pick != null) break;
                        }
                        if (allBuilt && c.Population >= next.minPop)
                            foreach (var kv in next.cost) if (n.Get(kv.Key) < kv.Value) reserve = true;
                    }
                    if (pick == null && !reserve)
                    {
                        var cands = new List<string>();
                        foreach (var d in s.Content.BuildingList)
                            if (d.specialization == c.Specialization && !d.megastructure) cands.Add(d.id);
                        foreach (var id in new[] { "storage", "workshop", "shrine", "granary", "school", "barracks", "mine", "forge", "market", "walls", "palisade" })
                            cands.Add(id);
                        if (!n.IsPlayer && c.IsCapital) cands.Add("laboratory");
                        foreach (var id in cands)
                            if (CanBuild(s, c, id, out _)) { pick = id; break; }
                    }
                }
            }
            if (pick != null) PlaceBuilding(s, c, pick);
        }

        /// <summary>Шалаши перестраиваются в дома, дома — в многоэтажные: город растёт на месте.</summary>
        static bool TryUpgradeHousing(GameSession s, CityState c, NationState n)
        {
            string best = NextHousing(s, n);
            var bd = s.Content.Buildings[best];
            foreach (var b in c.Buildings)
            {
                var cur = s.Content.Buildings[b.DefId];
                if (cur.housing <= 0 || cur.housing >= bd.housing || c.CountBuilding(b.DefId) < 4) continue;
                foreach (var kv in bd.cost) if (n.Get(kv.Key) < kv.Value) return false;
                foreach (var kv in bd.cost) n.Add(kv.Key, -kv.Value);
                b.DefId = best; b.BuiltMonths = s.State.Months;
                s.Map.RebuildBuildingBlockers(s.Content, s.State);
                s.Emit(new GameEvent { Type = GameEventType.BuildingBuilt, Id = best, Pos = b.Pos, Text = c.Id });
                return true;
            }
            return false;
        }

        static void UpdateLoyalty(GameSession s, CityState c, NationState n)
        {
            var cb = s.Balance.city;
            if (n == null) { c.Loyalty = MathUtil.Clamp(c.Loyalty + 0.5f, 0, 100); return; }
            var t = Totals(s, c);
            float target = 72 + t.Loyalty;
            var cap = s.Capital(n);
            if (c.IsCapital || cap == c) target += 15;
            else
            {
                target += CompanionSystem.GovernorLoyaltyBonus(s, c);
                if (cap != null) target -= Vec2.Distance(cap.Pos, c.Pos) * cb.loyaltyDistancePenalty;
            }
            if (c.Hungry) target -= 20;
            if (c.Provision < 1) target -= (1 - c.Provision) * 30;
            if (n.IsPlayer)
            {
                if (s.State.Narrative.Flags.Contains("taboo_broken")) target -= 8;
                if (s.State.Narrative.Flags.Contains("hero_absent")) target -= 15;
                if (s.State.Narrative.Flags.Contains("inequality_known")) target -= 6;
            }
            if (!n.Leader.Alive && !c.IsCapital) target -= 45; // регентство: окраины уязвимы
            c.Loyalty = MathUtil.Clamp(c.Loyalty + (target - c.Loyalty) * 0.15f, 0, 100);
            UpdateAutonomy(s, c, n);
        }

        static void UpdateAutonomy(GameSession s, CityState c, NationState n)
        {
            var cb = s.Balance.city;
            string prev = c.AutonomyState;
            if (c.IsCapital) { c.AutonomyState = Autonomy.Loyal; return; }
            if (c.Loyalty > cb.autonomyVulnerable + 10) c.AutonomyState = Autonomy.Loyal;
            else if (c.AutonomyState == Autonomy.Loyal && c.Loyalty < cb.autonomyVulnerable) c.AutonomyState = Autonomy.Vulnerable;
            else if (c.AutonomyState == Autonomy.Vulnerable && c.Loyalty < cb.autonomyEntrenched) c.AutonomyState = Autonomy.Entrenched;
            else if (c.AutonomyState == Autonomy.Entrenched && c.Loyalty < cb.autonomyLegitimate) c.AutonomyState = Autonomy.Legitimate;
            if (prev == c.AutonomyState) return;

            if (n.IsPlayer)
            {
                if (c.AutonomyState == Autonomy.Vulnerable) s.Message($"{c.Name} колеблется: лояльность падает.");
                if (c.AutonomyState == Autonomy.Entrenched)
                {
                    var g = s.Companion(c.GovernorId);
                    if (g != null && g.Relation < s.Balance.companions.rebellionRelation)
                        EventSystem.Fire(s, "rebellion", new DialogueState { CityId = c.Id, CompanionId = g.Id });
                    else s.Message($"{c.Name} закрепляет самоуправление.");
                }
            }
            if (c.AutonomyState == Autonomy.Legitimate) Secede(s, c);
        }

        public static void Secede(GameSession s, CityState c)
        {
            var n = s.Nation(c.NationId);
            c.NationId = null; c.IsCapital = false; c.AutonomyState = Autonomy.Legitimate; c.Loyalty = 60;
            var g = s.Companion(c.GovernorId);
            if (g != null) { g.Role = CompanionRoles.Rebel; g.AssignmentId = c.Id; c.RebelLeaderId = g.Id; }
            Chronicle.Add(s, "secession", new Dictionary<string, string> { ["city"] = c.Name, ["nation"] = n?.Name ?? "", ["leader"] = g?.Name ?? "горожане" });
            if (n != null && n.IsPlayer) s.Message($"{c.Name} отделился и стал вольным городом-государством!");
            TerritorySystem.Recompute(s);
        }

        public static void JoinNation(GameSession s, CityState c, NationState n, float loyalty)
        {
            var old = s.Nation(c.NationId);
            c.NationId = n.Id; c.Loyalty = loyalty; c.AutonomyState = Autonomy.Loyal;
            if (old != null && old.CapitalCityId == c.Id) NationSystem.ReassignCapital(s, old);
            if (c.RebelLeaderId != null) { var g = s.Companion(c.RebelLeaderId); if (g != null && n.IsPlayer) { g.Role = CompanionRoles.Governor; g.AssignmentId = c.Id; c.GovernorId = g.Id; } c.RebelLeaderId = null; }
            TerritorySystem.Recompute(s);
        }

        static void Emigration(GameSession s, CityState c, NationState n)
        {
            var cb = s.Balance.city;
            if (n == null || c.Population < 30) return;
            var t = Totals(s, c);
            bool crowded = c.Population > t.Housing * cb.emigrationOvercrowd || (c.Population >= t.Housing - 0.5f && c.Loyalty < 60);
            if (!crowded || s.Rng.NextFloat() > 0.08f) return;
            if (CityCount(s, n) + ArmySystem.SettlersInFlight(s, n) >= 6) return;
            ArmySystem.SendSettlers(s, n, c, null, spontaneous: true);
        }

        public static int CityCount(GameSession s, NationState n)
        {
            int k = 0; foreach (var c in s.State.Cities) if (c.NationId == n.Id) k++; return k;
        }

        public static float NationPopulation(GameSession s, NationState n)
        {
            float p = 0; foreach (var c in s.State.Cities) if (c.NationId == n.Id) p += c.Population; return p;
        }
    }

    /// <summary>Владения по клеткам: города, крепости, аванпосты.</summary>
    public static class TerritorySystem
    {
        public static void Recompute(GameSession s)
        {
            int cell = s.Balance.territoryCell;
            int nx = (s.Map.Size + cell - 1) / cell;
            if (s.State.TerritoryOwner == null || s.State.TerritoryOwner.Length != nx * nx) s.State.TerritoryOwner = new byte[nx * nx];
            s.State.TerritoryCellsX = nx;
            var best = new float[nx * nx];
            Array.Clear(s.State.TerritoryOwner, 0, s.State.TerritoryOwner.Length);
            float cellW = cell * s.Map.TileSize;

            void Claim(Vec2 pos, float radius, int nationIndex)
            {
                int cx = (int)(pos.x / cellW), cy = (int)(pos.y / cellW);
                int r = (int)Math.Ceiling(radius / cellW);
                for (int y = cy - r; y <= cy + r; y++)
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (x < 0 || y < 0 || x >= nx || y >= nx) continue;
                    var center = new Vec2((x + 0.5f) * cellW, (y + 0.5f) * cellW);
                    float strength = radius - Vec2.Distance(center, pos);
                    int i = y * nx + x;
                    if (strength > 0 && strength > best[i]) { best[i] = strength; s.State.TerritoryOwner[i] = (byte)(nationIndex + 1); }
                }
            }

            foreach (var c in s.State.Cities)
            {
                if (c.NationId == null) continue;
                int ni = s.State.Nations.FindIndex(n => n.Id == c.NationId);
                if (ni < 0) continue;
                var st = CitySystem.Stage(s, c);
                Claim(c.Pos, st.influenceRadius + (float)Math.Sqrt(c.Population) * 1.5f, ni);
            }
            foreach (var p in s.State.Pois)
            {
                if (p.OwnerNationId == null) continue;
                int ni = s.State.Nations.FindIndex(n => n.Id == p.OwnerNationId);
                if (ni >= 0) Claim(p.Pos, 30f, ni);
            }
        }

        public static NationState OwnerAt(GameSession s, Vec2 p)
        {
            if (s.State.TerritoryOwner == null) return null;
            float cellW = s.Balance.territoryCell * s.Map.TileSize;
            int x = (int)(p.x / cellW), y = (int)(p.y / cellW), nx = s.State.TerritoryCellsX;
            if (x < 0 || y < 0 || x >= nx || y >= nx) return null;
            int o = s.State.TerritoryOwner[y * nx + x];
            return o == 0 ? null : s.State.Nations[o - 1];
        }
    }
}
