using System;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using Immortal.Core.Content;
using Immortal.Core.Time;

namespace Immortal.Core.Action
{
    /// <summary>
    /// Умения из данных (дуга, снаряд с областью, рывок, область, усиление), автоатака,
    /// снаряды, урон, смерть, поражение героя.
    /// </summary>
    public static class CombatSystem
    {
        public static void Tick(GameSession s, float dt)
        {
            HeroAutoAttack(s, dt);
            Projectiles(s, dt);
            Areas(s, dt);
            Cleanup(s, dt);
            var hero = s.HeroUnit;
            if (hero != null && hero.Ai == AiState.Dead && !s.State.GameOver)
                TimeJumpSystem.LongSleep(s);
        }

        static void HeroAutoAttack(GameSession s, float dt)
        {
            var hero = s.HeroUnit;
            if (hero == null || hero.TargetId == 0 || hero.Ai == AiState.Dead) return;
            var target = s.FindUnit(hero.TargetId);
            if (target == null || target.Ai == AiState.Dead) { hero.TargetId = 0; hero.Path = null; return; }
            var basic = HeroSystem.BasicAttack(s);
            if (basic == null) return;
            float dist = Vec2.Distance(hero.Pos, target.Pos);
            float range = basic.type == "projectile" ? basic.range * 0.9f : basic.range + target.Radius;
            if (dist <= range)
            {
                hero.Path = null;
                hero.Facing = (target.Pos - hero.Pos).Normalized;
                if (!s.State.Hero.Cooldowns.ContainsKey(basic.id)) UseAbility(s, hero, basic, target.Pos);
            }
            else if (hero.Path == null || (s.State.Tick % 10) == 0)
            {
                MovementSystem.SetPath(s, hero, target.Pos);
            }
        }

        /// <summary>Применение умения. Для героя проверяет кулдаун, энергию и требование дыма.</summary>
        public static bool UseAbility(GameSession s, Unit caster, AbilityDef a, Vec2 targetPos)
        {
            bool isHero = caster.Kind == UnitKind.Hero;
            var h = s.State.Hero;
            if (isHero)
            {
                if (h.Cooldowns.ContainsKey(a.id)) return false;
                if (a.requiresSmoke && !h.InSmoke) { s.Message($"«{a.name}» действует только внутри дыма."); return false; }
                if (h.Energy < a.energy) { s.Message("Не хватает энергии."); return false; }
                h.Energy -= a.energy;
                h.Cooldowns[a.id] = a.cooldown * (1f - HeroSystem.Affix(s, "cooldown"));
                h.CombatTimer = 5f;
                HeroSystem.GainSkill(s, "combat", 0.15f);
            }
            Vec2 dir = (targetPos - caster.Pos).Normalized;
            if (dir.SqrLength < 0.01f) dir = caster.Facing;
            caster.Facing = dir;
            float dmg = ComputeDamage(s, caster, a);
            s.Emit(new GameEvent { Type = GameEventType.AbilityCast, UnitId = caster.Id, Id = a.id, Pos = targetPos, Value = dir.Angle });

            switch (a.type)
            {
                case "arc":
                    HitArc(s, caster, caster.Pos, dir, a.range, a.arc, dmg, a.mystic);
                    break;
                case "projectile":
                    for (int i = 0; i < Math.Max(1, a.count); i++)
                    {
                        float spread = a.count > 1 ? (i - (a.count - 1) / 2f) * 0.18f : 0f;
                        var d = Vec2.FromAngle(dir.Angle + spread);
                        s.State.Projectiles.Add(new Projectile
                        {
                            Id = s.State.NextId++, OwnerId = caster.Id, Team = caster.Team,
                            Pos = caster.Pos + d * (caster.Radius + 0.3f), Vel = d * a.speed, Damage = dmg,
                            Aoe = a.radius, Life = a.range / Math.Max(1f, a.speed), AbilityId = a.id, Mystic = a.mystic
                        });
                    }
                    break;
                case "dash":
                    Dash(s, caster, dir, a.range, dmg, a.radius);
                    break;
                case "area":
                    var p = caster.Pos + (targetPos - caster.Pos).ClampLength(a.range);
                    s.State.Areas.Add(new AreaEffect
                    {
                        Pos = p, Radius = a.radius, Damage = dmg, Delay = Math.Max(0.05f, a.windup), Interval = 0.35f,
                        Pulses = Math.Max(1, a.count), Team = caster.Team, OwnerId = caster.Id, AbilityId = a.id, Mystic = a.mystic
                    });
                    break;
                case "buff":
                    if (a.buffStat == "heal") caster.Hp = Math.Min(caster.MaxHp, caster.Hp + a.buffValue + dmg);
                    else if (isHero) h.Buffs.Add(new Buff { Stat = a.buffStat, Value = a.buffValue, TimeLeft = a.duration, Source = a.id });
                    if (a.damage > 0) HitArc(s, caster, caster.Pos, dir, a.radius, 360, dmg, a.mystic);
                    break;
            }
            return true;
        }

        public static float ComputeDamage(GameSession s, Unit caster, AbilityDef a)
        {
            if (caster.Kind == UnitKind.Hero)
            {
                var h = s.State.Hero;
                float scale = a.scaling == "mind" ? 1f + HeroSystem.EffMind(s) * 0.1f : 1f + HeroSystem.EffBody(s) * 0.08f;
                float baseDmg = a.damage + (a.mystic ? 0f : HeroSystem.WeaponDamage(s));
                return baseDmg * scale * (1f + HeroSystem.BuffValue(h, "damage")) * (1f + HeroSystem.Skill(h, "combat") * 0.004f);
            }
            return caster.Damage * (a.damage > 0 ? a.damage / 10f : 1f);
        }

        static bool CanHit(GameSession s, Unit src, Unit target)
        {
            if (target == src || target.Ai == AiState.Dead) return false;
            if (s.IsHostile(src.Team, target.Team)) return true;
            // Герой и спутники могут бить нейтральных животных (охота) и спровоцированных.
            if (src.Team == Teams.Player && target.Team == Teams.Neutral && (target.Kind == UnitKind.Animal)) return true;
            return false;
        }

        public static int HitArc(GameSession s, Unit src, Vec2 origin, Vec2 dir, float range, float arcDeg, float dmg, bool mystic)
        {
            int hits = 0;
            float cosHalf = (float)Math.Cos(arcDeg * 0.5f * Math.PI / 180.0);
            var units = s.State.Units;
            for (int i = 0; i < units.Count; i++)
            {
                var t = units[i];
                if (!CanHit(s, src, t)) continue;
                var d = t.Pos - origin;
                float dist = d.Length;
                if (dist > range + t.Radius) continue;
                if (arcDeg < 359 && dist > 0.3f && Vec2.Dot(d / dist, dir) < cosHalf) continue;
                DealDamage(s, src, t, dmg, mystic);
                hits++;
            }
            return hits;
        }

        static void Dash(GameSession s, Unit u, Vec2 dir, float dist, float dmg, float radius)
        {
            float moved = 0;
            while (moved < dist)
            {
                var np = u.Pos + dir * 0.3f;
                if (!s.Map.Walkable(np)) break;
                u.Pos = np; moved += 0.3f;
            }
            if (u.Kind == UnitKind.Hero) s.State.Hero.Buffs.Add(new Buff { Stat = "invuln", Value = 1, TimeLeft = 0.3f, Source = "dash" });
            if (dmg > 0) HitArc(s, u, u.Pos, dir, Math.Max(1.2f, radius), 360, dmg, false);
            u.Path = null;
        }

        public static void DealDamage(GameSession s, Unit src, Unit target, float dmg, bool mystic)
        {
            if (target.Ai == AiState.Dead) return;
            if (target.Kind == UnitKind.Hero && HeroSystem.BuffValue(s.State.Hero, "invuln") > 0) return;
            bool crit = false;
            if (src != null && src.Kind == UnitKind.Hero && s.Rng.Chance(0.07f)) { dmg *= 1.6f; crit = true; }
            dmg *= s.Rng.Range(0.9f, 1.1f);
            float armor = target.Armor;
            if (target.Kind == UnitKind.Hero) armor += HeroSystem.BuffValue(s.State.Hero, "armor");
            float final = Math.Max(1f, dmg - armor * (mystic ? 0.3f : 1f));
            target.Hp -= final;
            s.Emit(new GameEvent { Type = GameEventType.Hit, UnitId = target.Id, Value = final, Crit = crit, Pos = target.Pos });

            if (target.Kind == UnitKind.Hero) s.State.Hero.CombatTimer = 5f;
            if (src != null)
            {
                if (src.Kind == UnitKind.Hero) s.State.Hero.CombatTimer = 5f;
                target.Provoked = true;
                if (target.Kind != UnitKind.Hero && target.Kind != UnitKind.Companion &&
                    (target.Ai == AiState.Idle || target.Ai == AiState.Wander || target.Ai == AiState.Chase) &&
                    !(target.Kind == UnitKind.Animal && target.Behavior != "melee"))
                {
                    target.TargetId = src.Id;
                    if (target.Ai != AiState.Chase) target.Ai = AiState.Chase;
                }
            }
            if (target.Hp <= 0) Kill(s, target, src);
        }

        public static void Kill(GameSession s, Unit u, Unit killer)
        {
            u.Hp = 0; u.Ai = AiState.Dead; u.DeadTimer = 1.5f; u.Vel = Vec2.Zero; u.Path = null;
            s.Emit(new GameEvent { Type = GameEventType.Death, UnitId = u.Id, Pos = u.Pos });
            if (u.Kind == UnitKind.Hero) return; // обработка поражения после тика

            bool byPlayer = killer != null && killer.Team == Teams.Player;
            s.Content.Enemies.TryGetValue(u.DefId ?? "", out var def);
            if (byPlayer && def != null)
            {
                HeroSystem.GainXp(s, def.xp);
                if (def.lootTable != null) LootSystem.DropTable(s, def.lootTable, u.Pos);
                if (def.essence > 0 && s.Rng.Chance(def.essence))
                    LootSystem.Drop(s, u.Pos, "essence", 1);
            }

            if (u.Kind == UnitKind.Companion) Character.CompanionSystem.OnDowned(s, u);
            if (u.PoiId != null) SpawnSystem.OnPoiUnitKilled(s, u, byPlayer);
            if (u.ArmyId != null) ArmySystem.OnSoldierKilled(s, u);
            if (u.Kind == UnitKind.Leader) NationSystem.OnLeaderKilled(s, u.NationId, byPlayer);
            if (u.NpcId != null) { var npc = s.Npc(u.NpcId); if (npc != null) { npc.Alive = false; npc.UnitId = 0; } }
        }

        public static void UnitStrike(GameSession s, Unit u)
        {
            s.Emit(new GameEvent { Type = GameEventType.Strike, UnitId = u.Id, Pos = u.Pos, Id = u.Behavior });
            if (u.Behavior == "ranged")
            {
                float speed = 14f;
                s.State.Projectiles.Add(new Projectile
                {
                    Id = s.State.NextId++, OwnerId = u.Id, Team = u.Team, Pos = u.Pos + u.AttackDir * (u.Radius + 0.3f),
                    Vel = u.AttackDir * speed, Damage = u.Damage, Life = (u.AttackRange + 2f) / speed, AbilityId = "enemy_shot",
                    Mystic = u.Kind == UnitKind.Spirit
                });
                return;
            }
            HitArc(s, u, u.Pos, u.AttackDir, u.AttackRange + 0.4f, 100f, u.Damage, u.Kind == UnitKind.Spirit);
        }

        static void Projectiles(GameSession s, float dt)
        {
            var list = s.State.Projectiles;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var p = list[i];
                p.Pos += p.Vel * dt;
                p.Life -= dt;
                var owner = s.FindUnit(p.OwnerId);
                var src = owner ?? new Unit { Team = p.Team, Id = -1 };
                bool hit = false;
                var units = s.State.Units;
                for (int k = 0; k < units.Count; k++)
                {
                    var t = units[k];
                    if (!CanHit(s, src, t)) continue;
                    float r = t.Radius + p.Radius;
                    if (Vec2.SqrDistance(t.Pos, p.Pos) > r * r) continue;
                    if (p.Aoe <= 0) DealDamage(s, owner, t, p.Damage, p.Mystic);
                    hit = true; break;
                }
                var terrain = s.Map.TerrainAt(p.Pos);
                bool wall = !terrain.walkable && terrain.id != "water";
                if (hit || p.Life <= 0 || wall)
                {
                    if (p.Aoe > 0) Explode(s, owner, src, p.Pos, p.Aoe, p.Damage, p.Mystic, p.AbilityId);
                    list.RemoveAt(i);
                }
            }
        }

        static void Explode(GameSession s, Unit owner, Unit src, Vec2 pos, float radius, float dmg, bool mystic, string ability)
        {
            s.Emit(new GameEvent { Type = GameEventType.AbilityCast, Id = "explosion", Pos = pos, Value = radius, Text = ability });
            var units = s.State.Units;
            for (int k = 0; k < units.Count; k++)
            {
                var t = units[k];
                if (!CanHit(s, src, t)) continue;
                if (Vec2.Distance(t.Pos, pos) <= radius + t.Radius) DealDamage(s, owner, t, dmg, mystic);
            }
        }

        static void Areas(GameSession s, float dt)
        {
            var list = s.State.Areas;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                var a = list[i];
                a.Delay -= dt;
                if (a.Delay > 0) continue;
                var owner = s.FindUnit(a.OwnerId);
                var src = owner ?? new Unit { Team = a.Team, Id = -1 };
                var jitter = a.Pulses > 1 ? Vec2.FromAngle(s.Rng.Range(0, 6.283f)) * s.Rng.Range(0, a.Radius * 0.6f) : Vec2.Zero;
                Explode(s, owner, src, a.Pos + jitter, a.Radius * (a.Pulses > 1 ? 0.6f : 1f), a.Damage, a.Mystic, a.AbilityId);
                a.Pulses--;
                if (a.Pulses <= 0) list.RemoveAt(i); else a.Delay = a.Interval;
            }
        }

        static void Cleanup(GameSession s, float dt)
        {
            var units = s.State.Units;
            for (int i = units.Count - 1; i >= 0; i--)
            {
                var u = units[i];
                if (u.Ai != AiState.Dead || u.Kind == UnitKind.Hero) continue;
                u.DeadTimer -= dt;
                if (u.DeadTimer <= 0) s.RemoveUnit(u);
            }
        }
    }
}
