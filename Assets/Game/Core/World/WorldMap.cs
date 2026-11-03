using System;
using System.Collections.Generic;
using Immortal.Core.Content;

namespace Immortal.Core.World
{
    /// <summary>
    /// Детерминированная база мира из seed: местность, высоты, регионы, ресурсные узлы.
    /// Изменения (вырубка, поля, дороги) лежат слоем в GameState.WorldMods.
    /// </summary>
    public sealed class WorldMap
    {
        public readonly int Size;
        public readonly float TileSize;
        public readonly int Seed;
        readonly ContentDatabase _db;
        readonly GameState _state;
        public readonly byte[] BaseTerrain;
        public readonly float[] Height;
        public readonly byte[] Region;
        public readonly Vec2[] RegionCenters;
        /// <summary>Заблокированные постройками тайлы (пересобирается).</summary>
        public readonly bool[] BuildingBlocked;

        public float WorldSize => Size * TileSize;

        public WorldMap(ContentDatabase db, GameState state)
        {
            _db = db; _state = state; Seed = state.Seed;
            Size = db.Balance.worldTiles; TileSize = db.Balance.tileSize;
            int n = Size * Size;
            BaseTerrain = new byte[n]; Height = new float[n]; Region = new byte[n]; BuildingBlocked = new bool[n];
            RegionCenters = new Vec2[db.Regions.Count];
            Generate();
        }

        void Generate()
        {
            var rnd = new XorShiftRandom((ulong)(Seed * 7919 + 17));
            for (int i = 0; i < _db.Regions.Count; i++)
            {
                var r = _db.Regions[i];
                float jx = rnd.Range(-0.03f, 0.03f), jy = rnd.Range(-0.03f, 0.03f);
                RegionCenters[i] = new Vec2((r.cx + jx) * WorldSize, (r.cy + jy) * WorldSize);
            }

            int water = _db.TerrainIndex.TryGetValue("water", out var wi) ? wi : 0;
            int mountain = _db.TerrainIndex.TryGetValue("mountain", out var mi) ? mi : 0;
            for (int ty = 0; ty < Size; ty++)
            for (int tx = 0; tx < Size; tx++)
            {
                int idx = ty * Size + tx;
                float wx = (tx + 0.5f) * TileSize, wy = (ty + 0.5f) * TileSize;
                // Искривление границ регионов шумом.
                float warpX = (MathUtil.Fbm(tx * 0.02f, ty * 0.02f, Seed + 11) - 0.5f) * 70f;
                float warpY = (MathUtil.Fbm(tx * 0.02f, ty * 0.02f, Seed + 23) - 0.5f) * 70f;
                int best = 0; float bestD = float.MaxValue;
                for (int r = 0; r < RegionCenters.Length; r++)
                {
                    float d = Vec2.SqrDistance(new Vec2(wx + warpX, wy + warpY), RegionCenters[r]);
                    if (d < bestD) { bestD = d; best = r; }
                }
                Region[idx] = (byte)best;
                var reg = _db.Regions[best];
                int t = _db.TerrainIndex[reg.primary];
                float s = MathUtil.Fbm(tx * 0.06f, ty * 0.06f, Seed + 101);
                float w = MathUtil.Fbm(tx * 0.035f, ty * 0.035f, Seed + 202);
                float m = MathUtil.Fbm(tx * 0.045f, ty * 0.045f, Seed + 303);
                if (reg.secondary != null && s > 0.62f - reg.secondaryChance * 0.35f) t = _db.TerrainIndex[reg.secondary];
                if (w < 0.24f + reg.waterChance) t = water;
                else if (m > 0.78f - reg.mountainChance) t = mountain;
                int edge = Math.Min(Math.Min(tx, ty), Math.Min(Size - 1 - tx, Size - 1 - ty));
                if (edge < 3) t = edge < 2 ? water : mountain;
                BaseTerrain[idx] = (byte)t;
                var td = _db.TerrainByIndex[t];
                Height[idx] = td.height + (MathUtil.Fbm(tx * 0.05f, ty * 0.05f, Seed + 404) - 0.5f) * 1.2f;
            }
        }

        // ---------- Координаты ----------
        public bool InBounds(int tx, int ty) => tx >= 0 && ty >= 0 && tx < Size && ty < Size;
        public int TileIndex(int tx, int ty) => ty * Size + tx;
        public int TileAt(Vec2 p) => TileIndex(MathUtil.Clamp((int)(p.x / TileSize), 0, Size - 1), MathUtil.Clamp((int)(p.y / TileSize), 0, Size - 1));
        public void TileXY(Vec2 p, out int tx, out int ty) { tx = (int)Math.Floor(p.x / TileSize); ty = (int)Math.Floor(p.y / TileSize); }
        public Vec2 TileCenter(int tx, int ty) => new Vec2((tx + 0.5f) * TileSize, (ty + 0.5f) * TileSize);
        public Vec2 TileCenter(int idx) => TileCenter(idx % Size, idx / Size);
        public Vec2 Clamp(Vec2 p) => new Vec2(MathUtil.Clamp(p.x, TileSize * 2, WorldSize - TileSize * 2), MathUtil.Clamp(p.y, TileSize * 2, WorldSize - TileSize * 2));

        // ---------- Местность ----------
        public int TerrainIndexAt(int idx)
        {
            if (_state.WorldMods.Terrain.TryGetValue(idx, out var id) && _db.TerrainIndex.TryGetValue(id, out var ti)) return ti;
            return BaseTerrain[idx];
        }
        public TerrainDef TerrainAt(int idx) => _db.TerrainByIndex[TerrainIndexAt(idx)];
        public TerrainDef TerrainAt(Vec2 p) => TerrainAt(TileAt(p));
        public RegionDef RegionAt(Vec2 p) => _db.Regions[Region[TileAt(p)]];
        public int RegionIndexAt(Vec2 p) => Region[TileAt(p)];

        public bool Walkable(int tx, int ty)
        {
            if (!InBounds(tx, ty)) return false;
            int i = TileIndex(tx, ty);
            if (BuildingBlocked[i]) return false;
            if (!TerrainAt(i).walkable) return false;
            var node = NodeAt(i);
            return node == null || !node.blocks;
        }
        public bool Walkable(Vec2 p) { TileXY(p, out int tx, out int ty); return Walkable(tx, ty); }
        public float MoveCost(int idx) => TerrainAt(idx).moveCost;

        // ---------- Ресурсные узлы ----------
        /// <summary>Узел на тайле: детерминированный из seed с учётом истощения и смены местности.</summary>
        public NodeDef NodeAt(int idx)
        {
            var t = TerrainAt(idx);
            if (t.nodes.Count == 0) return null;
            if (_state.WorldMods.Depleted.TryGetValue(idx, out var until) && (until < 0 || until > _state.Months)) return null;
            int tx = idx % Size, ty = idx / Size;
            float h = MathUtil.Hash01(tx, ty, Seed + 999);
            float acc = 0;
            foreach (var ns in t.nodes)
            {
                acc += ns.chance;
                if (h < acc) return _db.Nodes[ns.node];
            }
            return null;
        }

        public void SetTerrain(int idx, string terrainId)
        {
            _state.WorldMods.Terrain[idx] = terrainId;
            _state.WorldMods.Revision++;
        }

        /// <summary>Ближайший проходимый тайл к точке (поиск по кольцам).</summary>
        public Vec2 NearestWalkable(Vec2 p, int maxRing = 12)
        {
            TileXY(p, out int cx, out int cy);
            if (Walkable(cx, cy)) return p;
            for (int r = 1; r <= maxRing; r++)
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Math.Abs(dx) != r && Math.Abs(dy) != r) continue;
                    if (Walkable(cx + dx, cy + dy)) return TileCenter(cx + dx, cy + dy);
                }
            return p;
        }

        public void RebuildBuildingBlockers(ContentDatabase db, GameState s)
        {
            Array.Clear(BuildingBlocked, 0, BuildingBlocked.Length);
            foreach (var c in s.Cities)
                foreach (var b in c.Buildings)
                {
                    if (!db.Buildings.TryGetValue(b.DefId, out var bd)) continue;
                    if (bd.id == "field" || bd.id == "fire_pit") continue; // по полю и вокруг костра можно ходить
                    float half = bd.size * 0.5f - 0.3f;
                    for (float y = b.Pos.y - half; y <= b.Pos.y + half; y += TileSize * 0.5f)
                    for (float x = b.Pos.x - half; x <= b.Pos.x + half; x += TileSize * 0.5f)
                    {
                        TileXY(new Vec2(x, y), out int tx, out int ty);
                        if (InBounds(tx, ty)) BuildingBlocked[TileIndex(tx, ty)] = true;
                    }
                }
        }

        /// <summary>Свободная прямая между двумя точками (для сглаживания путей и прямого хода).</summary>
        public bool LineWalkable(Vec2 a, Vec2 b)
        {
            float d = Vec2.Distance(a, b);
            int steps = Math.Max(1, (int)(d / (TileSize * 0.4f)));
            for (int i = 1; i <= steps; i++)
                if (!Walkable(Vec2.Lerp(a, b, i / (float)steps))) return false;
            return true;
        }
    }
}
