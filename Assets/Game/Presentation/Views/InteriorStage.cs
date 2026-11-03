using Immortal.Core;
using UnityEngine;

namespace Immortal.Presentation
{
    /// <summary>
    /// Интерьер постройки: отдельная «сцена-диорама» далеко под миром, которую снимает своя камера в RenderTexture.
    /// Префаб Resources/Interiors/&lt;id постройки&gt; (или Interiors/default) — если есть; иначе процедурная комната.
    /// </summary>
    public sealed class InteriorStage
    {
        static readonly Vector3 Origin = new Vector3(0, -500, 0);
        readonly Transform _root;
        Camera _cam;
        RenderTexture _rt;
        GameObject _set;
        float _angle;

        public RenderTexture Texture => _rt;

        public InteriorStage(Transform parent)
        {
            _root = new GameObject("InteriorStage").transform;
            _root.SetParent(parent, false);
            _root.position = Origin;
        }

        public RenderTexture Show(GameSession s, string buildingId, string cityId)
        {
            Hide();
            if (_rt == null) _rt = new RenderTexture(960, 540, 24) { name = "InteriorRT", antiAliasing = 2 };
            if (_cam == null)
            {
                var cg = new GameObject("InteriorCamera");
                cg.transform.SetParent(_root, false);
                _cam = cg.AddComponent<Camera>();
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0.05f, 0.04f, 0.035f);
                _cam.fieldOfView = 40; _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 60f;
                _cam.targetTexture = _rt;
            }
            _cam.gameObject.SetActive(true);
            Shader.SetGlobalVector(Look.HeroPosId, Origin);
            var prefab = Resources.Load<GameObject>("Interiors/" + buildingId) ?? Resources.Load<GameObject>("Interiors/default");
            if (prefab != null) { _set = Object.Instantiate(prefab, _root); _set.transform.localPosition = Vector3.zero; }
            else _set = BuildRoom(s, buildingId, cityId);
            _angle = 0;
            return _rt;
        }

        public void Hide()
        {
            if (_set != null) Object.Destroy(_set);
            _set = null;
            if (_cam != null) _cam.gameObject.SetActive(false);
        }

        public bool Active => _set != null;

        /// <summary>Медленный облёт комнаты и «жизнь» людей внутри.</summary>
        public void Update(float dt)
        {
            if (_set == null || _cam == null) return;
            _angle += dt * 6f;
            var rot = Quaternion.Euler(28, 200 + Mathf.Sin(_angle * Mathf.Deg2Rad * 3f) * 25f, 0);
            _cam.transform.position = Origin + new Vector3(0, 1.2f, 0) + rot * new Vector3(0, 0, -11f);
            _cam.transform.rotation = rot;
            foreach (var a in _set.GetComponentsInChildren<ProceduralAnimator>()) a.Tick(new UnitAnimState { Speed = 0, Gather = a.name == "worker" }, dt);
            // Свой шейдер не должен «туманить» комнату: центр зрения — здесь.
            Shader.SetGlobalVector(Look.HeroPosId, Origin);
            Shader.SetGlobalFloat(Look.VisionId, 999f);
        }

        GameObject BuildRoom(GameSession s, string id, string cityId)
        {
            var set = new GameObject("Room " + id);
            set.transform.SetParent(_root, false);
            var t = set.transform;
            var def = s.Content.Buildings.TryGetValue(id, out var d) ? d : null;
            var wall = def != null ? Look.Hex(def.color) * 0.8f : new Color(0.5f, 0.42f, 0.35f);
            var floor = new Color(0.36f, 0.28f, 0.2f);
            Look.Prim(PrimitiveType.Cube, t, floor, new Vector3(0, -0.05f, 0), new Vector3(12, 0.1f, 9));
            Look.Prim(PrimitiveType.Cube, t, wall, new Vector3(0, 2.5f, 4.5f), new Vector3(12, 5, 0.3f));
            Look.Prim(PrimitiveType.Cube, t, wall * 0.9f, new Vector3(-6, 2.5f, 0), new Vector3(0.3f, 5, 9));
            Look.Prim(PrimitiveType.Cube, t, wall * 0.9f, new Vector3(6, 2.5f, 0), new Vector3(0.3f, 5, 9));
            for (int i = 0; i < 5; i++) Look.Prim(PrimitiveType.Cube, t, wall * 0.6f, new Vector3(-5 + i * 2.5f, 4.9f, 0), new Vector3(0.25f, 0.25f, 9));
            Color glow;
            switch (id)
            {
                case "workshop":
                case "forge":
                    Look.Prim(PrimitiveType.Cube, t, new Color(0.45f, 0.32f, 0.2f), new Vector3(-2, 0.5f, 1.5f), new Vector3(3, 1, 1.2f));
                    Look.Prim(PrimitiveType.Cube, t, new Color(0.3f, 0.3f, 0.32f), new Vector3(2, 0.45f, 1), new Vector3(1, 0.9f, 0.6f));
                    glow = new Color(1f, 0.5f, 0.2f);
                    Glow(t, new Vector3(3.8f, 0.6f, 3.2f), glow, 0.9f);
                    break;
                case "school":
                    for (int i = 0; i < 3; i++) Look.Prim(PrimitiveType.Cube, t, new Color(0.5f, 0.38f, 0.25f), new Vector3(-3 + i * 3, 0.4f, 0), new Vector3(2.2f, 0.8f, 0.7f));
                    Look.Prim(PrimitiveType.Cube, t, new Color(0.9f, 0.86f, 0.75f), new Vector3(0, 2.4f, 4.3f), new Vector3(5, 2.2f, 0.05f));
                    break;
                case "shrine":
                case "laboratory":
                    glow = id == "shrine" ? new Color(0.7f, 0.5f, 1f) : new Color(0.55f, 0.9f, 1f);
                    Look.Prim(PrimitiveType.Cylinder, t, new Color(0.4f, 0.38f, 0.42f), new Vector3(0, 0.5f, 2), new Vector3(1.6f, 0.5f, 1.6f));
                    Glow(t, new Vector3(0, 1.7f, 2), glow, 1.1f);
                    if (id == "laboratory")
                        for (int i = 0; i < 5; i++) Glow(t, new Vector3(-4 + i * 2, 0.4f, 3.6f), glow * 0.8f, 0.35f);
                    break;
                case "palace":
                case "chief_house":
                    var nation = s.Nation(s.City(cityId)?.NationId);
                    var ncol = nation != null ? Look.Hex(nation.Color) : Look.HeroColor;
                    Look.Prim(PrimitiveType.Cube, t, ncol * 0.7f, new Vector3(0, 0.02f, 0.5f), new Vector3(3, 0.04f, 7));
                    Look.Prim(PrimitiveType.Cube, t, new Color(0.8f, 0.65f, 0.3f), new Vector3(0, 0.8f, 3.4f), new Vector3(1.6f, 1.6f, 1));
                    Look.Prim(PrimitiveType.Cube, t, new Color(0.8f, 0.65f, 0.3f), new Vector3(0, 2f, 3.8f), new Vector3(1.6f, 1.6f, 0.2f));
                    if (id == "palace") for (int i = 0; i < 4; i++) Look.Prim(PrimitiveType.Cylinder, t, new Color(0.85f, 0.8f, 0.7f), new Vector3((i % 2 == 0 ? -4 : 4), 2.5f, -2 + (i / 2) * 4), new Vector3(0.7f, 2.5f, 0.7f));
                    Glow(t, new Vector3(-4.5f, 0.6f, 3.5f), new Color(1f, 0.55f, 0.2f), 0.7f);
                    Glow(t, new Vector3(4.5f, 0.6f, 3.5f), new Color(1f, 0.55f, 0.2f), 0.7f);
                    break;
                case "market":
                    for (int i = 0; i < 3; i++)
                    {
                        Look.Prim(PrimitiveType.Cube, t, new Color(0.55f, 0.4f, 0.25f), new Vector3(-3.5f + i * 3.5f, 0.5f, 2), new Vector3(2.4f, 1, 1));
                        Look.Prim(PrimitiveType.Cube, t, new[] { new Color(0.8f, 0.3f, 0.25f), new Color(0.3f, 0.6f, 0.8f), new Color(0.85f, 0.75f, 0.3f) }[i], new Vector3(-3.5f + i * 3.5f, 2.2f, 2), new Vector3(2.6f, 0.1f, 1.4f));
                    }
                    break;
                default:
                    Glow(t, new Vector3(0, 0.4f, 1.5f), new Color(1f, 0.55f, 0.2f), 0.8f);
                    Look.Prim(PrimitiveType.Cube, t, new Color(0.5f, 0.38f, 0.25f), new Vector3(-3, 0.4f, 2.5f), new Vector3(2, 0.8f, 1));
                    break;
            }
            // Люди внутри: наместник (если есть) и пара жителей.
            var city = s.City(cityId);
            var gov = s.Companion(city?.GovernorId);
            int people = 2 + (city != null ? Mathf.Clamp((int)(city.Population / 40), 0, 3) : 0);
            for (int i = 0; i < people; i++)
            {
                Color cloth = new Color(0.6f, 0.5f, 0.4f), skin = new Color(0.85f, 0.68f, 0.55f);
                if (i == 0 && gov != null) Look.Face(gov.FaceId, out cloth, out skin);
                else Look.Face(i * 37 + 5, out cloth, out skin);
                var p = new GameObject(i == 1 ? "worker" : "person");
                p.transform.SetParent(t, false);
                p.transform.localPosition = new Vector3(-3f + i * 2f, 0, -0.5f - (i % 2));
                p.transform.localRotation = Quaternion.Euler(0, 180 + (i - 1) * 25, 0);
                var body = ViewFactory.BuildHumanoid(p.transform, cloth, 1f, new Unit { Kind = UnitKind.Npc }, out _, skin);
                var anim = p.AddComponent<ProceduralAnimator>();
                anim.name = p.name;
                anim.Init(body, false);
            }
            return set;
        }

        static void Glow(Transform t, Vector3 pos, Color c, float size)
        {
            var g = Look.Prim(PrimitiveType.Sphere, t, c, pos, Vector3.one * size, false, "glow");
            Look.SetColor(g.GetComponent<Renderer>(), c, 1.3f);
            g.AddComponent<FlameFlicker>();
        }
    }
}
