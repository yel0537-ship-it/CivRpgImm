namespace Immortal.Core
{
    public interface IRandom
    {
        /// <summary>[0, 1)</summary>
        float NextFloat();
        /// <summary>[0, max)</summary>
        int NextInt(int max);
        int Range(int minInclusive, int maxExclusive);
        float Range(float min, float max);
        bool Chance(float p);
        ulong State { get; set; }
    }

    /// <summary>xorshift64* — детерминированный, состояние хранится в GameState.</summary>
    public sealed class XorShiftRandom : IRandom
    {
        ulong _s;
        public XorShiftRandom(ulong seed) { _s = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }
        public ulong State { get => _s; set => _s = value == 0 ? 0x9E3779B97F4A7C15UL : value; }

        ulong Next()
        {
            _s ^= _s >> 12; _s ^= _s << 25; _s ^= _s >> 27;
            return _s * 2685821657736338717UL;
        }

        public float NextFloat() => (Next() >> 40) / 16777216f;
        public int NextInt(int max) => max <= 0 ? 0 : (int)((Next() >> 33) % (ulong)max);
        public int Range(int a, int b) => b <= a ? a : a + NextInt(b - a);
        public float Range(float a, float b) => a + (b - a) * NextFloat();
        public bool Chance(float p) => NextFloat() < p;
    }

    /// <summary>Подменяемый генератор для тестов: выдаёт заданную последовательность.</summary>
    public sealed class ScriptedRandom : IRandom
    {
        readonly float[] _values; int _i;
        public ScriptedRandom(params float[] values) { _values = values; }
        public ulong State { get; set; }
        public float NextFloat() { float v = _values[_i % _values.Length]; _i++; return v; }
        public int NextInt(int max) => max <= 0 ? 0 : (int)(NextFloat() * max) % max;
        public int Range(int a, int b) => b <= a ? a : a + NextInt(b - a);
        public float Range(float a, float b) => a + (b - a) * NextFloat();
        public bool Chance(float p) => NextFloat() < p;
    }
}
