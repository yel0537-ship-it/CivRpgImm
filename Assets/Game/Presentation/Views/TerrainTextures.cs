using Immortal.Core;
using Immortal.Core.Content;
using UnityEngine;

namespace Immortal.Presentation
{
    /// <summary>
    /// Собирает массив текстур местности. Для каждого типа из terrain.json берётся Resources/Textures/Terrain/&lt;id&gt;
    /// (любой размер и формат — пережимается до 256×256), а если файла нет — рисуется процедурная текстура в цвет местности.
    /// </summary>
    public static class TerrainTextures
    {
        public const int Size = 256;

        public static Texture2DArray Build(ContentDatabase db)
        {
            int n = db.TerrainByIndex.Count;
            var arr = new Texture2DArray(Size, Size, n, TextureFormat.RGBA32, true) { name = "TerrainLayers", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 4 };
            for (int i = 0; i < n; i++)
            {
                var t = db.TerrainByIndex[i];
                var real = Resources.Load<Texture2D>("Textures/Terrain/" + t.id);
                Color32[] px = real != null ? Resample(real) : Procedural(t, i);
                arr.SetPixels32(px, i, 0);
            }
            arr.Apply(true, true);
            return arr;
        }

        static Color32[] Resample(Texture2D src)
        {
            var rt = RenderTexture.GetTemporary(Size, Size, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(src, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tmp = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            tmp.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tmp.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = tmp.GetPixels32();
            Object.Destroy(tmp);
            return px;
        }

        // ---------- Процедурные текстуры (бесшовные) ----------
        static float Hash(int x, int y, int seed) => MathUtil.Hash01(x, y, seed);

        static float PNoise(float x, float y, int period, int seed)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float tx = x - xi, ty = y - yi;
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            int x0 = ((xi % period) + period) % period, y0 = ((yi % period) + period) % period;
            int x1 = (x0 + 1) % period, y1 = (y0 + 1) % period;
            float a = Hash(x0, y0, seed), b = Hash(x1, y0, seed), c = Hash(x0, y1, seed), d = Hash(x1, y1, seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        static float Fbm(float u, float v, int seed, int baseFreq = 4, int octaves = 4)
        {
            float sum = 0, amp = 0.5f, norm = 0; int f = baseFreq;
            for (int o = 0; o < octaves; o++) { sum += PNoise(u * f, v * f, f, seed + o * 31) * amp; norm += amp; amp *= 0.5f; f *= 2; }
            return sum / norm;
        }

        static Color32[] Procedural(TerrainDef t, int seed)
        {
            var baseCol = Look.Hex(t.color);
            var px = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = x / (float)Size, v = y / (float)Size;
                float n = Fbm(u, v, seed * 17 + 3);
                float fine = Fbm(u, v, seed * 17 + 9, 16, 2);
                float k = 0.85f + (n - 0.5f) * 0.35f + (fine - 0.5f) * 0.15f;
                Color c = baseCol * k;
                switch (t.id)
                {
                    case "grass":
                    case "steppe":
                        // Травинки: короткие светлые штрихи.
                        float blade = Hash(x / 2, y / 6, seed) > 0.92f ? 1.15f : 1f;
                        c *= blade;
                        if (t.id == "steppe") c = Color.Lerp(c, new Color(0.8f, 0.72f, 0.45f), (fine - 0.4f) * 0.4f);
                        break;
                    case "forest":
                        c = Color.Lerp(c, new Color(0.3f, 0.22f, 0.12f), Mathf.Clamp01((n - 0.55f) * 2.5f) * 0.5f); // опавшая листва
                        if (Hash(x / 3, y / 3, seed + 5) > 0.97f) c *= 1.25f;
                        break;
                    case "sand":
                        float ripple = Mathf.Sin((u * 12f + n * 1.5f) * Mathf.PI * 2) * 0.06f;
                        c *= 1f + ripple;
                        break;
                    case "swamp":
                        float wet = Mathf.Clamp01((n - 0.58f) * 6f);
                        c = Color.Lerp(c, new Color(0.18f, 0.28f, 0.26f), wet * 0.7f);
                        break;
                    case "snow":
                        c = Color.Lerp(c, Color.white, 0.2f) * (0.95f + (fine - 0.5f) * 0.1f);
                        if (Hash(x, y, seed + 77) > 0.995f) c = Color.white * 1.1f;
                        break;
                    case "hills":
                    case "mountain":
                        float crack = Mathf.Abs(Fbm(u, v, seed * 5 + 1, 6, 3) - 0.5f) < 0.02f ? 0.65f : 1f;
                        c *= crack;
                        break;
                    case "water":
                        float wave = Mathf.Sin((v * 10f + n * 2f) * Mathf.PI * 2) * 0.5f + 0.5f;
                        c = Color.Lerp(c, c * 1.35f, wave * wave * 0.4f);
                        break;
                    case "field":
                        float furrow = Mathf.Sin(u * 8f * Mathf.PI * 2) > 0.3f ? 1.08f : 0.82f;
                        c *= furrow;
                        break;
                    case "road":
                        if (Hash(x / 2, y / 2, seed + 13) > 0.9f) c *= 0.75f;
                        if (Hash(x / 3, y / 3, seed + 14) > 0.95f) c *= 1.2f;
                        break;
                }
                c.a = 1;
                px[y * Size + x] = c;
            }
            return px;
        }
    }
}
