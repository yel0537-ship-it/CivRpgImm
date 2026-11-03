using System;
using System.Collections.Generic;

namespace Immortal.Core.World
{
    /// <summary>A* по квадратной сетке тайлов (8 направлений) для отдельных юнитов.</summary>
    public sealed class Pathfinder
    {
        readonly WorldMap _map;
        readonly float[] _g;
        readonly int[] _parent;
        readonly int[] _stamp;
        int _curStamp;
        readonly MinHeap _open = new MinHeap(4096);
        static readonly int[] DX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        static readonly int[] DY = { 0, 0, 1, -1, 1, -1, 1, -1 };

        public Pathfinder(WorldMap map)
        {
            _map = map;
            int n = map.Size * map.Size;
            _g = new float[n]; _parent = new int[n]; _stamp = new int[n];
        }

        public List<Vec2> FindPath(Vec2 from, Vec2 to, int maxExpand = 8000)
        {
            _map.TileXY(from, out int sx, out int sy);
            to = _map.NearestWalkable(to, 6);
            _map.TileXY(to, out int ex, out int ey);
            if (!_map.InBounds(sx, sy) || !_map.InBounds(ex, ey)) return null;
            if (sx == ex && sy == ey) return new List<Vec2> { to };
            if (_map.LineWalkable(from, to)) return new List<Vec2> { to };

            _curStamp++;
            if (_curStamp == int.MaxValue) { Array.Clear(_stamp, 0, _stamp.Length); _curStamp = 1; }
            _open.Clear();
            int size = _map.Size;
            int start = sy * size + sx, goal = ey * size + ex;
            _stamp[start] = _curStamp; _g[start] = 0; _parent[start] = -1;
            _open.Push(start, H(sx, sy, ex, ey));
            int expanded = 0, bestNode = start; float bestH = float.MaxValue;

            while (_open.Count > 0 && expanded < maxExpand)
            {
                int cur = _open.Pop();
                if (cur == goal) { bestNode = goal; break; }
                expanded++;
                int cx = cur % size, cy = cur / size;
                float h = H(cx, cy, ex, ey);
                if (h < bestH) { bestH = h; bestNode = cur; }
                for (int k = 0; k < 8; k++)
                {
                    int nx = cx + DX[k], ny = cy + DY[k];
                    if (!_map.Walkable(nx, ny)) continue;
                    if (k >= 4 && (!_map.Walkable(cx + DX[k], cy) || !_map.Walkable(cx, cy + DY[k]))) continue; // не срезаем углы
                    int ni = ny * size + nx;
                    float step = (k >= 4 ? 1.4142f : 1f) * _map.MoveCost(ni);
                    float ng = _g[cur] + step;
                    if (_stamp[ni] == _curStamp && _g[ni] <= ng) continue;
                    _stamp[ni] = _curStamp; _g[ni] = ng; _parent[ni] = cur;
                    _open.Push(ni, ng + H(nx, ny, ex, ey));
                }
            }

            var tiles = new List<int>();
            for (int c = bestNode; c != -1; c = _parent[c]) tiles.Add(c);
            tiles.Reverse();
            var pts = new List<Vec2>();
            foreach (var t in tiles) pts.Add(_map.TileCenter(t));
            if (bestNode == goal) pts[pts.Count - 1] = to;
            return Smooth(from, pts);
        }

        static float H(int x, int y, int ex, int ey)
        {
            int dx = Math.Abs(x - ex), dy = Math.Abs(y - ey);
            // Октильная эвристика с учётом самой дешёвой местности (дороги ≈ 0.7).
            return 0.7f * (Math.Max(dx, dy) + 0.4142f * Math.Min(dx, dy));
        }

        List<Vec2> Smooth(Vec2 from, List<Vec2> pts)
        {
            if (pts.Count <= 2) return pts;
            var result = new List<Vec2>();
            Vec2 anchor = from; int i = 0;
            while (i < pts.Count)
            {
                int far = i;
                for (int j = pts.Count - 1; j > i; j--)
                    if (j - i < 24 && _map.LineWalkable(anchor, pts[j])) { far = j; break; }
                result.Add(pts[far]);
                anchor = pts[far];
                i = far + 1;
            }
            return result;
        }

        sealed class MinHeap
        {
            int[] _items; float[] _keys; public int Count;
            public MinHeap(int cap) { _items = new int[cap]; _keys = new float[cap]; }
            public void Clear() => Count = 0;
            public void Push(int item, float key)
            {
                if (Count == _items.Length) { Array.Resize(ref _items, Count * 2); Array.Resize(ref _keys, Count * 2); }
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) / 2;
                    if (_keys[p] <= key) break;
                    _items[i] = _items[p]; _keys[i] = _keys[p]; i = p;
                }
                _items[i] = item; _keys[i] = key;
            }
            public int Pop()
            {
                int top = _items[0];
                Count--;
                if (Count > 0)
                {
                    int item = _items[Count]; float key = _keys[Count];
                    int i = 0;
                    while (true)
                    {
                        int l = i * 2 + 1; if (l >= Count) break;
                        int r = l + 1; int c = (r < Count && _keys[r] < _keys[l]) ? r : l;
                        if (_keys[c] >= key) break;
                        _items[i] = _items[c]; _keys[i] = _keys[c]; i = c;
                    }
                    _items[i] = item; _keys[i] = key;
                }
                return top;
            }
        }
    }

    /// <summary>
    /// Общая карта направлений к герою для толпы: BFS в окне вокруг цели.
    /// Враги смотрят на соседний тайл с меньшей дистанцией вместо собственного A*.
    /// </summary>
    public sealed class FlowField
    {
        public const int Window = 72;
        readonly WorldMap _map;
        readonly ushort[] _dist = new ushort[Window * Window];
        readonly Queue<int> _q = new Queue<int>();
        int _ox, _oy, _targetTile = -1;
        public int Version;

        public FlowField(WorldMap map) { _map = map; }

        public void Update(Vec2 target, bool force = false)
        {
            int tt = _map.TileAt(target);
            if (tt == _targetTile && !force) return;
            _targetTile = tt;
            Version++;
            _map.TileXY(target, out int tx, out int ty);
            _ox = tx - Window / 2; _oy = ty - Window / 2;
            for (int i = 0; i < _dist.Length; i++) _dist[i] = ushort.MaxValue;
            _q.Clear();
            int c = (ty - _oy) * Window + (tx - _ox);
            _dist[c] = 0; _q.Enqueue(c);
            while (_q.Count > 0)
            {
                int cur = _q.Dequeue();
                int lx = cur % Window, ly = cur / Window;
                ushort nd = (ushort)(_dist[cur] + 1);
                for (int k = 0; k < 4; k++)
                {
                    int nx = lx + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = ly + (k == 2 ? 1 : k == 3 ? -1 : 0);
                    if (nx < 0 || ny < 0 || nx >= Window || ny >= Window) continue;
                    int ni = ny * Window + nx;
                    if (_dist[ni] <= nd) continue;
                    if (!_map.Walkable(nx + _ox, ny + _oy)) continue;
                    _dist[ni] = nd; _q.Enqueue(ni);
                }
            }
        }

        /// <summary>Направление движения из точки к цели или Zero, если вне окна.</summary>
        public Vec2 Direction(Vec2 from)
        {
            _map.TileXY(from, out int tx, out int ty);
            int lx = tx - _ox, ly = ty - _oy;
            if (lx < 1 || ly < 1 || lx >= Window - 1 || ly >= Window - 1) return Vec2.Zero;
            int best = _dist[ly * Window + lx];
            if (best == ushort.MaxValue) return Vec2.Zero;
            int bx = 0, by = 0;
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dy == 0) continue;
                int d = _dist[(ly + dy) * Window + lx + dx];
                if (dx != 0 && dy != 0 && (_dist[ly * Window + lx + dx] == ushort.MaxValue || _dist[(ly + dy) * Window + lx] == ushort.MaxValue)) continue;
                if (d < best) { best = d; bx = dx; by = dy; }
            }
            if (bx == 0 && by == 0) return Vec2.Zero;
            Vec2 target = _map.TileCenter(tx + bx, ty + by);
            return (target - from).Normalized;
        }
    }

    /// <summary>Поле зрения и исследованные тайлы (туман войны).</summary>
    public static class Vision
    {
        public static void EnsureExplored(GameState s, int size)
        {
            int bytes = (size * size + 7) / 8;
            if (s.WorldMods.Explored == null || s.WorldMods.Explored.Length != bytes) s.WorldMods.Explored = new byte[bytes];
        }

        public static bool IsExplored(GameState s, int idx) => (s.WorldMods.Explored[idx >> 3] & (1 << (idx & 7))) != 0;

        /// <summary>Отмечает тайлы в радиусе как исследованные. Возвращает true, если появились новые.</summary>
        public static bool Reveal(GameState s, WorldMap map, Vec2 center, float radius)
        {
            bool changed = false;
            map.TileXY(center, out int cx, out int cy);
            int r = (int)Math.Ceiling(radius / map.TileSize);
            float r2 = (radius / map.TileSize) * (radius / map.TileSize);
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (dx * dx + dy * dy > r2) continue;
                int x = cx + dx, y = cy + dy;
                if (!map.InBounds(x, y)) continue;
                int idx = y * map.Size + x;
                int b = idx >> 3, bit = 1 << (idx & 7);
                if ((s.WorldMods.Explored[b] & bit) == 0) { s.WorldMods.Explored[b] |= (byte)bit; changed = true; }
            }
            return changed;
        }
    }
}
