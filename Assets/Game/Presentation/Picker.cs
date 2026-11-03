using Immortal.Core;
using Immortal.Core.Commands;

namespace Immortal.Presentation
{
    public struct PickResult
    {
        public IGameCommand Command;
        public string Label;
        public Vec2 Pos;
        public float Radius;
        /// <summary>enemy, npc, loot, building, poi, node — для цвета кольца наведения.</summary>
        public string Kind;
    }

    /// <summary>Выбор объекта под курсором: ближайший в радиусе от точки пересечения луча с землёй.</summary>
    public static class Picker
    {
        public static PickResult Pick(GameSession s, Vec2 p)
        {
            var res = new PickResult();
            float best = float.MaxValue;
            bool heroSmoke = s.State.Hero.InSmoke;

            void Consider(float score, IGameCommand cmd, string label, Vec2 pos, float radius, string kind)
            {
                if (score < best) { best = score; res.Command = cmd; res.Label = label; res.Pos = pos; res.Radius = radius; res.Kind = kind; }
            }

            foreach (var u in s.State.Units)
            {
                if (u.Kind == UnitKind.Hero || u.Ai == AiState.Dead) continue;
                if (u.InSmoke != heroSmoke && u.Kind != UnitKind.Companion) continue;
                float d = Vec2.Distance(u.Pos, p);
                if (d > u.Radius + 1.1f) continue;
                bool hostile = u.Team == Teams.Wild || s.IsHostile(Teams.Player, u.Team) || u.Kind == UnitKind.Animal;
                if (hostile) Consider(d - 0.6f, new AttackUnit(u.Id), $"{u.Name} ({u.Hp:0}/{u.MaxHp:0})", u.Pos, u.Radius + 0.4f, "enemy");
                else Consider(d - 0.3f, Interact.Unit(u.Id), u.Kind == UnitKind.Leader ? $"Встреча: {u.Name}" : $"Говорить: {u.Name}", u.Pos, u.Radius + 0.4f, "npc");
            }
            foreach (var l in s.State.Loot)
            {
                float d = Vec2.Distance(l.Pos, p);
                if (d < 1.2f) Consider(d, new MoveTo(l.Pos), "Подобрать: " + (l.Item != null ? s.Content.ItemName(l.Item.DefId) : s.Content.ItemName(l.Resource)), l.Pos, 0.6f, "loot");
            }
            foreach (var c in s.State.Cities)
            {
                if (Vec2.Distance(c.Pos, p) > 70f) continue;
                foreach (var b in c.Buildings)
                {
                    var d = s.Content.Buildings[b.DefId];
                    float dist = Vec2.Distance(b.Pos, p);
                    if (dist <= d.size * 0.5f + 0.4f) Consider(dist + 0.2f, Interact.Building(c.Id, b.Slot), $"{d.name} ({c.Name})", b.Pos, d.size * 0.6f, "building");
                }
            }
            foreach (var poi in s.State.Pois)
            {
                if (!poi.Discovered) continue;
                var def = s.Content.Poi[poi.DefId];
                if (def.kind == "zone") continue;
                float d = Vec2.Distance(poi.Pos, p);
                if (d <= System.Math.Max(2.5f, System.Math.Min(poi.Radius, 4.5f))) Consider(d + 0.4f, Interact.Poi(poi.Id), poi.Name, poi.Pos, 2.5f, "poi");
            }
            s.Map.TileXY(p, out int tx, out int ty);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!s.Map.InBounds(tx + dx, ty + dy)) continue;
                int t = s.Map.TileIndex(tx + dx, ty + dy);
                var node = s.Map.NodeAt(t);
                if (node == null) continue;
                float d = Vec2.Distance(s.Map.TileCenter(t), p);
                if (d <= 1.4f) Consider(d + 0.5f, Interact.Gather(t), $"Собрать: {node.name}", s.Map.TileCenter(t), 1f, "node");
            }
            return res;
        }
    }
}
