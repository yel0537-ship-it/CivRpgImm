using System;
using System.Collections.Generic;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using Immortal.Core.Content;
using Immortal.Core.Narrative;

namespace Immortal.Core.World
{
    /// <summary>Заселение нового мира: точки интереса, люди для вербовки, нации-соперники, герой.</summary>
    public static class WorldSetup
    {
        public static void Populate(GameSession s)
        {
            var db = s.Content; var st = s.State; var map = s.Map;
            var rnd = new XorShiftRandom((ulong)(st.Seed * 31L + 7));
            var occupied = new List<Vec2>();

            // Старт героя и столицы соперников.
            NationDef playerDef = db.NationDefs.Find(n => n.player);
            int startRegion = Math.Max(0, db.Regions.FindIndex(r => r.id == playerDef?.region));
            Vec2 start = map.NearestWalkable(map.RegionCenters[startRegion], 20);
            occupied.Add(start);

            int ai = 0;
            foreach (var nd in db.NationDefs)
            {
                if (nd.player) continue;
                var n = NationSystem.Create(s, nd, ai++);
                int ri = Math.Max(0, db.Regions.FindIndex(r => r.id == nd.region));
                var pos = map.NearestWalkable(map.RegionCenters[ri], 20);
                occupied.Add(pos);
                var cap = CitySystem.Found(s, n, pos, nd.cityNames.Count > 0 ? nd.cityNames[0] : nd.name, 1, true);
                cap.Population = nd.startPop;
                foreach (var b in new[] { "hut", "hut", "hut", "field", "field", "storage" }) CitySystem.PlaceBuilding(s, cap, b, free: true);
                n.Treasury["wood"] = 40; n.Treasury["stone"] = 25; n.Treasury["food"] = 20;
                cap.FoodStock = 40;
            }
            st.Chronicle.Clear();

            // Точки интереса по регионам.
            for (int ri = 0; ri < db.Regions.Count; ri++)
            {
                var reg = db.Regions[ri];
                foreach (var kv in reg.pois)
                {
                    var def = db.Poi[kv.Key];
                    for (int k = 0; k < kv.Value; k++)
                    {
                        float minDist = def.kind == "zone" ? 45f : 26f;
                        for (int attempt = 0; attempt < 120; attempt++)
                        {
                            var p = map.RegionCenters[ri] + Vec2.FromAngle(rnd.Range(0, 6.283f)) * rnd.Range(20f, map.WorldSize * 0.2f);
                            p = map.Clamp(p);
                            if (map.RegionIndexAt(p) != ri || !map.Walkable(p)) continue;
                            if (ri == startRegion && Vec2.Distance(p, start) < (def.kind == "camp" || def.kind == "zone" ? 70f : 22f)) continue;
                            bool clash = false;
                            foreach (var o in occupied) if (Vec2.Distance(o, p) < minDist) { clash = true; break; }
                            if (clash) continue;
                            occupied.Add(p);
                            st.Pois.Add(new PoiState
                            {
                                Id = st.NewId("poi"), DefId = def.id, Name = def.name, RegionId = reg.id, Pos = p,
                                Radius = def.kind == "zone" ? rnd.Range(11f, 18f) : def.radius,
                                EnemiesRemaining = SpawnSystemTotal(def), EssencePool = def.essencePool, LastUsedMonths = 0
                            });
                            break;
                        }
                    }
                }
            }

            // Люди, которых можно завербовать.
            foreach (var rd in db.RecruitsFile.recruits)
            {
                int ri = Math.Max(0, db.Regions.FindIndex(r => r.id == rd.region));
                Vec2 center = ri == startRegion ? start : map.RegionCenters[ri];
                for (int attempt = 0; attempt < 80; attempt++)
                {
                    float maxR = ri == startRegion ? 26f + attempt : 30f + attempt * 1.5f;
                    var p = map.Clamp(center + Vec2.FromAngle(rnd.Range(0, 6.283f)) * rnd.Range(8f, maxR));
                    if (!map.Walkable(p)) continue;
                    bool clash = false;
                    foreach (var o in occupied) if (Vec2.Distance(o, p) < 7f) { clash = true; break; }
                    if (clash && attempt < 60) continue;
                    occupied.Add(p);
                    st.Npcs.Add(new NpcState
                    {
                        Id = st.NewId("npc"), RecruitDefId = rd.id, Name = rd.name, Pos = p, Home = p, Need = rd.need,
                        Character = rd.character, Distrust = rd.distrust, FaceId = rnd.Range(1, 999)
                    });
                    break;
                }
            }

            // Герой.
            var hb = db.Balance.hero; var h = st.Hero;
            h.VitalityYears = hb.startVitalityYears; h.MaxVitalityYears = hb.maxVitalityYears; h.StartAge = hb.startAge;
            h.Memories.Add("core_tribe");
            h.Add("berries", 4);
            foreach (var r in db.ItemsFile.recipes) if (r.tech == null && r.era == "primitive") h.KnownRecipes.Add(r.id);
            var hero = s.AddUnit(new Unit
            {
                Id = h.UnitId, Kind = UnitKind.Hero, Name = h.Name, Team = Teams.Player, Pos = start, Home = start,
                Radius = hb.radius, Speed = hb.speed, Ai = AiState.Idle
            });
            HeroSystem.RefreshUnitStats(s);
            hero.Hp = hero.MaxHp;
            h.Energy = HeroSystem.MaxEnergy(s);
            if (st.NextId <= h.UnitId) st.NextId = h.UnitId + 1;
            Vision.Reveal(st, map, start, db.Balance.vision.radius);
            TerritorySystem.Recompute(s);
            Chronicle.Add(s, "start", null);
            EventSystem.Fire(s, "start", new DialogueState());
        }

        static int SpawnSystemTotal(PoiDef def)
        {
            int n = 0; foreach (var e in def.enemies) n += e.count; return n;
        }
    }
}
