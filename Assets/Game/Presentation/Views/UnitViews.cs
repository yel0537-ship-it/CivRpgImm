using System.Collections.Generic;
using Immortal.Core;
using UnityEngine;

namespace Immortal.Presentation
{
    /// <summary>
    /// Виды юнитов (капсулы), снарядов, добычи и эффектов. Позиции интерполируются между тиками симуляции.
    /// Обратная связь боя: вспышка при попадании, полоски здоровья, индикатор замаха.
    /// </summary>
    public sealed class UnitViews
    {
        sealed class View
        {
            public GameObject Go;
            public Transform Body, HpBack, HpFill, Telegraph, Mount;
            public Renderer BodyR, TelegraphR, HpFillR;
            public Color BaseColor;
            public float Flash, DeathAnim;
            public bool Dying;
            public float Height;
        }

        readonly Transform _root;
        readonly WorldView _world;
        GameSession _s;
        readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        readonly Stack<View> _pool = new Stack<View>();
        readonly Dictionary<int, GameObject> _projectiles = new Dictionary<int, GameObject>();
        readonly Dictionary<int, GameObject> _loot = new Dictionary<int, GameObject>();
        readonly List<Fx> _fx = new List<Fx>();
        readonly Stack<GameObject> _fxPool = new Stack<GameObject>();

        sealed class Fx { public GameObject Go; public float T, Life; public Vector3 From, To; public Color Color; }

        public UnitViews(Transform parent, WorldView world)
        {
            _root = new GameObject("Units").transform;
            _root.SetParent(parent, false);
            _world = world;
        }

        public void Bind(GameSession s)
        {
            Clear();
            _s = s;
        }

        public void Clear()
        {
            foreach (var v in _views.Values) Object.Destroy(v.Go);
            _views.Clear();
            foreach (var g in _projectiles.Values) Object.Destroy(g);
            _projectiles.Clear();
            foreach (var g in _loot.Values) Object.Destroy(g);
            _loot.Clear();
            foreach (var f in _fx) Object.Destroy(f.Go);
            _fx.Clear();
        }

        Color UnitColor(Unit u)
        {
            switch (u.Kind)
            {
                case UnitKind.Hero: return Look.HeroColor;
                case UnitKind.Companion: return Look.CompanionColor;
                case UnitKind.Npc: return u.Team == Teams.Wild ? Look.HostileNpcColor : u.CompanionId != null ? Color.Lerp(Look.CompanionColor, Color.white, 0.4f) : Look.NpcColor;
                case UnitKind.Leader:
                case UnitKind.Soldier:
                    var n = _s.NationByTeam(u.Team) ?? (u.Team == Teams.Player ? _s.PlayerNation : null);
                    if (n != null) return Look.Hex(n.Color);
                    break;
            }
            if (u.DefId != null && _s.Content.Enemies.TryGetValue(u.DefId, out var d)) return Look.Hex(d.color, Color.red);
            return Color.gray;
        }

        View Create(Unit u)
        {
            var v = new View();
            float scale = 1f;
            if (u.DefId != null && _s.Content.Enemies.TryGetValue(u.DefId, out var d)) scale = d.scale;
            if (u.Kind == UnitKind.Leader) scale = 1.25f;
            v.Height = 1.8f * scale;
            v.Go = new GameObject($"Unit {u.Name} #{u.Id}");
            v.Go.transform.SetParent(_root, false);
            v.BaseColor = UnitColor(u);
            bool quadruped = u.Kind == UnitKind.Animal || (u.DefId != null && (u.DefId.StartsWith("wolf") || u.DefId == "bear" || u.DefId == "shade_beast"));
            if (quadruped)
            {
                var body = Look.Prim(PrimitiveType.Capsule, v.Go.transform, v.BaseColor, new Vector3(0, 0.55f * scale, 0), new Vector3(0.7f, 0.75f, 0.7f) * scale);
                body.transform.localRotation = Quaternion.Euler(90, 0, 0);
                Look.Prim(PrimitiveType.Sphere, body.transform, v.BaseColor * 0.85f, new Vector3(0, 0.95f, -0.15f), new Vector3(0.7f, 0.45f, 0.6f));
                v.Body = body.transform; v.Height = 1.2f * scale;
            }
            else
            {
                var body = Look.Prim(PrimitiveType.Capsule, v.Go.transform, v.BaseColor, new Vector3(0, 0.9f * scale, 0), new Vector3(0.8f, 0.9f, 0.8f) * scale);
                v.Body = body.transform;
                // «Нос» показывает направление взгляда.
                Look.Prim(PrimitiveType.Cube, body.transform, v.BaseColor * 0.6f, new Vector3(0, 0.45f, 0.42f), new Vector3(0.35f, 0.15f, 0.25f));
                if (u.Kind == UnitKind.Leader || u.Boss)
                    Look.Prim(PrimitiveType.Cube, body.transform, new Color(1f, 0.85f, 0.2f), new Vector3(0, 1.15f, 0), new Vector3(0.6f, 0.2f, 0.6f));
                if (u.Kind == UnitKind.Spirit)
                    Look.SetColor(body.GetComponent<Renderer>(), v.BaseColor, 0.5f);
            }
            v.BodyR = v.Body.GetComponent<Renderer>();
            var mountGo = Look.Prim(PrimitiveType.Capsule, v.Go.transform, new Color(0.5f, 0.32f, 0.18f), new Vector3(0, 0.75f, 0), new Vector3(0.8f, 1.1f, 0.8f));
            mountGo.transform.localRotation = Quaternion.Euler(90, 0, 0);
            v.Mount = mountGo.transform; v.Mount.gameObject.SetActive(false);
            v.HpBack = Look.Prim(PrimitiveType.Cube, v.Go.transform, new Color(0.1f, 0.1f, 0.1f), new Vector3(0, v.Height + 0.6f, 0), new Vector3(1.2f, 0.12f, 0.05f)).transform;
            v.HpFill = Look.Prim(PrimitiveType.Cube, v.HpBack, Color.green, new Vector3(0, 0, -0.6f), new Vector3(1f, 0.8f, 1f)).transform;
            v.HpFillR = v.HpFill.GetComponent<Renderer>();
            v.Telegraph = Look.Prim(PrimitiveType.Cylinder, v.Go.transform, new Color(1f, 0.15f, 0.1f, 0.45f), new Vector3(0, 0.05f, 0), new Vector3(1, 0.01f, 1), true).transform;
            v.TelegraphR = v.Telegraph.GetComponent<Renderer>();
            v.Telegraph.gameObject.SetActive(false);
            return v;
        }

        public void Sync(float alpha, float dt)
        {
            if (_s == null) return;
            var hero = _s.HeroUnit;
            Vec2 heroPos = hero != null ? Vec2.Lerp(hero.PrevPos, hero.Pos, alpha) : Vec2.Zero;
            bool heroSmoke = _s.State.Hero.InSmoke;
            float vision = heroSmoke ? _s.Balance.vision.smokeRadius + 2f : _s.Balance.vision.radius + 6f;
            var seen = new HashSet<int>();
            foreach (var u in _s.State.Units)
            {
                seen.Add(u.Id);
                if (!_views.TryGetValue(u.Id, out var v)) { v = Create(u); _views[u.Id] = v; }
                var p = Vec2.Lerp(u.PrevPos, u.Pos, alpha);
                bool visible = u.Kind == UnitKind.Hero || u.Kind == UnitKind.Companion || Vec2.Distance(p, heroPos) < vision;
                if (u.InSmoke != heroSmoke && u.Kind != UnitKind.Hero && u.Kind != UnitKind.Companion) visible = false; // снаружи содержимое дыма не видно
                v.Go.SetActive(visible);
                if (!visible) continue;
                v.Go.transform.position = _world.Ground(p);
                var face = new Vector3(u.Facing.x, 0, u.Facing.y);
                if (face.sqrMagnitude > 0.01f) v.Body.parent.rotation = Quaternion.Slerp(v.Body.parent.rotation, Quaternion.LookRotation(face), 1f - Mathf.Exp(-dt * 14f));
                v.HpBack.rotation = Quaternion.identity;
                v.Mount.gameObject.SetActive(u.Kind == UnitKind.Hero && _s.State.Hero.Mounted);
                if (u.Kind == UnitKind.Hero) v.Body.localPosition = new Vector3(0, _s.State.Hero.Mounted ? 1.6f : 0.9f, 0);

                // Полоска здоровья: у врагов всегда, у своих — если ранены.
                float frac = u.MaxHp > 0 ? Mathf.Clamp01(u.Hp / u.MaxHp) : 0;
                bool showHp = u.Ai != AiState.Dead && (frac < 0.999f || (u.Team != Teams.Player && u.Team != Teams.Neutral));
                v.HpBack.gameObject.SetActive(showHp);
                if (showHp)
                {
                    v.HpFill.localScale = new Vector3(frac, 0.8f, 1.2f);
                    v.HpFill.localPosition = new Vector3(-(1 - frac) * 0.5f, 0, -0.6f);
                    Look.SetColor(v.HpFillR, u.Team == Teams.Player ? new Color(0.3f, 0.85f, 0.4f) : new Color(0.9f, 0.2f, 0.15f));
                }

                // Индикатор замаха.
                bool tele = u.Ai == AiState.Windup && u.Kind != UnitKind.Hero;
                v.Telegraph.gameObject.SetActive(tele);
                if (tele)
                {
                    float prog = 1f - Mathf.Clamp01(u.StateTimer / Mathf.Max(0.05f, u.Windup));
                    float r = (u.AttackRange + 0.6f) * (u.Behavior == "ranged" ? 0.4f : 1f);
                    var dir = new Vector3(u.AttackDir.x, 0, u.AttackDir.y);
                    v.Telegraph.position = v.Go.transform.position + dir * r * 0.55f + Vector3.up * 0.06f;
                    v.Telegraph.localScale = new Vector3(r * (0.4f + prog * 0.8f), 0.01f, r * (0.4f + prog * 0.8f));
                    Look.SetColor(v.TelegraphR, new Color(1f, 0.15f, 0.1f, 0.25f + prog * 0.45f));
                }

                // Вспышка при попадании и смерть.
                if (v.Flash > 0) { v.Flash -= dt; Look.SetColor(v.BodyR, Color.white, 1.5f); }
                else Look.SetColor(v.BodyR, u.Ai == AiState.Dead ? v.BaseColor * 0.4f : v.BaseColor, u.Kind == UnitKind.Spirit ? 0.5f : 0f);
                if (u.Ai == AiState.Dead)
                {
                    v.DeathAnim = Mathf.Min(1f, v.DeathAnim + dt * 2f);
                    v.Body.parent.localScale = new Vector3(1f, 1f - v.DeathAnim * 0.8f, 1f);
                }
                else if (v.DeathAnim > 0) { v.DeathAnim = 0; v.Body.parent.localScale = Vector3.one; }
            }
            var drop = new List<int>();
            foreach (var kv in _views) if (!seen.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var id in drop) { Object.Destroy(_views[id].Go); _views.Remove(id); }

            SyncProjectiles(alpha);
            SyncLoot(dt);
            UpdateFx(dt);
        }

        void SyncProjectiles(float alpha)
        {
            var seen = new HashSet<int>();
            foreach (var p in _s.State.Projectiles)
            {
                seen.Add(p.Id);
                if (!_projectiles.TryGetValue(p.Id, out var go))
                {
                    Color c = Color.white;
                    if (p.AbilityId != null && _s.Content.Abilities.TryGetValue(p.AbilityId, out var a)) c = Look.Hex(a.color, Color.white);
                    else if (p.Mystic) c = new Color(0.75f, 0.5f, 1f);
                    go = Look.Prim(PrimitiveType.Sphere, _root, c, Vector3.zero, Vector3.one * (p.Aoe > 0 ? 0.7f : 0.35f));
                    Look.SetColor(go.GetComponent<Renderer>(), c, 0.8f);
                    _projectiles[p.Id] = go;
                }
                go.transform.position = _world.Ground(p.Pos, 1.1f);
            }
            var drop = new List<int>();
            foreach (var kv in _projectiles) if (!seen.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var id in drop) { Object.Destroy(_projectiles[id]); _projectiles.Remove(id); }
        }

        void SyncLoot(float dt)
        {
            var seen = new HashSet<int>();
            foreach (var l in _s.State.Loot)
            {
                seen.Add(l.Id);
                if (!_loot.TryGetValue(l.Id, out var go))
                {
                    string item = l.Item != null ? l.Item.DefId : l.Resource;
                    var c = _s.Content.Items.TryGetValue(item, out var d) ? Look.Hex(d.color) : Color.white;
                    go = Look.Prim(l.Item != null ? PrimitiveType.Cube : PrimitiveType.Sphere, _root, c, Vector3.zero, Vector3.one * (l.Item != null ? 0.45f : 0.35f));
                    Look.SetColor(go.GetComponent<Renderer>(), c, item == "essence" ? 1.2f : 0.3f);
                    _loot[l.Id] = go;
                }
                go.transform.position = _world.Ground(l.Pos, 0.4f + Mathf.Sin(Time.time * 3f + l.Id) * 0.12f);
                go.transform.Rotate(0, dt * 90f, 0);
            }
            var drop = new List<int>();
            foreach (var kv in _loot) if (!seen.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var id in drop) { Object.Destroy(_loot[id]); _loot.Remove(id); }
        }

        // ---------- События боя ----------
        public void OnHit(int unitId)
        {
            if (_views.TryGetValue(unitId, out var v)) v.Flash = 0.1f;
        }

        public Vector3? UnitWorldPos(int unitId, float lift)
        {
            if (_views.TryGetValue(unitId, out var v) && v.Go.activeSelf) return v.Go.transform.position + Vector3.up * (v.Height + lift);
            return null;
        }

        public void Burst(Vector3 pos, float radius, Color color, float life = 0.35f)
        {
            var go = _fxPool.Count > 0 ? _fxPool.Pop() : Look.Prim(PrimitiveType.Sphere, _root, color, Vector3.zero, Vector3.one, true, "fx");
            go.SetActive(true);
            go.transform.position = pos;
            _fx.Add(new Fx { Go = go, Life = life, From = Vector3.one * radius * 0.3f, To = Vector3.one * radius * 2f, Color = color });
        }

        public void Slash(Vector3 pos, Vector3 dir, float range, Color color)
        {
            var go = _fxPool.Count > 0 ? _fxPool.Pop() : Look.Prim(PrimitiveType.Sphere, _root, color, Vector3.zero, Vector3.one, true, "fx");
            go.SetActive(true);
            go.transform.position = pos + dir * range * 0.5f + Vector3.up * 0.8f;
            _fx.Add(new Fx { Go = go, Life = 0.18f, From = new Vector3(range, 0.1f, range * 0.5f), To = new Vector3(range * 1.3f, 0.15f, range * 0.8f), Color = color });
        }

        void UpdateFx(float dt)
        {
            for (int i = _fx.Count - 1; i >= 0; i--)
            {
                var f = _fx[i];
                f.T += dt;
                float k = Mathf.Clamp01(f.T / f.Life);
                f.Go.transform.localScale = Vector3.Lerp(f.From, f.To, k);
                Look.SetColor(f.Go.GetComponent<Renderer>(), new Color(f.Color.r, f.Color.g, f.Color.b, (1 - k) * 0.7f));
                if (k >= 1) { f.Go.SetActive(false); _fxPool.Push(f.Go); _fx.RemoveAt(i); }
            }
        }
    }
}
