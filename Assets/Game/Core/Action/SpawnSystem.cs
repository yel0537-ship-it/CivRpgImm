using System;
using System.Collections.Generic;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using Immortal.Core.Content;
using Immortal.Core.Narrative;

namespace Immortal.Core.Action
{
    /// <summary>
    /// Разворачивание/сворачивание упрощённых данных в реальных юнитов рядом с героем:
    /// враги точек интереса, животные, NPC, лидеры. Результат сохраняется при сворачивании.
    /// </summary>
    public static class SpawnSystem
    {
        public static Unit CreateEnemy(GameSession s, string defId, Vec2 pos, int team, string poiId = null)
        {
            var d = s.Content.Enemies[defId];
            var kind = d.kind == "animal" ? UnitKind.Animal : d.kind == "spirit" ? UnitKind.Spirit : d.kind == "soldier" ? UnitKind.Soldier : UnitKind.Enemy;
            float danger = 1f;
            if (poiId != null) { var poi = s.Poi(poiId); if (poi != null && s.Content.RegionById.TryGetValue(poi.RegionId ?? "", out var reg)) danger = reg.danger; }
            float hpMul = 0.8f + danger * 0.2f;
            var u = new Unit
            {
                Kind = kind, DefId = defId, Name = d.name, Team = team, Pos = s.Map.NearestWalkable(pos, 4), Home = pos,
                Radius = d.radius * d.scale, Speed = d.speed, Hp = d.hp * hpMul, MaxHp = d.hp * hpMul, Damage = d.damage * (0.85f + danger * 0.15f),
                Armor = d.armor, AttackRange = d.attackRange, AggroRange = d.aggroRange, Windup = d.windup, Recover = d.recover,
                Behavior = d.behavior, PoiId = poiId, Ai = AiState.Idle, Boss = d.boss,
                Ability = d.ability, AbilityCd = d.abilityCooldown * 0.5f
            };
            return s.AddUnit(u);
        }

        public static void Tick(GameSession s)
        {
            var hero = s.HeroUnit;
            if (hero == null) return;
            var act = s.Balance.activation;
            int activeEnemies = s.CountUnits(u => u.Ai != AiState.Dead && (u.Kind == UnitKind.Enemy || u.Kind == UnitKind.Spirit));

            foreach (var p in s.State.Pois)
            {
                var def = s.Content.Poi[p.DefId];
                if (def.kind == "zone") continue; // сущности дыма — ZoneSystem
                float d = Vec2.Distance(p.Pos, hero.Pos);
                if (!p.Active && !p.Cleared && p.EnemiesRemaining > 0 && d < act.poiRadius && activeEnemies < act.maxActiveEnemies)
                {
                    int team = p.OwnerNationId != null ? s.NationTeam(p.OwnerNationId) : Teams.Wild;
                    int n = Math.Min(p.EnemiesRemaining, act.maxActiveEnemies - activeEnemies);
                    var types = new List<string>();
                    foreach (var e in def.enemies) for (int i = 0; i < e.count; i++) types.Add(e.id);
                    for (int i = 0; i < n; i++)
                    {
                        var pos = p.Pos + Vec2.FromAngle(i * 2.4f) * (1.5f + i * 0.5f);
                        CreateEnemy(s, types[i % types.Count], pos, team, p.Id);
                    }
                    p.Active = true; activeEnemies += n;
                }
                else if (p.Active && d > act.despawnRadius) Collapse(s, p);
                if (p.Cleared && p.RespawnAtMonths > 0 && s.State.Months >= p.RespawnAtMonths && d > act.despawnRadius)
                {
                    p.Cleared = false; p.RespawnAtMonths = -1; p.OwnerNationId = null;
                    p.EnemiesRemaining = TotalEnemies(def);
                }
            }

            AnimalsTick(s, hero);
            NpcTick(s, hero);
            LeadersTick(s, hero);
            CityPeopleTick(s, hero);

            // Сворачиваем далёких животных и бродяг.
            var units = s.State.Units;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                if (u.Kind == UnitKind.Hero || u.Kind == UnitKind.Companion || u.PoiId != null || u.ArmyId != null) continue;
                if (Vec2.Distance(u.Pos, hero.Pos) > act.despawnRadius)
                {
                    if (u.NpcId != null) { var npc = s.Npc(u.NpcId); if (npc != null) { npc.UnitId = 0; npc.Pos = u.Pos; } }
                    if (u.Kind == UnitKind.Leader) { var n = s.Nation(u.NationId); if (n != null) n.Leader.UnitId = 0; }
                    s.RemoveUnit(u);
                }
            }
        }

        public static int TotalEnemies(PoiDef def)
        {
            int n = 0; foreach (var e in def.enemies) n += e.count; return n;
        }

        public static void Collapse(GameSession s, PoiState p)
        {
            int alive = 0;
            var units = s.State.Units;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                if (u.PoiId != p.Id) continue;
                if (u.Ai != AiState.Dead) alive++;
                s.RemoveUnit(u);
            }
            if (s.Content.Poi[p.DefId].kind != "zone") p.EnemiesRemaining = alive;
            p.Active = false;
        }

        public static void CollapseAll(GameSession s)
        {
            foreach (var p in s.State.Pois) if (p.Active) Collapse(s, p);
            ArmySystem.CollapseAll(s);
            var units = s.State.Units;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                if (u.Kind == UnitKind.Hero) continue;
                if (u.NpcId != null) { var npc = s.Npc(u.NpcId); if (npc != null) npc.UnitId = 0; }
                if (u.Kind == UnitKind.Leader) { var n = s.Nation(u.NationId); if (n != null) n.Leader.UnitId = 0; }
                if (u.Kind == UnitKind.Companion) { var c = s.Companion(u.CompanionId); if (c != null) c.UnitId = 0; }
                s.RemoveUnit(u);
            }
            s.State.Hero.PartyCompanionUnits.Clear();
            s.State.Projectiles.Clear();
            s.State.Areas.Clear();
            s.State.Hero.Pending = null;
        }

        public static void OnPoiUnitKilled(GameSession s, Unit u, bool byPlayer)
        {
            var p = s.Poi(u.PoiId);
            if (p == null) return;
            var def = s.Content.Poi[p.DefId];
            if (def.kind == "zone") { ZoneSystem.OnSpiritKilled(s, p); return; }
            bool anyAlive = false;
            foreach (var o in s.State.Units) if (o.PoiId == p.Id && o.Ai != AiState.Dead) { anyAlive = true; break; }
            if (!anyAlive && p.Active) OnPoiCleared(s, p, byPlayer);
        }

        public static void OnPoiCleared(GameSession s, PoiState p, bool byPlayer)
        {
            var def = s.Content.Poi[p.DefId];
            p.Cleared = true; p.EnemiesRemaining = 0; p.Active = false;
            p.RespawnAtMonths = def.respawnYears > 0 ? s.State.Months + def.respawnYears * 12 : -1;
            foreach (var u in s.State.Units) if (u.PoiId == p.Id) u.PoiId = null;
            if (!byPlayer) return;
            var h = s.State.Hero;
            foreach (var kv in def.rewards) LootSystem.Drop(s, p.Pos, kv.Key, kv.Value);
            if (def.lootTable != null) LootSystem.DropTable(s, def.lootTable, p.Pos);
            HeroSystem.GainXp(s, (int)def.xp);
            h.Reputation += 2;
            if (def.capturable && s.State.PlayerNationId != null)
            {
                p.OwnerNationId = s.State.PlayerNationId;
                s.Message($"Аванпост «{p.Name}» теперь ваш. Влияние расширилось.");
                Chronicle.Add(s, "outpost", new Dictionary<string, string> { ["poi"] = p.Name });
                TerritorySystem.Recompute(s);
            }
            else s.Message($"{p.Name}: зачищено.");
            if (def.kind == "camp") Chronicle.Add(s, "camp_cleared", new Dictionary<string, string> { ["poi"] = p.Name });
            TechSystem.TryRandomDiscovery(s, 0.25f);
            foreach (var n in s.State.Nations)
                if (!n.IsPlayer && Vec2.Distance(s.Capital(n)?.Pos ?? new Vec2(-9999, -9999), p.Pos) < 120f)
                    NationSystem.Remember(s, n, "hero_cleared_bandits", 3);
        }

        static void AnimalsTick(GameSession s, Unit hero)
        {
            var act = s.Balance.activation;
            if (s.State.Tick % (int)(act.animalSpawnInterval * s.TicksPerSecond) != 0) return;
            int animals = s.CountUnits(u => u.Kind == UnitKind.Animal && u.Ai != AiState.Dead);
            if (animals >= act.maxAnimals) return;
            var reg = s.Map.RegionAt(hero.Pos);
            if (reg.fauna.Count == 0) return;
            var pos = hero.Pos + Vec2.FromAngle(s.Rng.Range(0, 6.283f)) * s.Rng.Range(28f, 45f);
            if (!s.Map.Walkable(pos) || ZoneSystem.ZoneAt(s, pos) != null) return;
            if (s.NearestCity(pos) is CityState c && Vec2.Distance(c.Pos, pos) < 25f) return;
            string id = reg.fauna[s.Rng.NextInt(reg.fauna.Count)];
            var def = s.Content.Enemies[id];
            CreateEnemy(s, id, pos, def.kind == "animal" && def.behavior != "melee" ? Teams.Neutral : Teams.Wild);
        }

        static void NpcTick(GameSession s, Unit hero)
        {
            float r = s.Balance.activation.npcRadius;
            foreach (var npc in s.State.Npcs)
            {
                if (npc.Recruited || !npc.Alive || npc.UnitId != 0) continue;
                if (Vec2.Distance(npc.Pos, hero.Pos) > r) continue;
                var u = s.AddUnit(new Unit
                {
                    Kind = UnitKind.Npc, NpcId = npc.Id, Name = npc.Name, Team = npc.Hostile ? Teams.Wild : Teams.Neutral,
                    Pos = s.Map.NearestWalkable(npc.Pos), Home = npc.Home, Hp = 60, MaxHp = 60, Damage = 7, Speed = 3.2f,
                    Behavior = "melee", AggroRange = 9, DefId = npc.RecruitDefId, Ai = AiState.Wander
                });
                npc.UnitId = u.Id;
            }
        }

        static void LeadersTick(GameSession s, Unit hero)
        {
            float r = s.Balance.activation.npcRadius;
            foreach (var n in s.State.Nations)
            {
                if (n.IsPlayer || !n.Leader.Alive || n.Leader.Captive || n.Leader.UnitId != 0) continue;
                var cap = s.Capital(n);
                if (cap == null || Vec2.Distance(cap.Pos, hero.Pos) > r) continue;
                var pos = cap.Pos + new Vec2(2.5f, -2.5f);
                var u = s.AddUnit(new Unit
                {
                    Kind = UnitKind.Leader, NationId = n.Id, Name = n.Leader.Name, Team = n.Team, Pos = s.Map.NearestWalkable(pos), Home = pos,
                    Hp = 420, MaxHp = 420, Damage = 18, Armor = 3, Speed = 4.5f, Radius = 0.6f, AttackRange = 1.8f, AggroRange = 14,
                    Behavior = "melee", Ai = AiState.Wander, Windup = 0.7f, Recover = 0.5f, Boss = true, DefId = "leader"
                });
                n.Leader.UnitId = u.Id;
            }
        }

        /// <summary>Жители и спутники в городах рядом с героем: государство видно через людей.</summary>
        static void CityPeopleTick(GameSession s, Unit hero)
        {
            if (s.State.Tick % 40 != 0) return;
            foreach (var c in s.State.Cities)
            {
                if (Vec2.Distance(c.Pos, hero.Pos) > s.Balance.activation.npcRadius) continue;
                foreach (var comp in s.State.Companions)
                {
                    if (!comp.Alive || comp.UnitId != 0 || comp.Role == CompanionRoles.Party || comp.Role == CompanionRoles.Errand) continue;
                    string home = comp.Role == CompanionRoles.Governor || comp.Role == CompanionRoles.Rebel ? comp.AssignmentId : s.Capital(s.PlayerNation)?.Id;
                    if (home != c.Id) continue;
                    var pos = c.Pos + Vec2.FromAngle(MathUtil.Hash01((int)(MathUtil.Fnv1a(comp.Id) & 0x7FFFFFFF), 1, 3) * 6.28f) * 4f;
                    var u = s.AddUnit(new Unit
                    {
                        Kind = UnitKind.Npc, CompanionId = comp.Id, Name = comp.Name, Team = comp.Role == CompanionRoles.Rebel ? Teams.Neutral : Teams.Player,
                        Pos = s.Map.NearestWalkable(pos), Home = pos, Hp = 60, MaxHp = 60, Speed = 2.5f, Ai = AiState.Wander, DefId = "companion_npc"
                    });
                    comp.UnitId = u.Id;
                }
            }
            // Убираем спутников-NPC, если ушли далеко или сменили роль.
            var units = s.State.Units;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                if (u.Kind != UnitKind.Npc || u.CompanionId == null) continue;
                var comp = s.Companion(u.CompanionId);
                if (comp == null || !comp.Alive || comp.Role == CompanionRoles.Party || comp.Role == CompanionRoles.Errand || Vec2.Distance(u.Pos, hero.Pos) > s.Balance.activation.despawnRadius)
                {
                    if (comp != null && comp.UnitId == u.Id) comp.UnitId = 0;
                    s.RemoveUnit(u);
                }
            }
        }
    }

    /// <summary>Мистические зоны: дым, сущности, сила эпохи, рост и сжатие.</summary>
    public static class ZoneSystem
    {
        public static PoiState ZoneAt(GameSession s, Vec2 p)
        {
            foreach (var z in s.State.Pois)
                if (z.Radius > 0 && s.Content.Poi[z.DefId].kind == "zone" && Vec2.Distance(z.Pos, p) < z.Radius) return z;
            return null;
        }

        public static void TickRealtime(GameSession s, float dt)
        {
            var hero = s.HeroUnit; if (hero == null) return;
            var h = s.State.Hero;
            var zone = ZoneAt(s, hero.Pos);
            bool inSmoke = zone != null;
            if (inSmoke != h.InSmoke)
            {
                h.InSmoke = inSmoke; h.SmokePoiId = zone?.Id;
                if (inSmoke)
                {
                    var era = s.PlayerEraDef;
                    string power = era.superpowerName != null ? $" Сила эпохи (4): {era.superpowerName}." : " В эту эпоху дым даёт только ресурс.";
                    s.Emit(new GameEvent { Type = GameEventType.ZoneEntered, Id = zone.Id, Text = "Вы вошли в дым." + power });
                    s.Message("Вы вошли в дым." + power);
                    SimulationHints.ZoneGlitch(s, zone);
                }
                else
                {
                    s.Emit(new GameEvent { Type = GameEventType.ZoneExited });
                    s.Message("Вы вышли из дыма. Сила эпохи угасла.");
                }
            }
            foreach (var u in s.State.Units) u.InSmoke = ZoneAt(s, u.Pos) != null;

            foreach (var z in s.State.Pois)
            {
                if (s.Content.Poi[z.DefId].kind != "zone") continue;
                float d = Vec2.Distance(z.Pos, hero.Pos);
                if (d < z.Radius + 12f)
                {
                    z.Active = true;
                    int want = 2 + (int)(z.Radius / 8f);
                    int have = 0;
                    foreach (var u in s.State.Units) if (u.PoiId == z.Id && u.Ai != AiState.Dead) have++;
                    if (have < want && s.Rng.Chance(0.35f))
                    {
                        var era = s.PlayerEraDef;
                        var list = era.smokeEntities.Count > 0 ? era.smokeEntities : s.Content.Eras[0].smokeEntities;
                        string id = list[s.Rng.NextInt(list.Count)];
                        var pos = z.Pos + Vec2.FromAngle(s.Rng.Range(0, 6.283f)) * s.Rng.Range(0, z.Radius * 0.8f);
                        if (s.Map.Walkable(pos)) SpawnSystem.CreateEnemy(s, id, pos, Teams.Wild, z.Id);
                    }
                }
                else if (z.Active && d > z.Radius + 45f) SpawnSystem.Collapse(s, z);
            }
        }

        public static void OnSpiritKilled(GameSession s, PoiState z)
        {
            var eb = s.Balance.essence;
            z.Kills++;
            z.LastUsedMonths = s.State.Months;
            z.Radius = Math.Max(eb.zoneMinRadius, z.Radius - eb.zoneShrinkPerKill);
            if (z.EssencePool > 0) z.EssencePool -= 0.5f;
            if (z.Radius <= eb.zoneMinRadius + 0.01f && !s.State.Narrative.Flags.Contains("zone_exhausted_" + z.Id))
            {
                s.State.Narrative.Flags.Add("zone_exhausted_" + z.Id);
                s.Message($"Зона «{z.Name}» истощена.");
            }
        }

        /// <summary>Раз в месяц: заброшенные зоны растут, часто используемые — нет.</summary>
        public static void StepMonth(GameSession s)
        {
            var eb = s.Balance.essence;
            foreach (var z in s.State.Pois)
            {
                if (s.Content.Poi[z.DefId].kind != "zone") continue;
                if (s.State.Months - z.LastUsedMonths > 12)
                    z.Radius = Math.Min(eb.zoneMaxRadius, z.Radius + eb.zoneGrowthPerYear / 12f);
                z.EssencePool = Math.Min(30, z.EssencePool + eb.poolRegenPerYear / 12f);
                if (z.Radius >= eb.zoneMaxRadius - 0.01f) s.State.Narrative.Flags.Add("zone_overgrown_" + z.Id);
            }
        }

        public static int BrokenZones(GameSession s)
        {
            int n = 0;
            foreach (var f in s.State.Narrative.Flags) if (f.StartsWith("zone_exhausted_") || f.StartsWith("zone_overgrown_")) n++;
            return n;
        }
    }
}
