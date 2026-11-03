using System.Collections.Generic;
using Immortal.Core;
using UnityEngine;

namespace Immortal.Presentation
{
    /// <summary>
    /// Маркеры на земле: зоны отложенных ударов (каменный дождь, сокрушение) с заполнением к моменту удара,
    /// кольцо под объектом под курсором, отметка точки клика. Префабы-замены: fx:area_marker, fx:hover_ring, fx:click_marker.
    /// </summary>
    public sealed class Markers
    {
        readonly Transform _root;
        readonly WorldView _world;
        GameSession _s;
        static Mesh _ring, _disc;
        readonly Dictionary<AreaEffect, (GameObject ring, GameObject fill, float startDelay)> _areas = new Dictionary<AreaEffect, (GameObject, GameObject, float)>();
        GameObject _hover, _click;
        float _clickT;

        public Markers(Transform parent, WorldView world)
        {
            _root = new GameObject("Markers").transform;
            _root.SetParent(parent, false);
            _world = world;
            _hover = Make("hover_ring", Ring(0.82f), new Color(1, 1, 1, 0.5f));
            _click = Make("click_marker", Ring(0.7f), new Color(1f, 0.9f, 0.5f, 0.7f));
            _hover.SetActive(false); _click.SetActive(false);
        }

        public void Bind(GameSession s)
        {
            _s = s;
            foreach (var kv in _areas) { Object.Destroy(kv.Value.ring); Object.Destroy(kv.Value.fill); }
            _areas.Clear();
        }

        GameObject Make(string fxKey, Mesh mesh, Color color)
        {
            var ve = VisualCatalog.Resolve("fx", fxKey);
            if (ve != null)
            {
                var m = Object.Instantiate(ve.Prefab, _root);
                return m;
            }
            var go = new GameObject(fxKey);
            go.transform.SetParent(_root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Look.Transparent;
            Look.SetColor(r, color);
            return go;
        }

        static Mesh Ring(float inner)
        {
            const int seg = 48;
            var v = new Vector3[seg * 2]; var t = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg;
                var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v[i * 2] = d * inner; v[i * 2 + 1] = d;
                int n = (i + 1) % seg;
                t[i * 6] = i * 2; t[i * 6 + 1] = n * 2; t[i * 6 + 2] = i * 2 + 1;
                t[i * 6 + 3] = i * 2 + 1; t[i * 6 + 4] = n * 2; t[i * 6 + 5] = n * 2 + 1;
            }
            var m = new Mesh { name = "Ring", vertices = v, triangles = t };
            m.RecalculateNormals();
            return m;
        }

        static Mesh Disc()
        {
            if (_disc != null) return _disc;
            const int seg = 40;
            var v = new Vector3[seg + 1]; var t = new int[seg * 3];
            for (int i = 0; i < seg; i++) { float a = i * Mathf.PI * 2 / seg; v[i + 1] = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }
            for (int i = 0; i < seg; i++) { t[i * 3] = 0; t[i * 3 + 1] = (i + 1) % seg + 1; t[i * 3 + 2] = i + 1; }
            _disc = new Mesh { name = "Disc", vertices = v, triangles = t };
            _disc.RecalculateNormals();
            return _disc;
        }

        public void Update(float dt)
        {
            if (_s == null) return;
            if (_ring == null) _ring = Ring(0.9f);
            var alive = new HashSet<AreaEffect>(_s.State.Areas);
            foreach (var a in _s.State.Areas)
            {
                if (!_areas.TryGetValue(a, out var e))
                {
                    bool ours = a.Team == Teams.Player;
                    var col = ours ? new Color(1f, 0.85f, 0.3f, 0.6f) : new Color(1f, 0.25f, 0.15f, 0.7f);
                    var ring = Make("area_marker", _ring, col);
                    var fill = new GameObject("area_fill");
                    fill.transform.SetParent(_root, false);
                    fill.AddComponent<MeshFilter>().sharedMesh = Disc();
                    var fr = fill.AddComponent<MeshRenderer>(); fr.sharedMaterial = Look.Transparent;
                    Look.SetColor(fr, new Color(col.r, col.g, col.b, 0.25f));
                    e = (ring, fill, Mathf.Max(0.05f, a.Delay));
                    _areas[a] = e;
                }
                var pos = _world.Ground(a.Pos, 0.1f);
                e.ring.transform.position = pos;
                e.ring.transform.localScale = new Vector3(a.Radius, 1, a.Radius);
                float k = a.Pulses > 1 ? 1f : 1f - Mathf.Clamp01(a.Delay / e.startDelay);
                e.fill.transform.position = pos + Vector3.up * 0.01f;
                e.fill.transform.localScale = new Vector3(a.Radius * k, 1, a.Radius * k);
            }
            var drop = new List<AreaEffect>();
            foreach (var kv in _areas) if (!alive.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var a in drop) { Object.Destroy(_areas[a].ring); Object.Destroy(_areas[a].fill); _areas.Remove(a); }

            if (_click.activeSelf)
            {
                _clickT += dt;
                float s = Mathf.Lerp(1.2f, 0.3f, _clickT / 0.4f);
                _click.transform.localScale = new Vector3(s, 1, s);
                if (_clickT > 0.4f) _click.SetActive(false);
            }
        }

        public void Hover(Vec2? pos, float radius, Color color)
        {
            _hover.SetActive(pos.HasValue);
            if (!pos.HasValue) return;
            _hover.transform.position = _world.Ground(pos.Value, 0.12f);
            float pulse = 1f + Mathf.Sin(Time.time * 6f) * 0.06f;
            _hover.transform.localScale = new Vector3(radius * pulse, 1, radius * pulse);
            var r = _hover.GetComponent<MeshRenderer>();
            if (r != null && r.sharedMaterial == Look.Transparent) Look.SetColor(r, color);
        }

        public void Click(Vec2 pos)
        {
            _click.SetActive(true);
            _click.transform.position = _world.Ground(pos, 0.12f);
            _clickT = 0;
        }
    }
}
