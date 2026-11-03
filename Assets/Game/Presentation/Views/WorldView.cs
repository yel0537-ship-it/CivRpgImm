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
        sealed class Patrol { public GameObject Go; public ProceduralAnimator Anim; public int From, To; public float T; public Vector3 Last; }
        readonly List<Patrol> _patrols = new List<Patrol>();
        readonly List<Vector3> _bannerPos = new List<Vector3>();
        int _bannerCell = -1, _bannerUsed;
        int _revision = -1, _fogRevision = -1;
        float _rebuildCooldown;
        public bool HeroInSmoke;

        sealed class Chunk
        {
            public int Cx, Cy;
            public GameObject Go;
            public Mesh Mesh;
            public Vector3[] Verts; public Color[] Colors; public Vector3[] Normals; public int[] Tris; public Vector2[] Layers;
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
            foreach (var p in _patrols) Object.Destroy(p.Go);
            _patrols.Clear();
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
            SyncPatrols(dt);
        }

        /// <summary>Дозоры: пара стражей ходит между ближайшими знамёнами границы (только визуально).</summary>
        void SyncPatrols(float dt)
        {
            int want = Mathf.Min(3, _bannerPos.Count / 3);
            while (_patrols.Count > want) { Object.Destroy(_patrols[_patrols.Count - 1].Go); _patrols.RemoveAt(_patrols.Count - 1); }
            while (_patrols.Count < want)
            {
                int from = _patrols.Count * 3 % Mathf.Max(1, _bannerPos.Count);
                var go = new GameObject("patrol");
                go.transform.SetParent(_root, false);
                Color col = new Color(0.55f, 0.55f, 0.6f);
                var own = TerritorySystem.OwnerAt(_s, Look.ToCore(_bannerPos[from]));
                if (own != null) col = Look.Hex(own.Color);
                var ve = VisualCatalog.Resolve("unit", "patrol", "soldier");
                ProceduralAnimator anim;
                if (ve != null) { Object.Instantiate(ve.Prefab, go.transform); anim = go.AddComponent<ProceduralAnimator>(); anim.Init(go.transform.GetChild(0), false); }
                else
                {
                    var body = ViewFactory.BuildHumanoid(go.transform, col, 1f, new Unit { Kind = UnitKind.Soldier }, out var hand);
                    Look.Prim(PrimitiveType.Cylinder, hand, new Color(0.45f, 0.35f, 0.25f), new Vector3(0, 0.4f, 0), new Vector3(0.05f, 0.9f, 0.05f));
                    anim = go.AddComponent<ProceduralAnimator>(); anim.Init(body, false);
                }
                go.transform.position = _bannerPos[from];
                _patrols.Add(new Patrol { Go = go, Anim = anim, From = from, To = (from + 1) % _bannerPos.Count, Last = _bannerPos[from] });
            }
            foreach (var p in _patrols)
            {
                if (_bannerPos.Count < 2) break;
                p.From %= _bannerPos.Count; p.To %= _bannerPos.Count;
                var a = _bannerPos[p.From]; var b = _bannerPos[p.To];
                float len = Mathf.Max(1f, Vector3.Distance(a, b));
                p.T += dt * 1.6f / len;
                if (p.T >= 1f) { p.T = 0; p.From = p.To; p.To = (p.To + 1) % _bannerPos.Count; continue; }
                var pos = Vector3.Lerp(a, b, p.T);
                pos.y = GroundHeight(Look.ToCore(pos));
                var dir = b - a; dir.y = 0;
                p.Go.transform.position = pos;
                if (dir.sqrMagnitude > 0.01f) p.Go.transform.rotation = Quaternion.LookRotation(dir);
                float speed = dt > 0 ? Vector3.Distance(pos, p.Last) / dt : 0; p.Last = pos;
                p.Anim.Tick(new UnitAnimState { Speed = speed }, dt);
            }
        }

        Chunk NewChunk()
        {
            var c = new Chunk { Go = new GameObject("Chunk") };
            c.Go.transform.SetParent(_root, false);
            c.Mesh = new Mesh { name = "ChunkMesh" };
            c.Mesh.MarkDynamic();
            c.Go.AddComponent<MeshFilter>().sharedMesh = c.Mesh;
            var r = c.Go.AddComponent<MeshRenderer>();
            r.sharedMaterial = Look.Terrain != null ? Look.Terrain : Look.Lit;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            int n = _s.Balance.chunkTiles;
            c.Layers = new Vector2[n * n * 4];
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
                var layer = new Vector2(map.TerrainIndexAt(ty * map.Size + tx), 0);
                c.Layers[v] = c.Layers[v + 1] = c.Layers[v + 2] = c.Layers[v + 3] = layer;
                c.Tris[t] = v; c.Tris[t + 1] = v + 2; c.Tris[t + 2] = v + 1;
                c.Tris[t + 3] = v + 1; c.Tris[t + 4] = v + 2; c.Tris[t + 5] = v + 3;
                v += 4; t += 6;
            }
            FillColors(c);
            c.Mesh.Clear();
            c.Mesh.vertices = c.Verts;
            c.Mesh.normals = c.Normals;
            c.Mesh.colors = c.Colors;
            c.Mesh.uv2 = c.Layers;
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
                // С текстурами цвет даёт сама текстура слоя, вершины несут лишь разброс, оттенок эпохи и туман.
                var col = Look.Terrain != null ? Color.white : Look.Hex(td.color);
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
                float sc = 0.8f + MathUtil.Hash01(tx, ty, 9) * 0.5f;
                var col = Look.Hex(node.color) * (0.9f + jx * 0.2f);
                if (!c.Nodes.TryGetValue(node.id, out var list)) { list = new List<Matrix4x4>(); c.Nodes[node.id] = list; c.NodeColors[node.id] = new List<Vector4>(); }
                list.Add(Matrix4x4.TRS(p, Quaternion.Euler(0, MathUtil.Hash01(tx, ty, 11) * 360f, 0), Vector3.one * sc));
                c.NodeColors[node.id].Add(col);
            }
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

        /// <summary>Узлы рисуются инстансингом: часть модели × матрица экземпляра. Модель берётся из ViewFactory.Node.</summary>
        void DrawNodes()
        {
            foreach (var nd in _s.Content.TerrainFile.nodes)
            {
                var visual = ViewFactory.Node(nd);
                foreach (var part in visual.Parts)
                {
                    int count = 0;
                    foreach (var c in _chunks.Values)
                    {
                        if (!c.Nodes.TryGetValue(nd.id, out var list)) continue;
                        var cols = c.NodeColors[nd.id];
                        for (int i = 0; i < list.Count; i++)
                        {
                            _batch[count] = list[i] * part.Local;
                            _batchColors[count] = part.FixedColor ?? (Color)cols[i];
                            count++;
                            if (count == 1023) { Flush(part, count); count = 0; }
                        }
                    }
                    if (count > 0) Flush(part, count);
                }
            }
        }

        void Flush(NodePart part, int count)
        {
            if (part.UsesInstanceColor)
            {
                _nodeMpb.Clear();
                _nodeMpb.SetVectorArray(Look.ColorId, _batchColors);
                Graphics.DrawMeshInstanced(part.Mesh, 0, part.Material, _batch, count, _nodeMpb, UnityEngine.Rendering.ShadowCastingMode.Off, false);
            }
            else Graphics.DrawMeshInstanced(part.Mesh, 0, part.Material, _batch, count, null, UnityEngine.Rendering.ShadowCastingMode.On, true);
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

        GameObject BuildPoi(PoiState p, PoiDef def) => ViewFactory.CreatePoi(def, _root, Ground(p.Pos));

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
                ViewFactory.CreateBuilding(_s, d, ncol, c.Hungry, go.transform, Ground(b.Pos), (b.Slot * 37) % 90);
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
                    ViewFactory.CreateWallSegment(go.transform, stone, Ground(pos), Quaternion.Euler(0, -a * Mathf.Rad2Deg + 90, 0), 2 * Mathf.PI * r / segs);
                }
            }
            // Голод виден в мире: у голодного города бродят беженцы-тени.
            if (c.Hungry && c.NationId != null)
                for (int i = 0; i < 3; i++)
                    Look.Prim(PrimitiveType.Capsule, go.transform, new Color(0.35f, 0.33f, 0.3f), Ground(c.Pos + new Vec2(stage.radius + 3 + i, -2 - i), 0.6f), new Vector3(0.45f, 0.6f, 0.45f));
            return go;
        }

        public void InvalidateCities() { foreach (var g in _cities.Values) Object.Destroy(g); _cities.Clear(); _citySig.Clear(); }

        // ---------- Дым ----------
        readonly HashSet<string> _zoneFallback = new HashSet<string>();

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
                    go = ViewFactory.CreateZone(_root, out bool fb);
                    go.name = "Smoke " + z.Name;
                    if (fb) _zoneFallback.Add(z.Id);
                    _zones[z.Id] = go;
                }
                if (go == null) continue;
                go.SetActive(show);
                if (!show) continue;
                go.transform.position = Ground(z.Pos);
                bool inside = HeroInSmoke && _s.State.Hero.SmokePoiId == z.Id;
                if (!_zoneFallback.Contains(z.Id))
                {
                    var m = go.transform.Find("model");
                    if (m != null) m.localScale = Vector3.one * z.Radius;
                    continue;
                }
                var o = go.transform.Find("outer"); var i = go.transform.Find("inner");
                o.localScale = new Vector3(z.Radius * 2, 4f, z.Radius * 2); o.localPosition = new Vector3(0, 3.5f, 0);
                i.localScale = new Vector3(z.Radius * 1.4f, 5f, z.Radius * 1.4f); i.localPosition = new Vector3(0, 5f, 0);
                i.Rotate(0, Time.deltaTime * 6f, 0);
                // Клубы дыма дрейфуют по кругу.
                for (int k = 0; k < 10; k++)
                {
                    var puff = go.transform.Find("puff" + k);
                    float ang = Time.time * 0.15f + k * 0.628f;
                    float rr = z.Radius * (0.55f + 0.4f * Mathf.Sin(k * 1.7f));
                    puff.localPosition = new Vector3(Mathf.Cos(ang) * rr, 2f + Mathf.Sin(Time.time * 0.5f + k) * 1.2f, Mathf.Sin(ang) * rr);
                    puff.localScale = Vector3.one * (3f + (k % 3));
                    Look.SetColor(puff.GetComponent<Renderer>(), new Color(0.5f, 0.42f, 0.62f, inside ? 0.12f : 0.3f));
                }
                var g = go.transform.Find("glitch");
                if (g != null)
                {
                    bool near = Vec2.Distance(z.Pos, hero) < z.Radius + 6f;
                    g.gameObject.SetActive(near);
                    if (near)
                    {
                        float step = Mathf.Floor(Time.time / 2.3f);
                        g.localPosition = new Vector3((step % 3 - 1) * 1.5f, 2.6f + Mathf.Sin(Time.time) * 0.2f, 0);
                        g.localRotation = Quaternion.Euler(0, step * 30f, 0);
                        for (int gi = 0; gi < g.childCount; gi++) g.GetChild(gi).localRotation = Quaternion.Euler(Time.time * 40f, Time.time * 40f, 0);
                    }
                }
                // Изнутри дым прозрачнее — видно лишь на несколько шагов.
                Look.SetColor(o.GetComponent<Renderer>(), new Color(0.42f, 0.33f, 0.55f, inside ? 0.18f : 0.6f));
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
            _bannerPos.Clear();
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
                _bannerPos.Add(b.transform.position + new Vector3(1.2f, 0, 1.2f));
                Look.SetColor(b.transform.Find("flag").GetComponent<Renderer>(), Look.Hex(nation.Color));
            }
            for (int i = _bannerUsed; i < _banners.Count; i++) _banners[i].SetActive(false);
        }
    }
}
