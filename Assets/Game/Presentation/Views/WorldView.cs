using System.Collections.Generic;
using Immortal.Core;
using Immortal.Core.Civilization;
using Immortal.Core.Content;
using Immortal.Core.World;
using UnityEngine;

namespace Immortal.Presentation
{
    /// <summary>
    /// Потоковая загрузка мира вокруг героя: чанки местности (цвета вершин + туман), ресурсные узлы (инстансинг),
    /// точки интереса, постройки городов, дым мистических зон, знамёна границ, армии вдали.
    /// </summary>
    public sealed class WorldView
    {
        const int LoadRadius = 2;
        readonly Transform _root;
        GameSession _s;
        readonly Dictionary<long, Chunk> _chunks = new Dictionary<long, Chunk>();
        readonly Stack<Chunk> _chunkPool = new Stack<Chunk>();
        readonly Dictionary<string, GameObject> _pois = new Dictionary<string, GameObject>();
        readonly Dictionary<string, GameObject> _cities = new Dictionary<string, GameObject>();
        readonly Dictionary<string, string> _citySig = new Dictionary<string, string>();
        readonly Dictionary<string, GameObject> _zones = new Dictionary<string, GameObject>();
        readonly Dictionary<string, GameObject> _armies = new Dictionary<string, GameObject>();
        readonly List<GameObject> _banners = new List<GameObject>();
        int _bannerCell = -1, _bannerUsed;
        int _revision = -1, _fogRevision = -1;
        float _rebuildCooldown;
        public bool HeroInSmoke;

        sealed class Chunk
        {
            public int Cx, Cy;
            public GameObject Go;
            public Mesh Mesh;
            public Vector3[] Verts; public Color[] Colors; public Vector3[] Normals; public int[] Tris;
            public readonly Dictionary<string, List<Matrix4x4>> Nodes = new Dictionary<string, List<Matrix4x4>>();
            public readonly Dictionary<string, List<Vector4>> NodeColors = new Dictionary<string, List<Vector4>>();
        }

        public WorldView(Transform parent)
        {
            _root = new GameObject("World").transform;
            _root.SetParent(parent, false);
        }

        public void Bind(GameSession s)
        {
            Clear();
            _s = s;
            _revision = -1; _fogRevision = -1; _bannerCell = -1;
        }

        public void Clear()
        {
            foreach (var c in _chunks.Values) { c.Go.SetActive(false); _chunkPool.Push(c); }
            _chunks.Clear();
            foreach (var d in new[] { _pois, _cities, _zones, _armies }) { foreach (var g in d.Values) Object.Destroy(g); d.Clear(); }
            _citySig.Clear();
            foreach (var b in _banners) Object.Destroy(b);
            _banners.Clear();
        }

        // ---------- Высоты ----------
        public float CornerHeight(int vx, int vy)
        {
            var map = _s.Map; float sum = 0; int n = 0;
            for (int dy = -1; dy <= 0; dy++)
            for (int dx = -1; dx <= 0; dx++)
            {
                int tx = vx + dx, ty = vy + dy;
                if (!map.InBounds(tx, ty)) continue;
                sum += TileHeight(ty * map.Size + tx); n++;
            }
            return n > 0 ? sum / n : 0;
        }

        float TileHeight(int idx)
        {
            var t = _s.Map.TerrainAt(idx);
            float h = _s.Map.Height[idx];
            if (t.id == "water") return Mathf.Min(h, -0.5f);
            return h;
        }

        public float GroundHeight(Vec2 p)
        {
            if (_s == null) return 0;
            float ts = _s.Map.TileSize;
            float fx = p.x / ts, fy = p.y / ts;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            float a = CornerHeight(x0, y0), b = CornerHeight(x0 + 1, y0), c = CornerHeight(x0, y0 + 1), d = CornerHeight(x0 + 1, y0 + 1);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        public Vector3 Ground(Vec2 p, float lift = 0f) => new Vector3(p.x, GroundHeight(p) + lift, p.y);

        // ---------- Обновление ----------
        public void Update(Vec2 heroPos, float dt)
        {
            if (_s == null) return;
            var map = _s.Map;
            int chunkTiles = _s.Balance.chunkTiles;
            float chunkW = chunkTiles * map.TileSize;
            int hcx = (int)(heroPos.x / chunkW), hcy = (int)(heroPos.y / chunkW);
            int maxC = (map.Size + chunkTiles - 1) / chunkTiles;

            // Изменения мира: перестраиваем загруженные чанки (не чаще раза в секунду).
            _rebuildCooldown -= dt;
            bool terrainChanged = _s.State.WorldMods.Revision != _revision && _rebuildCooldown <= 0;
            if (terrainChanged) { _revision = _s.State.WorldMods.Revision; _rebuildCooldown = 1f; foreach (var c in _chunks.Values) BuildChunk(c); }
            if (_s.FogRevision != _fogRevision)
            {
                _fogRevision = _s.FogRevision;
                foreach (var c in _chunks.Values)
                    if (Mathf.Abs(c.Cx - hcx) <= 1 && Mathf.Abs(c.Cy - hcy) <= 1) { Recolor(c); RebuildNodes(c); }
            }

            // Загрузка/выгрузка вокруг героя.
            var keep = new HashSet<long>();
            for (int cy = hcy - LoadRadius; cy <= hcy + LoadRadius; cy++)
            for (int cx = hcx - LoadRadius; cx <= hcx + LoadRadius; cx++)
            {
                if (cx < 0 || cy < 0 || cx >= maxC || cy >= maxC) continue;
                long key = ((long)cx << 32) | (uint)cy;
                keep.Add(key);
                if (!_chunks.ContainsKey(key))
                {
                    var c = _chunkPool.Count > 0 ? _chunkPool.Pop() : NewChunk();
                    c.Cx = cx; c.Cy = cy; c.Go.SetActive(true);
                    BuildChunk(c);
                    _chunks[key] = c;
                }
            }
            var drop = new List<long>();
            foreach (var kv in _chunks) if (!keep.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var k in drop) { var c = _chunks[k]; c.Go.SetActive(false); _chunkPool.Push(c); _chunks.Remove(k); }

            DrawNodes();
            SyncPois(heroPos);
            SyncCities(heroPos);
            SyncZones(heroPos);
            SyncArmies(heroPos);
            SyncBanners(heroPos);
        }

        Chunk NewChunk()
        {
            var c = new Chunk { Go = new GameObject("Chunk") };
            c.Go.transform.SetParent(_root, false);
            c.Mesh = new Mesh { name = "ChunkMesh" };
            c.Mesh.MarkDynamic();
            c.Go.AddComponent<MeshFilter>().sharedMesh = c.Mesh;
            var r = c.Go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Look.Lit;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            int n = _s.Balance.chunkTiles;
            c.Verts = new Vector3[n * n * 4]; c.Colors = new Color[n * n * 4]; c.Normals = new Vector3[n * n * 4]; c.Tris = new int[n * n * 6];
            return c;
        }

        /// <summary>Чанк: по квадрату на тайл, общие высоты в углах (непрерывная поверхность) и плоский цвет тайла.</summary>
        void BuildChunk(Chunk c)
        {
            var map = _s.Map; int n = _s.Balance.chunkTiles; float ts = map.TileSize;
            int v = 0, t = 0;
            for (int ly = 0; ly < n; ly++)
            for (int lx = 0; lx < n; lx++)
            {
                int tx = c.Cx * n + lx, ty = c.Cy * n + ly;
                float h00 = CornerHeight(tx, ty), h10 = CornerHeight(tx + 1, ty), h01 = CornerHeight(tx, ty + 1), h11 = CornerHeight(tx + 1, ty + 1);
                c.Verts[v] = new Vector3(tx * ts, h00, ty * ts);
                c.Verts[v + 1] = new Vector3((tx + 1) * ts, h10, ty * ts);
                c.Verts[v + 2] = new Vector3(tx * ts, h01, (ty + 1) * ts);
                c.Verts[v + 3] = new Vector3((tx + 1) * ts, h11, (ty + 1) * ts);
                var nrm = Vector3.Cross(c.Verts[v + 2] - c.Verts[v], c.Verts[v + 1] - c.Verts[v]).normalized;
                if (nrm.y < 0) nrm = -nrm;
                c.Normals[v] = c.Normals[v + 1] = c.Normals[v + 2] = c.Normals[v + 3] = nrm;
                c.Tris[t] = v; c.Tris[t + 1] = v + 2; c.Tris[t + 2] = v + 1;
                c.Tris[t + 3] = v + 1; c.Tris[t + 4] = v + 2; c.Tris[t + 5] = v + 3;
                v += 4; t += 6;
            }
            FillColors(c);
            c.Mesh.Clear();
            c.Mesh.vertices = c.Verts;
            c.Mesh.normals = c.Normals;
            c.Mesh.colors = c.Colors;
            c.Mesh.triangles = c.Tris;
            c.Mesh.RecalculateBounds();
            RebuildNodes(c);
        }

        void Recolor(Chunk c) { FillColors(c); c.Mesh.colors = c.Colors; }

        void FillColors(Chunk c)
        {
            var map = _s.Map; int n = _s.Balance.chunkTiles;
            int v = 0;
            var tint = Look.Hex(_s.PlayerEraDef.tint, Color.white);
            for (int ly = 0; ly < n; ly++)
            for (int lx = 0; lx < n; lx++)
            {
                int tx = c.Cx * n + lx, ty = c.Cy * n + ly;
                int idx = ty * map.Size + tx;
                var td = map.TerrainAt(idx);
                var col = Look.Hex(td.color);
                float j = (MathUtil.Hash01(tx, ty, 77) - 0.5f) * 0.08f;
                col = new Color(col.r + j, col.g + j, col.b + j) * tint;
                if (!Vision.IsExplored(_s.State, idx)) col = new Color(0.05f, 0.06f, 0.08f);
                c.Colors[v] = c.Colors[v + 1] = c.Colors[v + 2] = c.Colors[v + 3] = col;
                v += 4;
            }
        }

        // ---------- Ресурсные узлы (инстансинг) ----------
        void RebuildNodes(Chunk c)
        {
            foreach (var l in c.Nodes.Values) l.Clear();
            foreach (var l in c.NodeColors.Values) l.Clear();
            var map = _s.Map; int n = _s.Balance.chunkTiles;
            for (int ly = 0; ly < n; ly++)
            for (int lx = 0; lx < n; lx++)
            {
                int tx = c.Cx * n + lx, ty = c.Cy * n + ly;
                int idx = ty * map.Size + tx;
                var node = map.NodeAt(idx);
                if (node == null || !Vision.IsExplored(_s.State, idx)) continue;
                var center = map.TileCenter(tx, ty);
                float jx = (MathUtil.Hash01(tx, ty, 5) - 0.5f) * 0.8f, jy = (MathUtil.Hash01(tx, ty, 6) - 0.5f) * 0.8f;
                var p = new Vector3(center.x + jx, GroundHeight(center), center.y + jy);
                float s = 0.8f + MathUtil.Hash01(tx, ty, 9) * 0.5f;
                var col = Look.Hex(node.color);
                switch (node.shape)
                {
                    case "tree":
                        AddNode(c, "trunk", Matrix4x4.TRS(p + Vector3.up * 0.9f * s, Quaternion.identity, new Vector3(0.35f, 0.9f, 0.35f) * s), new Color(0.42f, 0.3f, 0.2f));
                        AddNode(c, "crown", Matrix4x4.TRS(p + Vector3.up * 2.3f * s, Quaternion.identity, new Vector3(1.7f, 1.9f, 1.7f) * s), col * (0.9f + jx * 0.2f));
                        break;
                    case "rock":
                        AddNode(c, "rock", Matrix4x4.TRS(p + Vector3.up * 0.35f * s, Quaternion.Euler(0, jx * 90, 15 * jy), new Vector3(1.1f, 0.7f, 0.9f) * s), col);
                        break;
                    default:
                        AddNode(c, "bush", Matrix4x4.TRS(p + Vector3.up * 0.35f, Quaternion.identity, new Vector3(0.9f, 0.6f, 0.9f) * s), col);
                        break;
                }
            }
        }

        static void AddNode(Chunk c, string type, Matrix4x4 m, Color col)
        {
            if (!c.Nodes.TryGetValue(type, out var l)) { l = new List<Matrix4x4>(); c.Nodes[type] = l; c.NodeColors[type] = new List<Vector4>(); }
            l.Add(m); c.NodeColors[type].Add(col);
        }

        public void OnGathered(int tile)
        {
            var map = _s.Map; int n = _s.Balance.chunkTiles;
            int cx = (tile % map.Size) / n, cy = (tile / map.Size) / n;
            if (_chunks.TryGetValue(((long)cx << 32) | (uint)cy, out var c)) RebuildNodes(c);
        }

        readonly Matrix4x4[] _batch = new Matrix4x4[1023];
        readonly Vector4[] _batchColors = new Vector4[1023];
        readonly MaterialPropertyBlock _nodeMpb = new MaterialPropertyBlock();

        void DrawNodes()
        {
            foreach (var type in new[] { "trunk", "crown", "rock", "bush" })
            {
                var mesh = Look.Mesh(type == "trunk" ? PrimitiveType.Cylinder : type == "rock" ? PrimitiveType.Cube : PrimitiveType.Sphere);
                int count = 0;
                foreach (var c in _chunks.Values)
                {
                    if (!c.Nodes.TryGetValue(type, out var list)) continue;
                    var cols = c.NodeColors[type];
                    for (int i = 0; i < list.Count; i++)
                    {
                        _batch[count] = list[i]; _batchColors[count] = cols[i]; count++;
                        if (count == 1023) { Flush(mesh, count); count = 0; }
                    }
                }
                if (count > 0) Flush(mesh, count);
            }
        }

        void Flush(Mesh mesh, int count)
        {
            _nodeMpb.Clear();
            _nodeMpb.SetVectorArray(Look.ColorId, _batchColors);
            Graphics.DrawMeshInstanced(mesh, 0, Look.LitInstanced, _batch, count, _nodeMpb, UnityEngine.Rendering.ShadowCastingMode.Off, false);
        }

        // ---------- Точки интереса ----------
        void SyncPois(Vec2 hero)
        {
            foreach (var p in _s.State.Pois)
            {
                var def = _s.Content.Poi[p.DefId];
                if (def.kind == "zone") continue;
                bool show = p.Discovered && Vec2.Distance(p.Pos, hero) < 110f;
                _pois.TryGetValue(p.Id, out var go);
                if (show && go == null) { go = BuildPoi(p, def); _pois[p.Id] = go; }
                if (go != null)
                {
                    go.SetActive(show);
                    if (show && p.Cleared && def.kind == "camp") go.transform.localScale = new Vector3(1, 0.5f, 1); // сожжённый лагерь
                }
            }
        }

        GameObject BuildPoi(PoiState p, PoiDef def)
        {
            var go = new GameObject("POI " + p.Name);
            go.transform.SetParent(_root, false);
            go.transform.position = Ground(p.Pos);
            var col = Look.Hex(def.color);
            var t = go.transform;
            switch (def.kind)
            {
                case "ruins":
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 1.25f;
                        float h = 0.8f + (i % 3) * 0.9f;
                        Look.Prim(PrimitiveType.Cube, t, col, new Vector3(Mathf.Cos(a) * 2.2f, h * 0.5f, Mathf.Sin(a) * 2.2f), new Vector3(0.7f, h, 0.7f));
                    }
                    Look.Prim(PrimitiveType.Cube, t, col * 0.8f, new Vector3(0, 0.1f, 0), new Vector3(3.5f, 0.2f, 3.5f));
                    break;
                case "cache":
                    Look.Prim(PrimitiveType.Cube, t, col, new Vector3(0, 0.35f, 0), new Vector3(0.9f, 0.6f, 0.6f));
                    break;
                case "lair":
                    Look.Prim(PrimitiveType.Sphere, t, col, new Vector3(0, 0.2f, 0), new Vector3(4.5f, 1.6f, 4f));
                    Look.Prim(PrimitiveType.Sphere, t, Color.black, new Vector3(0, 0.4f, -1.8f), new Vector3(1.4f, 1f, 0.6f));
                    break;
                case "camp":
                case "outpost":
                    if (def.kind == "outpost")
                    {
                        Look.Prim(PrimitiveType.Cube, t, col, new Vector3(0, 2.5f, 0), new Vector3(1.4f, 5f, 1.4f));
                        Look.Prim(PrimitiveType.Cube, t, col * 1.2f, new Vector3(0, 5.2f, 0), new Vector3(2.6f, 0.4f, 2.6f));
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * 2.1f + 0.4f;
                        Look.Prim(PrimitiveType.Cube, t, col * 0.9f, new Vector3(Mathf.Cos(a) * 3f, 0.8f, Mathf.Sin(a) * 3f), new Vector3(1.8f, 1.6f, 1.8f)).transform.localRotation = Quaternion.Euler(0, 45, 0);
                    }
                    Look.Prim(PrimitiveType.Cylinder, t, new Color(1f, 0.5f, 0.2f), new Vector3(0, 0.15f, 0), new Vector3(0.9f, 0.15f, 0.9f));
                    Look.Prim(PrimitiveType.Cube, t, new Color(0.7f, 0.1f, 0.1f), new Vector3(2.2f, 2.6f, 0), new Vector3(0.1f, 0.8f, 1.1f));
                    break;
                case "horses":
                    for (int i = 0; i < 3; i++)
                        Look.Prim(PrimitiveType.Capsule, t, new Color(0.55f, 0.35f, 0.2f), new Vector3(i * 1.6f - 1.6f, 0.7f, (i % 2) * 1.2f), new Vector3(0.6f, 0.7f, 0.6f)).transform.localRotation = Quaternion.Euler(90, 20 * i, 0);
                    break;
                case "oddity":
                    for (int i = 0; i < 9; i++)
                        Look.Prim(PrimitiveType.Cube, t, col, new Vector3((i % 3 - 1) * 1.6f, 0.25f, (i / 3 - 1) * 1.6f), new Vector3(0.5f, 0.5f, 0.5f));
                    break;
                case "strange":
                    Look.Prim(PrimitiveType.Cube, t, col, new Vector3(0, 1.6f, 0), new Vector3(0.6f, 3.2f, 0.6f));
                    Look.Prim(PrimitiveType.Capsule, t, new Color(0.7f, 0.6f, 0.85f), new Vector3(1.2f, 0.9f, 0), new Vector3(0.6f, 0.9f, 0.6f));
                    break;
                case "grave":
                    Look.Prim(PrimitiveType.Cube, t, col, new Vector3(0, 0.5f, 0), new Vector3(0.5f, 1f, 0.2f));
                    Look.Prim(PrimitiveType.Cube, t, col * 0.8f, new Vector3(0, 0.05f, 0.6f), new Vector3(0.8f, 0.1f, 1.4f));
                    break;
            }
            return go;
        }

        // ---------- Города ----------
        void SyncCities(Vec2 hero)
        {
            foreach (var c in _s.State.Cities)
            {
                bool show = Vec2.Distance(c.Pos, hero) < 140f && Vision.IsExplored(_s.State, _s.Map.TileAt(c.Pos));
                string sig = $"{c.StageIndex}|{c.Buildings.Count}|{c.NationId}|{c.Hungry}|{BuildingSig(c)}";
                _cities.TryGetValue(c.Id, out var go);
                if (show && (go == null || _citySig[c.Id] != sig))
                {
                    if (go != null) Object.Destroy(go);
                    go = BuildCity(c);
                    _cities[c.Id] = go; _citySig[c.Id] = sig;
                }
                if (go != null) go.SetActive(show);
            }
        }

        static string BuildingSig(CityState c)
        {
            int h = 17; foreach (var b in c.Buildings) h = h * 31 + b.DefId.GetHashCode(); return h.ToString();
        }

        GameObject BuildCity(CityState c)
        {
            var go = new GameObject("City " + c.Name);
            go.transform.SetParent(_root, false);
            var nation = _s.Nation(c.NationId);
            var ncol = nation != null ? Look.Hex(nation.Color) : new Color(0.7f, 0.7f, 0.7f);
            var stage = CitySystem.Stage(_s, c);
            foreach (var b in c.Buildings)
            {
                var d = _s.Content.Buildings[b.DefId];
                var col = Look.Hex(d.color);
                if (c.Hungry && d.housing > 0) col *= 0.6f; // голод виден: дома тускнеют
                var p = Ground(b.Pos);
                var bt = new GameObject(d.name).transform;
                bt.SetParent(go.transform, false); bt.position = p;
                bt.localRotation = Quaternion.Euler(0, (b.Slot * 37) % 90, 0);
                switch (b.DefId)
                {
                    case "fire_pit":
                        var fire = Look.Prim(PrimitiveType.Cylinder, bt, new Color(1f, 0.55f, 0.15f), new Vector3(0, 0.25f, 0), new Vector3(1.2f, 0.25f, 1.2f));
                        Look.SetColor(fire.GetComponent<Renderer>(), new Color(1f, 0.55f, 0.15f), 0.8f);
                        break;
                    case "field":
                        Look.Prim(PrimitiveType.Cube, bt, col, new Vector3(0, 0.08f, 0), new Vector3(d.size, 0.16f, d.size));
                        for (int i = 0; i < 4; i++) Look.Prim(PrimitiveType.Cube, bt, col * 0.8f, new Vector3(-d.size * 0.375f + i * d.size * 0.25f, 0.2f, 0), new Vector3(0.15f, 0.12f, d.size * 0.9f));
                        break;
                    case "eternal_spire":
                    case "mirror_of_sky":
                        Look.Prim(PrimitiveType.Cylinder, bt, col * 0.8f, new Vector3(0, 0.5f, 0), new Vector3(d.size, 0.5f, d.size));
                        var top = Look.Prim(PrimitiveType.Cylinder, bt, col, new Vector3(0, d.height * 0.5f, 0), new Vector3(d.size * 0.35f, d.height * 0.5f, d.size * 0.35f));
                        Look.SetColor(top.GetComponent<Renderer>(), col, 0.6f);
                        break;
                    default:
                        Look.Prim(PrimitiveType.Cube, bt, col, new Vector3(0, d.height * 0.4f, 0), new Vector3(d.size, d.height * 0.8f, d.size));
                        var roof = Look.Prim(PrimitiveType.Cube, bt, Color.Lerp(col, ncol, 0.5f) * 0.85f, new Vector3(0, d.height * 0.85f, 0), new Vector3(d.size * 0.75f, d.size * 0.75f, d.size * 0.75f));
                        roof.transform.localRotation = Quaternion.Euler(45, 0, 45);
                        roof.transform.localScale = new Vector3(d.size * 0.6f, d.size * 0.6f, d.size * 0.6f);
                        if (b.DefId == "palace" || b.DefId == "chief_house")
                            Look.Prim(PrimitiveType.Cube, bt, ncol, new Vector3(d.size * 0.3f, d.height + 0.8f, 0), new Vector3(0.12f, 1.6f, 0.12f));
                        break;
                }
            }
            // Стены у крупных городов.
            if (stage.walls || c.HasBuilding("walls") || c.HasBuilding("palisade"))
            {
                float r = stage.radius + 6f;
                bool stone = c.HasBuilding("walls") || stage.walls;
                int segs = 28;
                for (int i = 0; i < segs; i++)
                {
                    if (i == 0 || i == segs / 2) continue; // ворота
                    float a = i * Mathf.PI * 2 / segs;
                    var pos = c.Pos + new Vec2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    var w = Look.Prim(PrimitiveType.Cube, go.transform, stone ? new Color(0.62f, 0.62f, 0.6f) : new Color(0.45f, 0.32f, 0.2f), Ground(pos, 1f), new Vector3(2 * Mathf.PI * r / segs, 2f, 0.6f));
                    w.transform.rotation = Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90, 0);
                }
            }
            return go;
        }

        public void InvalidateCities() { foreach (var g in _cities.Values) Object.Destroy(g); _cities.Clear(); _citySig.Clear(); }

        // ---------- Дым ----------
        void SyncZones(Vec2 hero)
        {
            HeroInSmoke = _s.State.Hero.InSmoke;
            foreach (var z in _s.State.Pois)
            {
                if (_s.Content.Poi[z.DefId].kind != "zone") continue;
                bool show = Vec2.Distance(z.Pos, hero) < z.Radius + 120f && (z.Discovered || Vision.IsExplored(_s.State, _s.Map.TileAt(z.Pos)));
                _zones.TryGetValue(z.Id, out var go);
                if (show && go == null)
                {
                    go = new GameObject("Smoke " + z.Name);
                    go.transform.SetParent(_root, false);
                    var outer = Look.Prim(PrimitiveType.Cylinder, go.transform, new Color(0.42f, 0.33f, 0.55f, 0.55f), Vector3.zero, Vector3.one, true, "outer");
                    var inner = Look.Prim(PrimitiveType.Cylinder, go.transform, new Color(0.3f, 0.25f, 0.4f, 0.35f), Vector3.zero, Vector3.one, true, "inner");
                    var r = outer.GetComponent<Renderer>(); var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb); mpb.SetFloat(Look.PulseId, 1f); r.SetPropertyBlock(mpb);
                    _zones[z.Id] = go;
                }
                if (go == null) continue;
                go.SetActive(show);
                if (!show) continue;
                go.transform.position = Ground(z.Pos);
                var o = go.transform.Find("outer"); var i = go.transform.Find("inner");
                o.localScale = new Vector3(z.Radius * 2, 4f, z.Radius * 2); o.localPosition = new Vector3(0, 3.5f, 0);
                i.localScale = new Vector3(z.Radius * 1.4f, 5f, z.Radius * 1.4f); i.localPosition = new Vector3(0, 5f, 0);
                i.Rotate(0, Time.deltaTime * 6f, 0);
                // Изнутри дым прозрачнее — видно лишь на несколько шагов.
                float a = HeroInSmoke && _s.State.Hero.SmokePoiId == z.Id ? 0.18f : 0.6f;
                Look.SetColor(o.GetComponent<Renderer>(), new Color(0.42f, 0.33f, 0.55f, a));
            }
        }

        // ---------- Армии вдали ----------
        void SyncArmies(Vec2 hero)
        {
            var seen = new HashSet<string>();
            foreach (var a in _s.State.Armies)
            {
                if (a.Expanded || a.Strength <= 0) continue;
                if (Vec2.Distance(a.Pos, hero) > 120f || !Vision.IsExplored(_s.State, _s.Map.TileAt(a.Pos))) continue;
                seen.Add(a.Id);
                if (!_armies.TryGetValue(a.Id, out var go))
                {
                    go = new GameObject("Army " + a.Id);
                    go.transform.SetParent(_root, false);
                    var n = _s.Nation(a.NationId);
                    var col = a.Kind == "raiders" ? new Color(0.55f, 0.2f, 0.15f) : a.Kind == "settlers" ? new Color(0.7f, 0.6f, 0.4f) : (n != null ? Look.Hex(n.Color) : Color.gray);
                    int k = a.Kind == "settlers" ? 3 : 5;
                    for (int i = 0; i < k; i++)
                        Look.Prim(PrimitiveType.Capsule, go.transform, col, new Vector3((i % 3 - 1) * 1.1f, 0.8f, (i / 3) * 1.1f), new Vector3(0.6f, 0.8f, 0.6f));
                    if (a.Kind == "settlers") Look.Prim(PrimitiveType.Cube, go.transform, new Color(0.5f, 0.35f, 0.2f), new Vector3(0, 0.6f, -1.5f), new Vector3(1.2f, 0.8f, 1.8f));
                    else Look.Prim(PrimitiveType.Cube, go.transform, col, new Vector3(0, 2.6f, 0), new Vector3(0.9f, 0.6f, 0.05f));
                    _armies[a.Id] = go;
                }
                var target = Ground(a.Pos);
                go.transform.position = Vector3.Lerp(go.transform.position == Vector3.zero ? target : go.transform.position, target, 0.2f);
            }
            var drop = new List<string>();
            foreach (var kv in _armies) if (!seen.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var k in drop) { Object.Destroy(_armies[k]); _armies.Remove(k); }
        }

        // ---------- Знамёна на границах ----------
        void SyncBanners(Vec2 hero)
        {
            var own = _s.State.TerritoryOwner;
            if (own == null) return;
            float cellW = _s.Balance.territoryCell * _s.Map.TileSize;
            int nx = _s.State.TerritoryCellsX;
            int hc = (int)(hero.y / cellW) * nx + (int)(hero.x / cellW);
            if (hc == _bannerCell && Time.frameCount % 120 != 0) return;
            _bannerCell = hc;
            _bannerUsed = 0;
            int hx = (int)(hero.x / cellW), hy = (int)(hero.y / cellW);
            for (int y = hy - 4; y <= hy + 4; y++)
            for (int x = hx - 4; x <= hx + 4; x++)
            {
                if (x < 1 || y < 1 || x >= nx - 1 || y >= nx - 1) continue;
                int o = own[y * nx + x];
                if (o == 0) continue;
                bool border = own[y * nx + x + 1] != o || own[y * nx + x - 1] != o || own[(y + 1) * nx + x] != o || own[(y - 1) * nx + x] != o;
                if (!border) continue;
                var pos = new Vec2((x + 0.5f) * cellW, (y + 0.5f) * cellW);
                if (!_s.Map.Walkable(pos) || !Vision.IsExplored(_s.State, _s.Map.TileAt(pos))) continue;
                var nation = _s.State.Nations[o - 1];
                GameObject b;
                if (_bannerUsed < _banners.Count) b = _banners[_bannerUsed];
                else
                {
                    b = new GameObject("Banner");
                    b.transform.SetParent(_root, false);
                    Look.Prim(PrimitiveType.Cylinder, b.transform, new Color(0.35f, 0.25f, 0.15f), new Vector3(0, 1.5f, 0), new Vector3(0.12f, 1.5f, 0.12f), false, "pole");
                    Look.Prim(PrimitiveType.Cube, b.transform, Color.white, new Vector3(0.45f, 2.5f, 0), new Vector3(0.9f, 0.6f, 0.05f), false, "flag");
                    _banners.Add(b);
                }
                _bannerUsed++;
                b.SetActive(true);
                b.transform.position = Ground(pos);
                Look.SetColor(b.transform.Find("flag").GetComponent<Renderer>(), Look.Hex(nation.Color));
            }
            for (int i = _bannerUsed; i < _banners.Count; i++) _banners[i].SetActive(false);
        }
    }
}
