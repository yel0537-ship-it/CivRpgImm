using System;

namespace Immortal.Core.Action
{
    /// <summary>ИИ врагов (Idle → Chase → Windup с показанным замахом → Recover), животных, спутников и NPC.</summary>
    public static class AiSystem
    {
        public static void Tick(GameSession s, float dt)
        {
            var hero = s.HeroUnit;
            if (hero != null) s.Flow.Update(hero.Pos); // пересчёт только при смене тайла — детерминированно после загрузки
            var units = s.State.Units;
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.Ai == AiState.Dead || u.Kind == UnitKind.Hero) continue;
                u.StateTimer -= dt;
                switch (u.Kind)
                {
                    case UnitKind.Companion: Companion(s, u, hero, dt); break;
                    case UnitKind.Npc:
                        if (u.Team == Teams.Wild) Hostile(s, u, hero, dt); else Wander(s, u, 0.35f);
                        break;
                    case UnitKind.Animal: Animal(s, u, hero, dt); break;
                    case UnitKind.Leader:
                        if (u.Provoked || (hero != null && s.IsHostile(u.Team, hero.Team))) Hostile(s, u, hero, dt); else Wander(s, u, 0.25f);
                        break;
                    default: Hostile(s, u, hero, dt); break;
                }
            }
        }

        static void Wander(GameSession s, Unit u, float speedFactor)
        {
            if (u.StateTimer <= 0)
            {
                u.StateTimer = s.Rng.Range(2f, 5f);
                if (s.Rng.Chance(0.5f)) { u.Vel = Vec2.Zero; return; }
                var target = u.Home + Vec2.FromAngle(s.Rng.Range(0, 6.283f)) * s.Rng.Range(1f, 5f);
                u.Vel = (target - u.Pos).Normalized * (u.Speed * speedFactor);
            }
            if (Vec2.Distance(u.Pos, u.Home) > 7f) u.Vel = (u.Home - u.Pos).Normalized * (u.Speed * speedFactor);
        }

        static void Animal(GameSession s, Unit u, Unit hero, float dt)
        {
            if (u.Behavior == "melee" && (u.Provoked || u.Ai == AiState.Chase || u.Ai == AiState.Windup || u.Ai == AiState.Recover))
            {
                if (u.TargetId == 0 && hero != null) u.TargetId = hero.Id;
                Engage(s, u, dt, chaseLimit: 30f);
                return;
            }
            if (hero != null && u.Ai != AiState.Flee && Vec2.Distance(hero.Pos, u.Pos) < (u.Provoked ? 14f : 6f) && u.Behavior != "melee")
                u.Ai = AiState.Flee;
            if (u.Ai == AiState.Flee && hero != null)
            {
                var away = (u.Pos - hero.Pos);
                if (away.Length > 18f) { u.Ai = AiState.Wander; u.Home = u.Pos; u.Provoked = false; return; }
                u.Vel = away.Normalized * u.Speed;
                return;
            }
            Wander(s, u, 0.4f);
        }

        static void Hostile(GameSession s, Unit u, Unit hero, float dt)
        {
            if (u.Ai == AiState.Idle || u.Ai == AiState.Wander)
            {
                Wander(s, u, 0.3f);
                if ((s.State.Tick + u.Id) % 5 == 0)
                {
                    var t = FindTarget(s, u, u.AggroRange);
                    if (t != null) { u.TargetId = t.Id; u.Ai = AiState.Chase; }
                }
                return;
            }
            Engage(s, u, dt, chaseLimit: u.Kind == UnitKind.Soldier ? 80f : 45f);
        }

        /// <summary>Общая боевая логика: преследование, замах, удар, восстановление.</summary>
        public static void Engage(GameSession s, Unit u, float dt, float chaseLimit)
        {
            var target = s.FindUnit(u.TargetId);
            if (target == null || target.Ai == AiState.Dead)
            {
                u.TargetId = 0;
                var t = FindTarget(s, u, u.AggroRange);
                if (t == null) { u.Ai = u.Kind == UnitKind.Companion ? AiState.Follow : AiState.Idle; u.Vel = Vec2.Zero; return; }
                u.TargetId = t.Id; target = t;
            }
            float dist = Vec2.Distance(u.Pos, target.Pos);
            switch (u.Ai)
            {
                case AiState.Windup:
                    u.Vel = Vec2.Zero;
                    if (u.StateTimer <= 0)
                    {
                        CombatSystem.UnitStrike(s, u);
                        u.Ai = AiState.Recover; u.StateTimer = u.Recover;
                    }
                    return;
                case AiState.Recover:
                    u.Vel = Vec2.Zero;
                    if (u.StateTimer <= 0) u.Ai = AiState.Chase;
                    return;
            }

            u.Ai = AiState.Chase;
            bool leash = u.Kind != UnitKind.Companion && Vec2.Distance(u.Pos, u.Home) > chaseLimit;
            if (leash || dist > u.AggroRange * 2.5f)
            {
                u.TargetId = 0; u.Ai = AiState.Idle; u.Provoked = false;
                u.Vel = (u.Home - u.Pos).Normalized * u.Speed;
                u.Hp = Math.Min(u.MaxHp, u.Hp + u.MaxHp * 0.3f);
                return;
            }
            if (dist <= u.AttackRange + target.Radius)
            {
                u.Ai = AiState.Windup; u.StateTimer = u.Windup;
                u.AttackDir = (target.Pos - u.Pos).Normalized;
                u.Facing = u.AttackDir;
                u.Vel = Vec2.Zero;
                return;
            }
            Vec2 dir = Vec2.Zero;
            if (target.Kind == UnitKind.Hero && dist > 3f) dir = s.Flow.Direction(u.Pos);
            if (dir.SqrLength < 0.01f) dir = (target.Pos - u.Pos).Normalized;
            // Дальнобойные держат дистанцию.
            if (u.Behavior == "ranged" && dist < u.AttackRange * 0.5f) dir = -dir;
            u.Vel = dir * u.Speed;
            u.Facing = dir;
        }

        public static Unit FindTarget(GameSession s, Unit u, float range)
        {
            Unit best = null; float bd = range * range;
            var units = s.State.Units;
            for (int i = 0; i < units.Count; i++)
            {
                var o = units[i];
                if (o == u || o.Ai == AiState.Dead) continue;
                if (!s.IsHostile(u.Team, o.Team)) continue;
                if (o.Kind == UnitKind.Npc && o.Team == Teams.Neutral) continue;
                if (o.InSmoke != u.InSmoke && o.Kind == UnitKind.Hero && !u.InSmoke) continue; // из-за дыма не видно
                float d = Vec2.SqrDistance(o.Pos, u.Pos);
                if (o.Kind == UnitKind.Hero) d *= 0.8f;
                if (d < bd) { bd = d; best = o; }
            }
            return best;
        }

        static void Companion(GameSession s, Unit u, Unit hero, float dt)
        {
            if (hero == null) { u.Vel = Vec2.Zero; return; }
            if (u.Ai == AiState.Chase || u.Ai == AiState.Windup || u.Ai == AiState.Recover)
            {
                var t = s.FindUnit(u.TargetId);
                if (t != null && Vec2.Distance(t.Pos, hero.Pos) < 18f) { Engage(s, u, dt, 999f); return; }
                u.Ai = AiState.Follow; u.TargetId = 0;
            }
            // Помогаем герою: его цель или ближайший враг рядом.
            if ((s.State.Tick + u.Id) % 5 == 0)
            {
                var ht = s.FindUnit(hero.TargetId);
                Unit t = ht != null && ht.Ai != AiState.Dead && ht.Kind != UnitKind.Npc ? ht : null;
                if (t == null)
                {
                    var near = FindTarget(s, u, 11f);
                    if (near != null && Vec2.Distance(near.Pos, hero.Pos) < 13f) t = near;
                }
                if (t != null) { u.TargetId = t.Id; u.Ai = AiState.Chase; return; }
            }
            u.Ai = AiState.Follow;
            int idx = s.State.Hero.PartyCompanionUnits.IndexOf(u.Id);
            float ang = hero.Facing.Angle + (float)Math.PI + (idx - 1) * 0.7f;
            var spot = hero.Pos + Vec2.FromAngle(ang) * 2.6f;
            float d = Vec2.Distance(u.Pos, spot);
            if (Vec2.Distance(u.Pos, hero.Pos) > 30f) { u.Pos = s.Map.NearestWalkable(spot); u.Vel = Vec2.Zero; return; }
            u.Vel = d > 0.8f ? (spot - u.Pos).Normalized * Math.Min(u.Speed * 1.15f, d * 4f) : Vec2.Zero;
            if (u.Vel.SqrLength > 0.01f) u.Facing = u.Vel.Normalized;
        }
    }
}
