using System.Collections.Generic;
using Immortal.Core;
using UnityEngine;

namespace Immortal.Presentation
{
    /// <summary>
    /// Общие ресурсы визуала: материалы на своих шейдерах, сетки примитивов, цвета и перевод координат.
    /// Все виды строятся из примитивов, поэтому их легко заменить моделями, не трогая логику.
    /// </summary>
    public static class Look
    {
        static Shader _lit, _transparent;
        static readonly Dictionary<PrimitiveType, Mesh> Meshes = new Dictionary<PrimitiveType, Mesh>();
        static readonly Dictionary<string, Color> ColorCache = new Dictionary<string, Color>();
        public static Material Lit, LitInstanced, Transparent, Terrain;
        public static readonly int SeasonId = Shader.PropertyToID("_ImmSeason");

        /// <summary>Материал местности с массивом текстур. null — местность рисуется цветами вершин.</summary>
        public static void InitTerrain(Immortal.Core.Content.ContentDatabase db)
        {
            var sh = Shader.Find("Immortal/Terrain");
            if (sh == null || !SystemInfo.supports2DArrayTextures) { Terrain = null; return; }
            Terrain = new Material(sh) { name = "Immortal Terrain" };
            Terrain.SetTexture("_TerrainTex", TerrainTextures.Build(db));
            Terrain.SetFloat("_Tiling", 0.25f);
        }

        public static readonly int ColorId = Shader.PropertyToID("_Color");
        public static readonly int EmissionId = Shader.PropertyToID("_Emission");
        public static readonly int PulseId = Shader.PropertyToID("_Pulse");
        public static readonly int HeroPosId = Shader.PropertyToID("_ImmHeroPos");
        public static readonly int VisionId = Shader.PropertyToID("_ImmVision");
        public static readonly int FogDarkId = Shader.PropertyToID("_ImmFogDark");

        public static void Init()
        {
            if (Lit != null) return;
            _lit = Shader.Find("Immortal/Lit");
            _transparent = Shader.Find("Immortal/Transparent");
            if (_lit == null) { Debug.LogWarning("Шейдер Immortal/Lit не найден, беру запасной."); _lit = FallbackShader(false); }
            if (_transparent == null) _transparent = FallbackShader(true);
            Lit = new Material(_lit) { name = "Immortal Lit" };
            LitInstanced = new Material(_lit) { name = "Immortal Lit Instanced", enableInstancing = true };
            Transparent = new Material(_transparent) { name = "Immortal Transparent" };
            Shader.SetGlobalFloat(VisionId, 999f);
            Shader.SetGlobalFloat(FogDarkId, 0f);
        }

        static Shader FallbackShader(bool transparent)
        {
            var s = Shader.Find(transparent ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Simple Lit");
            if (s == null) s = Shader.Find(transparent ? "Unlit/Transparent" : "Standard");
            return s;
        }

        public static Mesh Mesh(PrimitiveType t)
        {
            if (Meshes.TryGetValue(t, out var m)) return m;
            var go = GameObject.CreatePrimitive(t);
            m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            Meshes[t] = m;
            return m;
        }

        /// <summary>Объект из примитива без коллайдера, с общим материалом и цветом через MaterialPropertyBlock.</summary>
        public static GameObject Prim(PrimitiveType t, Transform parent, Color color, Vector3 localPos, Vector3 scale, bool transparent = false, string name = null)
        {
            var go = new GameObject(name ?? t.ToString());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = Mesh(t);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = transparent ? Transparent : Lit;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            SetColor(r, color);
            return go;
        }

        static readonly MaterialPropertyBlock Mpb = new MaterialPropertyBlock();

        public static void SetColor(Renderer r, Color c, float emission = 0f)
        {
            r.GetPropertyBlock(Mpb);
            Mpb.SetColor(ColorId, c);
            Mpb.SetFloat(EmissionId, emission);
            r.SetPropertyBlock(Mpb);
        }

        public static Color Hex(string hex, Color fallback = default)
        {
            if (string.IsNullOrEmpty(hex)) return fallback == default ? Color.white : fallback;
            if (ColorCache.TryGetValue(hex, out var c)) return c;
            if (!ColorUtility.TryParseHtmlString(hex, out c)) c = fallback == default ? Color.magenta : fallback;
            ColorCache[hex] = c;
            return c;
        }

        public static Vector3 ToWorld(Vec2 p, float y) => new Vector3(p.x, y, p.y);
        public static Vec2 ToCore(Vector3 p) => new Vec2(p.x, p.z);

        static readonly Color[] Cloth =
        {
            new Color(0.62f, 0.45f, 0.3f), new Color(0.48f, 0.55f, 0.35f), new Color(0.55f, 0.3f, 0.28f), new Color(0.35f, 0.42f, 0.55f),
            new Color(0.6f, 0.55f, 0.42f), new Color(0.42f, 0.32f, 0.45f), new Color(0.7f, 0.62f, 0.5f), new Color(0.3f, 0.38f, 0.32f)
        };
        static readonly Color[] Skins =
        {
            new Color(0.92f, 0.76f, 0.62f), new Color(0.85f, 0.66f, 0.52f), new Color(0.72f, 0.54f, 0.4f), new Color(0.58f, 0.42f, 0.3f), new Color(0.95f, 0.82f, 0.7f)
        };

        /// <summary>Внешность по FaceId: одинаковый FaceId — одинаковое лицо и одежда (намёк на симуляцию у потомков).</summary>
        public static void Face(int faceId, out Color cloth, out Color skin)
        {
            uint h = MathUtil.Hash(faceId, 7, 13);
            cloth = Cloth[h % (uint)Cloth.Length];
            skin = Skins[(h >> 8) % (uint)Skins.Length];
        }

        public static readonly Color HeroColor = new Color(0.95f, 0.78f, 0.25f);
        public static readonly Color CompanionColor = new Color(0.45f, 0.75f, 1f);
        public static readonly Color NpcColor = new Color(0.92f, 0.9f, 0.82f);
        public static readonly Color HostileNpcColor = new Color(0.85f, 0.25f, 0.2f);
    }

    /// <summary>Камера сверху под углом, как в Diablo: следует за героем, колёсико отдаляет.</summary>
    public sealed class CameraRig
    {
        public readonly Camera Cam;
        public float Distance = 26f;
        public float Pitch = 55f;
        Vector3 _focus;
        bool _init;
        float _shake;

        public void Shake(float amount) { if (GameSettings.CameraShake) _shake = Mathf.Max(_shake, amount); }

        public CameraRig()
        {
            var cam = Camera.main;
            if (cam == null)
            {
                var go = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = go.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
            cam.nearClipPlane = 0.3f; cam.farClipPlane = 500f;
            cam.fieldOfView = 45f;
            Cam = cam;
        }

        public void Update(Vector3 target, float scroll, float dt)
        {
            Distance = Mathf.Clamp(Distance - scroll * 3f, 12f, 70f);
            if (!_init) { _focus = target; _init = true; }
            _focus = Vector3.Lerp(_focus, target, 1f - Mathf.Exp(-dt * 8f));
            var rot = Quaternion.Euler(Pitch, 0f, 0f);
            Vector3 jitter = Vector3.zero;
            if (_shake > 0.001f)
            {
                jitter = new Vector3(Mathf.PerlinNoise(Time.time * 40f, 0) - 0.5f, Mathf.PerlinNoise(0, Time.time * 40f) - 0.5f, 0) * _shake;
                _shake = Mathf.MoveTowards(_shake, 0, dt * 3f);
            }
            Cam.transform.position = _focus + rot * new Vector3(0, 0, -Distance) + jitter;
            Cam.transform.rotation = rot;
        }

        public void Snap(Vector3 target) { _focus = target; _init = true; }

        /// <summary>Луч камеры пересекается с плоскостью земли на заданной высоте.</summary>
        public bool GroundPoint(Vector2 screen, float groundY, out Vector3 point)
        {
            var ray = Cam.ScreenPointToRay(screen);
            var plane = new Plane(Vector3.up, new Vector3(0, groundY, 0));
            if (plane.Raycast(ray, out float d)) { point = ray.GetPoint(d); return true; }
            point = Vector3.zero;
            return false;
        }
    }
}
