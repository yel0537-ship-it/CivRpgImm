using System;
using System.Collections.Generic;
using Immortal.Core.Action;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using Immortal.Core.Narrative;

namespace Immortal.Core.Time
{
    /// <summary>Единые мировые часы: ~1 минута реального времени = 1 игровой месяц (balance.json).</summary>
    public static class WorldClock
    {
        static readonly string[] Months =
        {
            "Январь", "Февраль", "Март", "Апрель", "Май", "Июнь", "Июль", "Август", "Сентябрь", "Октябрь", "Ноябрь", "Декабрь"
        };

        public static int Year(GameSession s) => s.Balance.startYear + (int)(s.State.Months / 12.0);
        public static int Month(GameSession s) => (int)(s.State.Months % 12.0);
        public static string Format(GameSession s) => $"{Months[Month(s)]}, {Year(s)} год";
        public static string Season(GameSession s) { int m = Month(s); return m < 2 || m == 11 ? "зима" : m < 5 ? "весна" : m < 8 ? "лето" : "осень"; }
    }

    /// <summary>Расчёт всего мира: раз в неделю экономика и наука, раз в месяц решения, ИИ, старение, события.</summary>
    public static class WorldSimulation
    {
        public static void StepWeek(GameSession s)
        {
            foreach (var c in s.State.Cities.ToArray()) CitySystem.Step(s, c, 0.25f);
            foreach (var n in s.State.Nations) if (!n.Destroyed) TechSystem.Step(s, n, 0.25f);
            TerritorySystem.Recompute(s);
        }

        public static void StepMonth(GameSession s)
        {
            CompanionSystem.StepMonth(s);
            foreach (var n in s.State.Nations.ToArray()) NationSystem.StepMonth(s, n);
            Diplomacy.StepMonth(s);
            ArmySystem.StepMonth(s);
            ZoneSystem.StepMonth(s);
            foreach (var c in s.State.Cities.ToArray()) CitySystem.StepMonth(s, c);
            foreach (var n in s.State.Nations) EraSystem.Check(s, n);
            RegrowNodes(s);
            EventSystem.Monthly(s);
            EndingSystem.CheckMonthly(s);
        }

        static void RegrowNodes(GameSession s)
        {
            if ((int)s.State.Months % 6 != 0) return;
            var dep = s.State.WorldMods.Depleted;
            var remove = new List<int>();
            foreach (var kv in dep) if (kv.Value >= 0 && kv.Value <= s.State.Months) remove.Add(kv.Key);
            foreach (var k in remove) dep.Remove(k);
            if (remove.Count > 0) s.State.WorldMods.Revision++;
        }
    }

    /// <summary>
    /// Прыжки во времени: мир быстро просчитывается вперёд, хроника рассказывает, что произошло,
    /// затем герой выходит в заметно изменившийся мир.
    /// </summary>
    public static class TimeJumpSystem
    {
        public static void Jump(GameSession s, int years, string reason)
        {
            if (years <= 0 || s.State.GameOver) return;
            var st = s.State; var ns = st.Narrative; var h = st.Hero;
            ns.Active = null; ns.Queue.Clear();
            ns.JumpOptions.Clear();
            s.InTimeJump = true;
            SpawnSystem.CollapseAll(s);
            int from = st.Chronicle.Count;
            int citiesBefore = st.Cities.Count;
            float popBefore = s.PlayerNation != null ? CitySystem.NationPopulation(s, s.PlayerNation) : 0;
            Chronicle.Add(s, reason == "defeat" ? "sleep_start" : "jump_start", new Dictionary<string, string> { ["years"] = years.ToString() });
            if (reason == "defeat") ns.Flags.Add("hero_absent");

            int months = years * 12;
            for (int m = 0; m < months; m++)
            {
                st.Months += 1.0;
                st.LastWeekMonths = st.Months; st.LastMonthMonths = st.Months;
                for (int w = 0; w < 4; w++) WorldSimulation.StepWeek(s);
                WorldSimulation.StepMonth(s);
                if (!ns.Flags.Contains("true_immortality"))
                {
                    h.VitalityYears -= s.Balance.time.jumpVitalityFactor / 12.0;
                    if (h.VitalityYears <= 0) { h.VitalityYears = 0; EndingSystem.DieOfOldAge(s); }
                }
                if (m % 12 == 11) ChangeLand(s);
                if (st.GameOver) break;
            }
            ns.Flags.Remove("hero_absent");
            SimulationHints.MaybeChronicleGlitch(s);

            float popAfter = s.PlayerNation != null ? CitySystem.NationPopulation(s, s.PlayerNation) : 0;
            Chronicle.Add(s, "jump_end", new Dictionary<string, string>
            {
                ["years"] = years.ToString(), ["pop_before"] = popBefore.ToString("0"), ["pop_after"] = popAfter.ToString("0"),
                ["cities"] = (st.Cities.Count - citiesBefore).ToString()
            });

            // Герой выходит в мир.
            var hero = s.HeroUnit;
            if (hero != null)
            {
                var cap = s.Capital(s.PlayerNation);
                if (cap != null) hero.Pos = s.Map.NearestWalkable(cap.Pos + new Vec2(0, -5));
                hero.PrevPos = hero.Pos;
                hero.Ai = AiState.Idle; hero.Path = null; hero.TargetId = 0; hero.Vel = Vec2.Zero;
                HeroSystem.RefreshUnitStats(s);
                hero.Hp = hero.MaxHp;
            }
            h.Energy = HeroSystem.MaxEnergy(s);
            s.InTimeJump = false;
            if (!st.GameOver) CompanionSystem.RespawnParty(s);
            st.Chapter++;
            ns.ChronicleOpen = true; ns.ChronicleFrom = from;
            s.Map.RebuildBuildingBlockers(s.Content, st);
            TerritorySystem.Recompute(s);
            st.WorldMods.Revision++;
            s.Emit(new GameEvent { Type = GameEventType.TimeJumped, Value = years, Text = reason });
            s.Emit(new GameEvent { Type = GameEventType.ChronicleOpened });
            s.Emit(new GameEvent { Type = GameEventType.Autosave });
            if (!st.GameOver) EventSystem.Fire(s, "after_jump", new DialogueState());
        }

        /// <summary>Поражение героя: долгий сон на десятилетия, часть сырья потеряна.</summary>
        public static void LongSleep(GameSession s)
        {
            var h = s.State.Hero;
            h.Defeats++;
            float loss = s.Balance.hero.defeatResourceLoss;
            foreach (var k in new List<string>(h.Inventory.Keys))
            {
                if (k == "essence") continue;
                h.Inventory[k] = (int)(h.Inventory[k] * (1 - loss));
                if (h.Inventory[k] <= 0) h.Inventory.Remove(k);
            }
            h.Mounted = false;
            s.Emit(new GameEvent { Type = GameEventType.HeroDefeated });
            int years = s.Rng.Range(s.Balance.time.sleepYearsMin, s.Balance.time.sleepYearsMax + 1);
            var hero = s.HeroUnit;
            if (hero != null) { hero.Ai = AiState.Idle; hero.Hp = 1; }
            s.State.Narrative.Flags.Add("legend_of_return");
            if (s.PlayerNation == null && hero != null)
            {
                // Без своей общины герой просыпается там, где упал.
                hero.Pos = s.Map.NearestWalkable(hero.Pos + new Vec2(4, 4));
            }
            Jump(s, years, "defeat");
            EventSystem.Fire(s, "awaken", new DialogueState());
        }

        /// <summary>Мир меняет облик: вокруг городов лес уступает полям, растут дороги.</summary>
        static void ChangeLand(GameSession s)
        {
            float k = s.Balance.time.fieldConversionPerPop;
            foreach (var c in s.State.Cities)
            {
                int conversions = (int)(c.Population * k);
                for (int i = 0; i < conversions; i++)
                {
                    float r = CitySystem.Stage(s, c).radius + 4 + s.Rng.Range(0f, 10f + c.Population * 0.08f);
                    var p = c.Pos + Vec2.FromAngle(s.Rng.Range(0, 6.283f)) * r;
                    int t = s.Map.TileAt(p);
                    var td = s.Map.TerrainAt(t);
                    if (td.fertile && td.id != "field" && td.id != "road") s.Map.SetTerrain(t, "field");
                }
            }
            // Связать города нации дорогами.
            foreach (var c in s.State.Cities)
            {
                if (c.NationId == null) continue;
                bool hasRoad = false;
                foreach (var r in s.State.Roads) if (r.FromCity == c.Id || r.ToCity == c.Id) { hasRoad = true; break; }
                if (hasRoad) continue;
                var other = s.NearestCity(c.Pos, x => x != c && x.NationId == c.NationId);
                if (other != null && Vec2.Distance(other.Pos, c.Pos) < 220) CitySystem.BuildRoad(s, other, c);
            }
        }
    }
}
