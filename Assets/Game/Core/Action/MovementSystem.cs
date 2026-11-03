using System;
using Immortal.Core.Character;

namespace Immortal.Core.Action
{
    /// <summary>Движение по пути/WASD, скольжение вдоль препятствий, мягкое расталкивание кругов.</summary>
    public static class MovementSystem
    {
        public static void Tick(GameSession s, float dt)
        {
            var units = s.State.Units;
            for (int i = 0; i < units.Count; i++) units[i].PrevPos = units[i].Pos;

            var hero = s.HeroUnit;
            if (hero != null && hero.Ai != AiState.Dead) HeroMove(s, hero, dt);

            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.Kind == UnitKind.Hero || u.Ai == AiState.Dead) continue;
                Integrate(s, u, u.Vel, dt);
            }
            Separate(s);
        }

        public static float HeroSpeed(GameSession s)
        {
            var h = s.State.Hero; var b = s.Balance.hero;
            return b.speed * (h.Mounted ? b.mountedSpeedMult : 1f) * (1f + HeroSystem.BuffValue(h, "speed") + HeroSystem.Affix(s, "speed"));
        }

        static void HeroMove(GameSession s, Unit hero, float dt)
        {
            var h = s.State.Hero;
            float speed = HeroSpeed(s);
            Vec2 v = Vec2.Zero;
            if (s.MoveInput.SqrLength > 0.01f)
            {
                v = s.MoveInput.Normalized * speed;
                hero.Path = null; hero.TargetId = 0; h.Pending = null; h.GatherProgress = 0;
            }
            else if (hero.Path != null)
            {
                v = FollowPath(hero, speed, dt);
            }
            hero.Vel = v;
            Integrate(s, hero, v, dt);
            if (v.SqrLength > 0.01f) hero.Facing = v.Normalized;
        }

        public static Vec2 FollowPath(Unit u, float speed, float dt)
        {
            if (u.Path == null || u.PathIndex >= u.Path.Count) { u.Path = null; return Vec2.Zero; }
            var target = u.Path[u.PathIndex];
            var d = target - u.Pos;
            float dist = d.Length;
            if (dist <= Math.Max(0.15f, speed * dt))
            {
                u.PathIndex++;
                if (u.PathIndex >= u.Path.Count) { u.Path = null; return d / dt; }
                return FollowPath(u, speed, dt);
            }
            return d * (speed / dist);
        }

        public static void Integrate(GameSession s, Unit u, Vec2 v, float dt)
        {
            if (v.SqrLength < 1e-6f) return;
            var map = s.Map;
            float cost = Math.Max(0.6f, map.TerrainAt(u.Pos).moveCost);
            var step = v * (dt / cost);
            var tryX = new Vec2(u.Pos.x + step.x, u.Pos.y);
            if (map.Walkable(tryX)) u.Pos = tryX;
            var tryY = new Vec2(u.Pos.x, u.Pos.y + step.y);
            if (map.Walkable(tryY)) u.Pos = tryY;
        }

        static void Separate(GameSession s)
        {
            var units = s.State.Units;
            int n = units.Count;
            for (int i = 0; i < n; i++)
            {
                var a = units[i];
                if (a.Ai == AiState.Dead) continue;
                for (int j = i + 1; j < n; j++)
                {
                    var b = units[j];
                    if (b.Ai == AiState.Dead) continue;
                    float r = a.Radius + b.Radius;
                    float dx = b.Pos.x - a.Pos.x, dy = b.Pos.y - a.Pos.y;
                    float d2 = dx * dx + dy * dy;
                    if (d2 >= r * r) continue;
                    float d = (float)Math.Sqrt(d2);
                    Vec2 dir = d > 1e-4f ? new Vec2(dx / d, dy / d) : new Vec2(1, 0);
                    float overlap = (r - d) * 0.5f;
                    // Герой тяжелее толпы: его толкают меньше.
                    float wa = a.Kind == UnitKind.Hero ? 0.2f : (b.Kind == UnitKind.Hero ? 1.8f : 1f);
                    float wb = 2f - wa;
                    Push(s, a, dir * (-overlap * wa));
                    Push(s, b, dir * (overlap * wb));
                }
            }
        }

        static void Push(GameSession s, Unit u, Vec2 delta)
        {
            var np = u.Pos + delta;
            if (s.Map.Walkable(np)) u.Pos = np;
        }

        public static void SetPath(GameSession s, Unit u, Vec2 target)
        {
            u.Path = s.Paths.FindPath(u.Pos, s.Map.Clamp(target));
            u.PathIndex = 0;
        }
    }
}
