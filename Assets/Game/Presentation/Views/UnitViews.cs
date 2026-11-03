using System.Collections.Generic;
using Immortal.Core;
using UnityEngine;

namespace Immortal.Presentation
{
    /// <summary>
    /// Виды юнитов, снарядов, добычи и эффектов. Позиции интерполируются между тиками симуляции.
    /// Обратная связь боя: вспышка при попадании, полоски здоровья, сектор замаха, анимации через IUnitAnimator.
    /// Что именно рисуется (модель или примитив), решает ViewFactory.
    /// </summary>
    public sealed class UnitViews
    {
        sealed class View
        {
            public UnitRig Rig;
            public Transform HpBack, HpFill, Telegraph;
            public Renderer TelegraphR, HpFillR;
            public float Flash;
            public bool Flashed, DeadTint;
            public bool WasDead;
            public Vector3 LastPos;
            public float Speed;
        }

        readonly Transform _root;
        readonly WorldView _world;
        GameSession _s;
        readonly Dictionary<int, View> _views = new Dictionary<int, View>();
        readonly Dictionary<int, GameObject> _projectiles = new Dictionary<int, GameObject>();
        readonly Dictionary<int, GameObject> _loot = new Dictionary<int, GameObject>();
        readonly List<Fx> _fx = new List<Fx>();
        readonly Stack<GameObject> _fxPool = new Stack<GameObject>();
        static Mesh _sector;

        sealed class Fx { public GameObject Go; public float T, Life; public Vector3 From, To; public Color Color; }

        public UnitViews(Transform parent, WorldView world)
        {
            _root = new GameObject("Units").transform;
            _root.SetParent(parent, false);
            _world = world;
        }

        public void Bind(GameSession s) { Clear(); _s = s; }

        public void Clear()
        {
            foreach (var v in _views.Values) Object.Destroy(v.Rig.Root);
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

        /// <summary>Плоский сектор (угол 100°) для индикатора замаха — показывает, куда придётся удар.</summary>
        static Mesh Sector()
        {
            if (_sector != null) return _sector;
            const int seg = 16; const float arc = 100f * Mathf.Deg2Rad;
            var verts = new Vector3[seg + 2]; var tris = new int[seg * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i <= seg; i++)
            {
                float a = -arc / 2 + arc * i / seg;
                verts[i + 1] = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a));
            }
            for (int i = 0; i < seg; i++) { tris[i * 3] = 0; tris[i * 3 + 1] = i + 1; tris[i * 3 + 2] = i + 2; }
            _sector = new Mesh { name = "Sector", vertices = verts, triangles = tris };
            _sector.RecalculateNormals();
            return _sector;
        }

        View Create(Unit u)
        {
            var v = new View { Rig = ViewFactory.CreateUnit(_s, u, _root, UnitColor(u)) };
            var t = v.Rig.Root.transform;
            v.HpBack = Look.Prim(PrimitiveType.Cube, t, new Color(0.1f, 0.1f, 0.1f), new Vector3(0, v.Rig.Height + 0.6f, 0), new Vector3(1.2f, 0.12f, 0.05f), false, "hp").transform;
            v.HpFill = Look.Prim(PrimitiveType.Cube, v.HpBack, Color.green, new Vector3(0, 0, -0.6f), new Vector3(1f, 0.8f, 1f), false, "hpfill").transform;
            v.HpFillR = v.HpFill.GetComponent<Renderer>();
            var tele = new GameObject("telegraph");
            tele.transform.SetParent(_root, false);
            tele.AddComponent<MeshFilter>().sharedMesh = Sector();
            v.TelegraphR = tele.AddComponent<MeshRenderer>();
            v.TelegraphR.sharedMaterial = Look.Transparent;
            v.Telegraph = tele.transform;
            tele.SetActive(false);
            v.LastPos = Look.ToWorld(u.Pos, 0);
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
                var rig = v.Rig;
                var p = Vec2.Lerp(u.PrevPos, u.Pos, alpha);
                bool visible = u.Kind == UnitKind.Hero || u.Kind == UnitKind.Companion || Vec2.Distance(p, heroPos) < vision;
                if (u.InSmoke != heroSmoke && u.Kind != UnitKind.Hero && u.Kind != UnitKind.Companion) visible = false; // снаружи содержимое дыма не видно
                rig.Root.SetActive(visible);
                if (!visible) { v.Telegraph.gameObject.SetActive(false); continue; }
                var wp = _world.Ground(p);
                v.Speed = dt > 0 ? Mathf.Lerp(v.Speed, new Vector2(wp.x - v.LastPos.x, wp.z - v.LastPos.z).magnitude / dt, 0.3f) : 0;
                v.LastPos = wp;
                rig.Root.transform.position = wp;
                var face = new Vector3(u.Facing.x, 0, u.Facing.y);
                if (face.sqrMagnitude > 0.01f) rig.Root.transform.rotation = Quaternion.Slerp(rig.Root.transform.rotation, Quaternion.LookRotation(face), 1f - Mathf.Exp(-dt * 14f));
                v.HpBack.rotation = Quaternion.identity;

                // Анимация.
                bool isHero = u.Kind == UnitKind.Hero;
                var h = _s.State.Hero;
                var st = new UnitAnimState
                {
                    Speed = v.Speed, Dead = u.Ai == AiState.Dead, Windup = u.Ai == AiState.Windup,
                    WindupProgress = u.Ai == AiState.Windup ? 1f - Mathf.Clamp01(u.StateTimer / Mathf.Max(0.05f, u.Windup)) : 0,
                    Gather = isHero && h.Pending != null && h.Pending.Kind == "gather" && h.GatherProgress > 0,
                    Mounted = isHero && h.Mounted, InCombat = isHero ? h.CombatTimer > 0 : u.Ai == AiState.Chase || u.Ai == AiState.Windup
                };
                rig.Animator?.Tick(st, dt);
                if (v.WasDead && !st.Dead) rig.Animator?.Fire(AnimEvent.Revive);
                if (!v.WasDead && st.Dead) rig.Animator?.Fire(AnimEvent.Death);
                v.WasDead = st.Dead;
                if (isHero)
                {
                    ViewFactory.SetMounted(_s, rig, h.Mounted);
                    var w = h.WeaponId != null ? Core.Character.HeroSystem.Item(h, h.WeaponId) : null;
                    ViewFactory.UpdateWeapon(_s, rig, w?.DefId);
                }

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

                // Сектор замаха: растёт и краснеет к моменту удара.
                bool tele = st.Windup && !isHero;
                v.Telegraph.gameObject.SetActive(tele);
                if (tele)
                {
                    float r = u.Behavior == "ranged" ? 1.2f : u.AttackRange + 0.6f;
                    var dir = new Vector3(u.AttackDir.x, 0, u.AttackDir.y);
                    v.Telegraph.position = wp + Vector3.up * 0.08f;
                    v.Telegraph.rotation = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : Quaternion.identity;
                    float k = 0.4f + st.WindupProgress * 0.6f;
                    v.Telegraph.localScale = new Vector3(r * k, 1, r * k);
                    Look.SetColor(v.TelegraphR, new Color(1f, 0.15f, 0.1f, 0.2f + st.WindupProgress * 0.5f));
                }

                // Вспышка попадания.
                if (v.Flash > 0) { v.Flash -= dt; ViewFactory.ApplyFlash(rig, 1, false); v.Flashed = true; }
                else if (v.Flashed || st.Dead != v.DeadTint) { ViewFactory.ApplyFlash(rig, 0, st.Dead); v.Flashed = false; v.DeadTint = st.Dead; }
                if (rig.IsPrefab && rig.Punch > 0)
                {
                    rig.Punch -= dt * 6f;
                    rig.Root.transform.localScale = Vector3.one * (1f + Mathf.Max(0, rig.Punch) * 0.12f);
                }
            }
            var drop = new List<int>();
            foreach (var kv in _views) if (!seen.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var id in drop) { Object.Destroy(_views[id].Telegraph.gameObject); Object.Destroy(_views[id].Rig.Root); _views.Remove(id); }

            SyncProjectiles();
            SyncLoot(dt);
            UpdateFx(dt);
        }

        void SyncProjectiles()
        {
            var seen = new HashSet<int>();
            foreach (var p in _s.State.Projectiles)
            {
                seen.Add(p.Id);
                if (!_projectiles.TryGetValue(p.Id, out var go)) { go = ViewFactory.CreateProjectile(_s, p, _root); _projectiles[p.Id] = go; }
                go.transform.position = _world.Ground(p.Pos, 1.1f);
                var dir = new Vector3(p.Vel.x, 0, p.Vel.y);
                if (dir.sqrMagnitude > 0.01f) go.transform.rotation = Quaternion.LookRotation(dir);
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
                if (!_loot.TryGetValue(l.Id, out var go)) { go = ViewFactory.CreateLoot(_s, l, _root); _loot[l.Id] = go; }
                go.transform.position = _world.Ground(l.Pos, 0.4f + Mathf.Sin(Time.time * 3f + l.Id) * 0.12f);
                go.transform.Rotate(0, dt * 90f, 0);
            }
            var drop = new List<int>();
            foreach (var kv in _loot) if (!seen.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var id in drop) { Object.Destroy(_loot[id]); _loot.Remove(id); }
        }

        // ---------- События ----------
        public void OnHit(int unitId)
        {
            if (!_views.TryGetValue(unitId, out var v)) return;
            v.Flash = 0.1f;
            v.Rig.Punch = 1f;
            v.Rig.Animator?.Fire(AnimEvent.Hit);
        }

        public void OnStrike(int unitId, bool cast)
        {
            if (_views.TryGetValue(unitId, out var v)) v.Rig.Animator?.Fire(cast ? AnimEvent.Cast : AnimEvent.Attack);
        }

        public Vector3? UnitWorldPos(int unitId, float lift)
        {
            if (_views.TryGetValue(unitId, out var v) && v.Rig.Root.activeSelf) return v.Rig.Root.transform.position + Vector3.up * (v.Rig.Height + lift);
            return null;
        }

        /// <summary>Вспышка-эффект: префаб Fx/&lt;key&gt;, иначе полупрозрачная сфера.</summary>
        public void Burst(string key, Vector3 pos, float radius, Color color, float life = 0.35f)
        {
            if (key == "__none" || ViewFactory.TrySpawnFx(_root, key, pos, Vector3.zero, radius)) return;
            var go = _fxPool.Count > 0 ? _fxPool.Pop() : Look.Prim(PrimitiveType.Sphere, _root, color, Vector3.zero, Vector3.one, true, "fx");
            go.SetActive(true);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.identity;
            _fx.Add(new Fx { Go = go, Life = life, From = Vector3.one * radius * 0.3f, To = Vector3.one * radius * 2f, Color = color });
        }

        public void Slash(string key, Vector3 pos, Vector3 dir, float range, Color color)
        {
            if (ViewFactory.TrySpawnFx(_root, key, pos + Vector3.up * 0.8f, dir, range)) return;
            var go = _fxPool.Count > 0 ? _fxPool.Pop() : Look.Prim(PrimitiveType.Sphere, _root, color, Vector3.zero, Vector3.one, true, "fx");
            go.SetActive(true);
            go.transform.position = pos + dir * range * 0.5f + Vector3.up * 0.8f;
            go.transform.rotation = dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : Quaternion.identity;
            _fx.Add(new Fx { Go = go, Life = 0.18f, From = new Vector3(range * 0.5f, 0.1f, range), To = new Vector3(range * 0.8f, 0.15f, range * 1.3f), Color = color });
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
