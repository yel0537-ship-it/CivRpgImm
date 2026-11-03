using System;

namespace Immortal.Core
{
    /// <summary>Собственный 2D-вектор ядра (x — восток, y — север). Ядро не зависит от UnityEngine.</summary>
    public struct Vec2 : IEquatable<Vec2>
    {
        public float x;
        public float y;

        public Vec2(float x, float y) { this.x = x; this.y = y; }

        public static readonly Vec2 Zero = new Vec2(0, 0);

        public float Length => (float)Math.Sqrt(x * x + y * y);
        public float SqrLength => x * x + y * y;

        public Vec2 Normalized
        {
            get
            {
                float l = Length;
                return l > 1e-5f ? new Vec2(x / l, y / l) : Zero;
            }
        }

        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.x + b.x, a.y + b.y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.x - b.x, a.y - b.y);
        public static Vec2 operator -(Vec2 a) => new Vec2(-a.x, -a.y);
        public static Vec2 operator *(Vec2 a, float k) => new Vec2(a.x * k, a.y * k);
        public static Vec2 operator *(float k, Vec2 a) => new Vec2(a.x * k, a.y * k);
        public static Vec2 operator /(Vec2 a, float k) => new Vec2(a.x / k, a.y / k);

        public static float Dot(Vec2 a, Vec2 b) => a.x * b.x + a.y * b.y;
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static float SqrDistance(Vec2 a, Vec2 b) => (a - b).SqrLength;
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t);
        public static Vec2 FromAngle(float rad) => new Vec2((float)Math.Cos(rad), (float)Math.Sin(rad));
        public float Angle => (float)Math.Atan2(y, x);

        public Vec2 ClampLength(float max)
        {
            float l = Length;
            return l > max && l > 0 ? this * (max / l) : this;
        }

        public bool Equals(Vec2 o) => x == o.x && y == o.y;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => x.GetHashCode() * 397 ^ y.GetHashCode();
        public override string ToString() => $"({x:0.0}, {y:0.0})";
    }

    public static class MathUtil
    {
        public static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        public static double Clamp(double v, double a, double b) => v < a ? a : (v > b ? b : v);
        public static int Clamp(int v, int a, int b) => v < a ? a : (v > b ? b : v);
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * t;

        /// <summary>Детерминированный хеш целых координат (для генерации без хранения).</summary>
        public static uint Hash(int a, int b, int seed)
        {
            unchecked
            {
                uint h = (uint)seed * 374761393u + (uint)a * 668265263u + (uint)b * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                return h ^ (h >> 16);
            }
        }

        public static float Hash01(int a, int b, int seed) => (Hash(a, b, seed) & 0xFFFFFF) / 16777216f;

        /// <summary>Сглаженный value-noise для генерации местности.</summary>
        public static float ValueNoise(float x, float y, int seed)
        {
            int xi = (int)Math.Floor(x), yi = (int)Math.Floor(y);
            float tx = x - xi, ty = y - yi;
            tx = tx * tx * (3 - 2 * tx);
            ty = ty * ty * (3 - 2 * ty);
            float a = Hash01(xi, yi, seed), b = Hash01(xi + 1, yi, seed);
            float c = Hash01(xi, yi + 1, seed), d = Hash01(xi + 1, yi + 1, seed);
            return Lerp(Lerp(a, b, tx), Lerp(c, d, tx), ty);
        }

        public static float Fbm(float x, float y, int seed, int octaves = 4)
        {
            float sum = 0, amp = 0.5f, norm = 0;
            for (int i = 0; i < octaves; i++)
            {
                sum += ValueNoise(x, y, seed + i * 1013) * amp;
                norm += amp;
                x *= 2.03f; y *= 2.03f; amp *= 0.5f;
            }
            return sum / norm;
        }

        public static ulong Fnv1a(string s)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                foreach (char c in s) { h ^= c; h *= 1099511628211UL; }
                return h;
            }
        }
    }
}
