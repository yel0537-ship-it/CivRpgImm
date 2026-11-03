using System;
using System.Collections.Generic;
using Immortal.Core.Action;
using Immortal.Core.Character;
using Immortal.Core.Content;
using Immortal.Core.Narrative;

namespace Immortal.Core.Civilization
{
    /// <summary>
    /// Армии, поселенцы и налётчики идут по миру настоящими путями. Вдали от героя бои считаются упрощённо;
    /// если герой рядом — отряды разворачиваются в солдат и битва идёт в реальном времени вокруг него.
    /// </summary>
    public static class ArmySystem
    {
        public static ArmyState Create(GameSession s, NationState n, CityState home, float strength, string kind)
        {
            var a = new ArmyState
            {
                Id = s.State.NewId("army"), NationId = n?.Id, HomeCityId = home?.Id, Pos = home != null ? home.Pos + new Vec2(3, 3) : Vec2.Zero,
                Strength = strength, Kind = kind
            };
            s.State.Armies.Add(a);
            return a;
        }

        public static void Order(GameSession s, ArmyState a, CityState target)
        {
            a.TargetCityId = target?.Id;
            if (target == null) { a.State = ArmyStates.Idle; return; }
            MoveTo(s, a, target.Pos);
        }

        static void MoveTo(GameSession s, ArmyState a, Vec2 dest)
        {
            a.Destination = dest;
            a.Path = s.Paths.FindPath(a.Pos, dest, 30000) ?? new List<Vec2> { dest };
            a.PathIndex = 0;
            a.State = ArmyStates.Marching;
        }

        public static void ReturnHome(GameSession s, ArmyState a)
        {
            var home = s.City(a.HomeCityId);
            a.TargetCityId = null;
            if (home == null) { a.Strength = 0; return; }
            MoveTo(s, a, home.Pos);
            a.State = ArmyStates.Returning;
        }

        public static void SendSettlers(GameSession s, NationState n, CityState from, Vec2? site, bool spontaneous)
        {
            float pop = s.Balance.city.settlerCostPop;
            if (from.Population < pop + 8) return;
            Vec2 dest = site ?? FindSite(s, from.Pos);
            if (dest.x < 0) return;
            from.Population -= pop;
            var a = Create(s, n, from, pop, "settlers");
            a.Destination = dest;
            MoveTo(s, a, dest);
            if (spontaneous) a.GeneralId = "spontaneous";
            if (n.IsPlayer) s.Message(spontaneous ? $"Часть жителей {from.Name} уходит искать новое место." : "Поселенцы выступили.");
        }

        public static int SettlersInFlight(GameSession s, NationState n)
        {
            int k = 0; foreach (var a in s.State.Armies) if (a.Kind == "settlers" && a.NationId == n.Id) k++; return k;
        }

        static string ToRoman(int v) => v switch { 2 => "II", 3 => "III", 4 => "IV", 5 => "V", _ => v.ToString() };

        public static Vec2 FindSite(GameSession s, Vec2 near)
        {
            for (int i = 0; i < 40; i++)
            {
                var p = near + Vec2.FromAngle(s.Rng.Range(0, 6.283f)) * s.Rng.Range(60f, 120f);
                p = s.Map.Clamp(p);
                if (!s.Map.Walkable(p)) continue;
                var nc = s.NearestCity(p);
                if (nc != null && Vec2.Distance(nc.Pos, p) < 55f) continue;
                if (ZoneSystem.ZoneAt(s, p) != null) continue;
                var owner = TerritorySystem.OwnerAt(s, p);
                if (owner != null && Vec2.Distance(s.Capital(owner)?.Pos ?? p, p) < 40f) continue;
                return p;
            }
            return new Vec2(-1, -1);
        }

        // ---------- Движение ----------
        public static void TickRealtime(GameSession s, float dt)
        {
            var hero = s.HeroUnit;
            var list = s.State.Armies;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var a = list[i];
                if (!a.Expanded) Move(s, a, s.Balance.war.armySpeed * dt);
                if (hero != null) UpdateExpansion(s, a, hero);
                if (!a.Expanded) ResolveContacts(s, a, dt / s.Balance.secondsPerMonth);
            }
            list.RemoveAll(a => a.Strength <= 0.01f && !a.Expanded);
        }

        static void Move(GameSession s, ArmyState a, float dist)
        {
            if (a.State != ArmyStates.Marching && a.State != ArmyStates.Returning) return;
            while (dist > 0 && a.Path != null && a.PathIndex < a.Path.Count)
            {
                var t = a.Path[a.PathIndex];
                float d = Vec2.Distance(a.Pos, t);
                if (d <= dist) { a.Pos = t; a.PathIndex++; dist -= d; }
                else { a.Pos = a.Pos + (t - a.Pos) * (dist / d); dist = 0; }
            }
            if (a.Path == null || a.PathIndex >= a.Path.Count) Arrive(s, a);
        }

        static void Arrive(GameSession s, ArmyState a)
        {
            var n = s.Nation(a.NationId);
            if (a.Kind == "settlers")
            {
                if (n == null) { a.Strength = 0; return; }
                var names = s.Content.Nations.TryGetValue(n.DefId, out var nd) ? nd.cityNames : new List<string>();
                int idx = n.CitiesFounded;
                string name = names.Count == 0 ? $"Город {idx + 1}" : idx < names.Count ? names[idx] : $"{names[idx % names.Count]} {ToRoman(idx / names.Count + 1)}";
                var c = CitySystem.Found(s, n, a.Destination, name, 1, false);
                c.Population = a.Strength;
                c.Loyalty = a.GeneralId == "spontaneous" ? 52 : 70;
                CitySystem.PlaceBuilding(s, c, "hut", free: true);
                CitySystem.PlaceBuilding(s, c, "field", free: true);
                a.Strength = 0;
                return;
            }
            if (a.State == ArmyStates.Returning)
            {
                var home = s.City(a.HomeCityId);
                if (home != null && home.NationId == a.NationId) home.Garrison += a.Strength * 0.5f;
                if (n != null) n.MilitaryPool += a.Strength * 0.5f;
                a.Strength = 0;
                return;
            }
            var target = s.City(a.TargetCityId);
            if (target == null || target.NationId == a.NationId) { ReturnHome(s, a); return; }
            a.State = ArmyStates.Sieging;
        }

        /// <summary>Упрощённое сражение: столкновения армий и осада.</summary>
        static void ResolveContacts(GameSession s, ArmyState a, float months)
        {
            var wb = s.Balance.war;
            if (a.Strength <= 0) return;
            foreach (var b in s.State.Armies)
            {
                if (b == a || b.Strength <= 0 || b.Expanded || b.Kind == "settlers" || a.Kind == "settlers") continue;
                if (!Hostile(s, a, b) || Vec2.Distance(a.Pos, b.Pos) > 6f) continue;
                float da = b.Strength * wb.attritionRate * months * 4 * Bonus(s, b);
                float db = a.Strength * wb.attritionRate * months * 4 * Bonus(s, a);
                a.Strength -= da; b.Strength -= db;
                a.State = b.State = ArmyStates.Battle;
                if (a.Strength <= 0) Destroyed(s, a, b);
                if (b.Strength <= 0) Destroyed(s, b, a);
            }
            if (a.State == ArmyStates.Battle && a.Strength > 0)
            {
                bool still = false;
                foreach (var b in s.State.Armies) if (b != a && b.Strength > 0 && Hostile(s, a, b) && Vec2.Distance(a.Pos, b.Pos) <= 6f) still = true;
                if (!still) { if (a.TargetCityId != null) MoveTo(s, a, s.City(a.TargetCityId)?.Pos ?? a.Pos); else ReturnHome(s, a); }
            }
            if (a.State == ArmyStates.Sieging)
            {
                var c = s.City(a.TargetCityId);
                if (c == null || c.NationId == a.NationId) { ReturnHome(s, a); return; }
                float defense = c.Garrison + CitySystem.Totals(s, c).Defense * 3;
                c.Garrison -= a.Strength * wb.siegeRate * months * Bonus(s, a);
                a.Strength -= defense * wb.siegeRate * months * 0.8f;
                if (a.Kind == "raiders" && c.Garrison > 0)
                {
                    c.FoodStock = Math.Max(0, c.FoodStock - 10 * months * 10);
                }
                if (c.Garrison <= 0) Capture(s, a, c);
                else if (a.Strength <= 0) Destroyed(s, a, null);
            }
        }

        static float Bonus(GameSession s, ArmyState a)
        {
            var n = s.Nation(a.NationId);
            float b = 1f;
            if (n != null) b += TechSystem.Bonus(s, n, "military") + s.Content.EraOrder(n.EraId) * 0.15f;
            var g = s.Companion(a.GeneralId);
            if (g != null && g.Alive) b += g.Combat * 0.01f;
            return b;
        }

        public static bool Hostile(GameSession s, ArmyState a, ArmyState b)
        {
            if (a.Kind == "raiders" || b.Kind == "raiders") return a.Kind != b.Kind;
            return Diplomacy.AtWar(s, a.NationId, b.NationId);
        }

        static void Destroyed(GameSession s, ArmyState a, ArmyState by)
        {
            a.Strength = 0;
            var n = s.Nation(a.NationId);
            if (n != null && (n.IsPlayer || (by != null && s.Nation(by.NationId)?.IsPlayer == true)))
                Chronicle.Add(s, "battle", new Dictionary<string, string> { ["loser"] = n.Name, ["winner"] = s.Nation(by?.NationId)?.Name ?? "защитники" });
        }

        static void Capture(GameSession s, ArmyState a, CityState c)
        {
            var att = s.Nation(a.NationId);
            if (a.Kind == "raiders" || att == null)
            {
                c.Population = Math.Max(1, c.Population * 0.85f);
                c.FoodStock = 0;
                c.Garrison = 2;
                a.Strength = 0;
                if (c.NationId == s.State.PlayerNationId) s.Message($"Разбойники разграбили {c.Name}!");
                return;
            }
            var old = s.Nation(c.NationId);
            bool wasCapital = old != null && old.CapitalCityId == c.Id;
            CitySystem.JoinNation(s, c, att, 40);
            c.Garrison = a.Strength * 0.5f;
            a.Strength *= 0.5f;
            s.Emit(new GameEvent { Type = GameEventType.CityCaptured, Id = c.Id, Text = c.Name });
            Chronicle.Add(s, "city_captured", new Dictionary<string, string> { ["city"] = c.Name, ["nation"] = att.Name, ["old"] = old?.Name ?? "вольные" });
            if (old != null && old.IsPlayer) s.Message($"{att.Name} захватили {c.Name}!");
            if (att.IsPlayer) s.Message($"Вы взяли {c.Name}!");
            if (wasCapital && old != null && old.Leader.Alive && !old.IsPlayer) NationSystem.OnLeaderKilled(s, old.Id, att.IsPlayer);
            ReturnHome(s, a);
            EndingSystem.CheckMilitary(s);
        }

        // ---------- Битвы с участием героя ----------
        static void UpdateExpansion(GameSession s, ArmyState a, Unit hero)
        {
            var wb = s.Balance.war;
            bool fighting = a.State == ArmyStates.Battle || a.State == ArmyStates.Sieging;
            bool near = Vec2.Distance(a.Pos, hero.Pos) < wb.expandRadius;
            if (a.Kind == "settlers") { a.Expanded = false; return; }
            if (near && fighting && !s.State.GameOver)
            {
                if (!a.Expanded) { a.Expanded = true; s.Emit(new GameEvent { Type = GameEventType.BattleStarted, Pos = a.Pos, Text = s.Nation(a.NationId)?.Name ?? "Разбойники" }); s.Message("Рядом идёт битва!"); }
                TopUp(s, a, a.Id, a.Strength, ArmyTeam(s, a), a.Pos);
                if (a.State == ArmyStates.Sieging)
                {
                    var c = s.City(a.TargetCityId);
                    if (c != null)
                    {
                        TopUp(s, a, "garrison:" + c.Id, c.Garrison, s.NationTeam(c.NationId), c.Pos);
                        if (c.Garrison <= 0.01f) { Collapse(s, a); Capture(s, a, c); return; }
                    }
                }
                if (a.Strength <= 0.01f) { Collapse(s, a); a.Strength = 0; }
            }
            else if (a.Expanded) Collapse(s, a);
        }

        static int ArmyTeam(GameSession s, ArmyState a) => a.Kind == "raiders" || a.NationId == null ? Teams.Wild : s.NationTeam(a.NationId);

        static void TopUp(GameSession s, ArmyState a, string armyKey, float strength, int team, Vec2 center)
        {
            var wb = s.Balance.war;
            int want = Math.Min(wb.maxSoldiersPerSide, (int)Math.Ceiling(strength / wb.soldiersPerUnit));
            int have = 0;
            foreach (var u in s.State.Units) if (u.ArmyId == armyKey && u.Ai != AiState.Dead) have++;
            var n = s.Nation(a.NationId);
            string def = a.Kind == "raiders" || team == Teams.Wild ? "raider" : (n != null && s.Content.EraOrder(n.EraId) >= 1 ? "soldier_bronze" : "soldier");
            if (armyKey.StartsWith("garrison:"))
            {
                var c = s.City(armyKey.Substring(9));
                var cn = s.Nation(c?.NationId);
                def = cn != null && s.Content.EraOrder(cn.EraId) >= 1 ? "soldier_bronze" : "soldier";
            }
            for (int i = have; i < want; i++)
            {
                var pos = center + Vec2.FromAngle(s.Rng.Range(0, 6.283f)) * s.Rng.Range(2f, 6f);
                var u = SpawnSystem.CreateEnemy(s, def, pos, team);
                u.Kind = UnitKind.Soldier; u.ArmyId = armyKey; u.AggroRange = 25f; u.Home = center;
                var nat = s.NationByTeam(team);
                u.NationId = team == Teams.Player ? s.State.PlayerNationId : nat?.Id;
            }
        }

        public static void Collapse(GameSession s, ArmyState a)
        {
            var units = s.State.Units;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                if (u.ArmyId == a.Id || (a.TargetCityId != null && u.ArmyId == "garrison:" + a.TargetCityId)) s.RemoveUnit(u);
            }
            a.Expanded = false;
        }

        public static void CollapseAll(GameSession s)
        {
            foreach (var a in s.State.Armies) if (a.Expanded) Collapse(s, a);
        }

        public static void OnSoldierKilled(GameSession s, Unit u)
        {
            float per = s.Balance.war.soldiersPerUnit;
            if (u.ArmyId.StartsWith("garrison:"))
            {
                var c = s.City(u.ArmyId.Substring(9));
                if (c != null) c.Garrison = Math.Max(0, c.Garrison - per);
                return;
            }
            var a = s.Army(u.ArmyId);
            if (a != null) a.Strength = Math.Max(0, a.Strength - per);
            if (a != null && a.Strength <= 0) s.Message($"Отряд {s.Nation(a.NationId)?.Name ?? "разбойников"} разбит.");
        }

        /// <summary>Ежемесячно: движение во время прыжка, налёты разбойников.</summary>
        public static void StepMonth(GameSession s)
        {
            if (s.InTimeJump)
            {
                foreach (var a in s.State.Armies.ToArray())
                {
                    Move(s, a, s.Balance.war.armySpeed * s.Balance.secondsPerMonth * 0.5f);
                    ResolveContacts(s, a, 1f);
                }
                s.State.Armies.RemoveAll(a => a.Strength <= 0.01f);
            }
            // Разбойники — фоновое давление с ранней игры.
            foreach (var p in s.State.Pois)
            {
                if (p.Cleared || s.Content.Poi[p.DefId].kind != "camp" || !s.Rng.Chance(0.04f)) continue;
                var target = s.NearestCity(p.Pos, c => c.NationId != null);
                if (target == null || Vec2.Distance(target.Pos, p.Pos) > 140f) continue;
                int raiders = 0; foreach (var a in s.State.Armies) if (a.Kind == "raiders") raiders++;
                if (raiders >= 3) break;
                var r = Create(s, null, null, s.Rng.Range(8, 20), "raiders");
                r.Pos = p.Pos; r.TargetCityId = target.Id;
                MoveTo(s, r, target.Pos);
                if (target.NationId == s.State.PlayerNationId) s.Message($"Разбойники из «{p.Name}» идут на {target.Name}!");
            }
        }

        public static ArmyState Recruit(GameSession s, CityState c, float strength)
        {
            var n = s.Nation(c.NationId);
            if (n == null) return null;
            float food = strength * s.Balance.war.recruitCostFood;
            float popCost = strength / 5f;
            if (n.Get("food") + c.FoodStock < food) { s.Message($"Нужно {food:0} еды."); return null; }
            if (c.Population < popCost + 5) { s.Message("Мало людей для войска."); return null; }
            float fromN = Math.Min(n.Get("food"), food);
            n.Add("food", -fromN); c.FoodStock -= food - fromN;
            c.Population -= popCost;
            var a = Create(s, n, c, strength, "army");
            s.Message($"Собрано войско ({strength:0}).");
            return a;
        }
    }

    /// <summary>Цели и концовки: военная, влияния, истинное бессмертие, пробуждение; смерть от старости.</summary>
    public static class EndingSystem
    {
        public static void Trigger(GameSession s, string id)
        {
            if (s.State.GameOver) return;
            s.State.EndingId = id;
            s.State.GameOver = true;
            s.State.Narrative.Active = null;
            Chronicle.Add(s, "ending_" + id, null);
            s.Emit(new GameEvent { Type = GameEventType.Ending, Id = id });
        }

        public static void DieOfOldAge(GameSession s) => Trigger(s, "old_age");

        public static void CheckMilitary(GameSession s)
        {
            var pn = s.PlayerNation; if (pn == null) return;
            int rivals = 0, taken = 0;
            foreach (var n in s.State.Nations)
            {
                if (n.IsPlayer) continue;
                rivals++;
                var cap = s.City(n.OriginalCapitalId);
                if (cap != null && cap.NationId == pn.Id) taken++;
            }
            if (rivals > 0 && taken == rivals) Trigger(s, "military");
        }

        public static void CheckMonthly(GameSession s)
        {
            var pn = s.PlayerNation; if (pn == null || s.State.GameOver) return;
            CheckMilitary(s);
            var eb = s.Balance.endings;
            float maxRival = 0;
            foreach (var n in s.State.Nations) if (!n.IsPlayer) maxRival = Math.Max(maxRival, n.InfluenceTotal);
            if (pn.InfluenceTotal >= eb.influenceVictory && pn.InfluenceTotal >= maxRival * eb.influenceRatio) Trigger(s, "influence");
        }

        public static bool AwakeningReady(GameSession s, out string missing)
        {
            var pn = s.PlayerNation; var eb = s.Balance.endings;
            missing = null;
            if (pn == null) { missing = "нет державы"; return false; }
            if (!TechSystem.AllKnowledge(s, pn)) { missing = "нужно всё Знание"; return false; }
            if (HeroSystem.EssenceAvailable(s) < eb.awakeningEssence) { missing = $"нужно {eb.awakeningEssence} ресурса"; return false; }
            if (s.State.Narrative.HintsSeen < eb.awakeningHints) { missing = "мир ещё не просвечивает (мало намёков)"; return false; }
            if (ZoneSystem.BrokenZones(s) >= 3) { missing = "зоны истощены или заброшены — путь закрыт"; return false; }
            return true;
        }

        public static void OnMegastructure(GameSession s, CityState c, string id)
        {
            var pn = s.PlayerNation;
            if (pn == null || c.NationId != pn.Id) return;
            if (id == "eternal_spire" && pn.Researched.Contains("eternity_formula") && CitySystem.CityCount(s, pn) > 0)
            {
                bool lab = false; foreach (var x in s.State.Cities) if (x.NationId == pn.Id && x.HasBuilding("laboratory")) lab = true;
                if (lab)
                {
                    s.State.Narrative.Flags.Add("true_immortality");
                    Trigger(s, "immortality");
                }
            }
            if (id == "mirror_of_sky")
            {
                if (AwakeningReady(s, out var missing))
                {
                    HeroSystem.SpendEssence(s, s.Balance.endings.awakeningEssence);
                    Trigger(s, "awakening");
                }
                else s.Message($"Зеркало небес молчит: {missing}.");
            }
        }
    }
}
