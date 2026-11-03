using System.Collections.Generic;
using Immortal.Core;
using Immortal.Core.Content;
using UnityEngine;

namespace Immortal.Presentation
{
    /// <summary>Собранный вид юнита: модель или примитив, аниматор, точка оружия, рендереры для вспышки.</summary>
    public sealed class UnitRig
    {
        public GameObject Root;
        public Transform Model;
        public Renderer[] Renderers;
        public bool IsPrefab;
        public IUnitAnimator Animator;
        public float Height = 1.8f;
        public Transform Hand;
        public GameObject Weapon;
        public string WeaponDefId;
        public GameObject Mount;
        public Color BaseColor;
        public Color[] BaseColors;
        public float Emission;
        public float Punch;
    }

    public sealed class NodePart
    {
        public Mesh Mesh; public Material Material; public Matrix4x4 Local; public Color? FixedColor; public bool UsesInstanceColor;
    }

    public sealed class NodeVisual { public readonly List<NodePart> Parts = new List<NodePart>(); }

    /// <summary>
    /// Все виды создаются здесь. Для каждого объекта сначала ищется модель в VisualCatalog,
    /// а если её нет — строится примитив. Логика игры и остальные виды об этом не знают.
    /// </summary>
    public static class ViewFactory
    {
        static readonly string[] HandNames = { "hand_r", "Hand_R", "RightHand", "mixamorig:RightHand", "Hand.R", "weapon" };

        // ---------- Юниты ----------
        public static string[] UnitKeys(GameSession s, Unit u)
        {
            switch (u.Kind)
            {
                case UnitKind.Hero: return new[] { "hero" };
                case UnitKind.Companion:
                    var c = s.Companion(u.CompanionId);
                    return new[] { c?.RecruitDefId, "companion" };
                case UnitKind.Npc:
                    if (u.CompanionId != null) { var cc = s.Companion(u.CompanionId); return new[] { cc?.RecruitDefId, "companion", "npc" }; }
                    return new[] { u.DefId, "npc" };
                case UnitKind.Leader:
                    var n = s.Nation(u.NationId);
                    return new[] { "leader_" + n?.DefId, "leader" };
                default: return new[] { u.DefId };
            }
        }

        public static UnitRig CreateUnit(GameSession s, Unit u, Transform parent, Color color)
        {
            var rig = new UnitRig { BaseColor = color };
            rig.Root = new GameObject($"Unit {u.Name} #{u.Id}");
            rig.Root.transform.SetParent(parent, false);
            float scale = 1f;
            if (u.DefId != null && s.Content.Enemies.TryGetValue(u.DefId, out var d)) scale = d.scale;
            if (u.Kind == UnitKind.Leader) scale = 1.25f;

            var ve = VisualCatalog.Resolve("unit", UnitKeys(s, u));
            if (ve != null)
            {
                var model = Object.Instantiate(ve.Prefab, rig.Root.transform);
                model.transform.localPosition = new Vector3(0, ve.yOffset, 0);
                model.transform.localRotation = Quaternion.Euler(0, ve.rotationY, 0);
                model.transform.localScale = model.transform.localScale * ve.scale * scale;
                rig.Model = model.transform;
                rig.IsPrefab = true;
                rig.Renderers = model.GetComponentsInChildren<Renderer>();
                rig.Height = ve.height > 0 ? ve.height : MeasureHeight(rig.Renderers, rig.Root.transform, 1.8f * scale);
                rig.Hand = FindDeep(model.transform, ve.handBone);
                var custom = model.GetComponentInChildren<IUnitAnimator>();
                if (custom != null) rig.Animator = custom;
                else
                {
                    var anim = model.GetComponentInChildren<Animator>();
                    if (anim != null) { var br = rig.Root.AddComponent<AnimatorBridge>(); br.Init(anim); rig.Animator = br; }
                    else { var pa = rig.Root.AddComponent<ProceduralAnimator>(); pa.Init(model.transform, false); rig.Animator = pa; }
                }
                if (ve.tintByNation) Tint(rig.Renderers, color);
                return rig;
            }

            // Примитив: «кукла» из туловища, головы и конечностей на шарнирах (их качает ProceduralAnimator).
            bool quadruped = u.Kind == UnitKind.Animal || (u.DefId != null && (u.DefId.StartsWith("wolf") || u.DefId == "bear" || u.DefId == "shade_beast" || u.DefId == "boar"));
            Transform body = quadruped ? BuildQuadruped(rig.Root.transform, color, scale) : BuildHumanoid(rig.Root.transform, color, scale, u, out rig.Hand);
            rig.Height = (quadruped ? 1.25f : 1.95f) * scale;
            if (u.Kind == UnitKind.Spirit)
                foreach (var r in body.GetComponentsInChildren<Renderer>()) Look.SetColor(r, color, 0.5f);
            rig.Model = body;
            rig.Renderers = body.GetComponentsInChildren<Renderer>();
            rig.BaseColors = new Color[rig.Renderers.Length];
            var mpb = new MaterialPropertyBlock();
            for (int i = 0; i < rig.Renderers.Length; i++) { rig.Renderers[i].GetPropertyBlock(mpb); rig.BaseColors[i] = mpb.GetColor(Look.ColorId); }
            rig.Emission = u.Kind == UnitKind.Spirit ? 0.5f : 0f;
            var proc = rig.Root.AddComponent<ProceduralAnimator>();
            proc.Init(body, quadruped);
            rig.Animator = proc;
            return rig;
        }

        static readonly Color Skin = new Color(0.85f, 0.68f, 0.55f);

        static Transform Pivot(Transform parent, string name, Vector3 pos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = pos;
            return t;
        }

        public static Transform BuildHumanoid(Transform root, Color color, float s, Unit u, out Transform hand)
        {
            var body = Pivot(root, "body", new Vector3(0, 0.95f * s, 0));
            body.localScale = Vector3.one * s;
            bool spirit = u.Kind == UnitKind.Spirit;
            var head = spirit ? color * 1.2f : Skin;
            Look.Prim(PrimitiveType.Capsule, body, color, new Vector3(0, 0.36f, 0), new Vector3(0.55f, 0.42f, 0.38f), false, "torso");
            Look.Prim(PrimitiveType.Sphere, body, head, new Vector3(0, 0.98f, 0), Vector3.one * 0.36f, false, "head");
            Look.Prim(PrimitiveType.Cube, body, color * 0.55f, new Vector3(0, 0.98f, 0.16f), new Vector3(0.22f, 0.07f, 0.08f), false, "eyes");
            if (u.Kind == UnitKind.Leader || u.Boss)
                Look.Prim(PrimitiveType.Cube, body, new Color(1f, 0.85f, 0.2f), new Vector3(0, 1.2f, 0), new Vector3(0.34f, 0.1f, 0.34f), false, "crown");
            if (u.Kind == UnitKind.Hero)
                Look.Prim(PrimitiveType.Cube, body, color * 0.7f, new Vector3(0, 0.38f, -0.2f), new Vector3(0.5f, 0.75f, 0.05f), false, "cape");
            var limb = color * 0.85f;
            foreach (var side in new[] { -1, 1 })
            {
                var arm = Pivot(body, side < 0 ? "arm_l" : "arm_r", new Vector3(0.33f * side, 0.66f, 0));
                Look.Prim(PrimitiveType.Capsule, arm, limb, new Vector3(0, -0.3f, 0), new Vector3(0.14f, 0.3f, 0.14f), false, "arm");
                Look.Prim(PrimitiveType.Sphere, arm, head, new Vector3(0, -0.6f, 0), Vector3.one * 0.13f, false, "fist");
                var leg = Pivot(body, side < 0 ? "leg_l" : "leg_r", new Vector3(0.13f * side, 0.02f, 0));
                Look.Prim(PrimitiveType.Capsule, leg, color * 0.6f, new Vector3(0, -0.45f, 0), new Vector3(0.17f, 0.43f, 0.17f), false, "leg");
            }
            hand = Pivot(body.Find("arm_r"), "hand_r", new Vector3(0, -0.62f, 0.05f));
            return body;
        }

        static Transform BuildQuadruped(Transform root, Color color, float s)
        {
            var body = Pivot(root, "body", new Vector3(0, 0.62f * s, 0));
            body.localScale = Vector3.one * s;
            var torso = Look.Prim(PrimitiveType.Capsule, body, color, Vector3.zero, new Vector3(0.62f, 0.62f, 0.62f), false, "torso");
            torso.transform.localRotation = Quaternion.Euler(90, 0, 0);
            Look.Prim(PrimitiveType.Sphere, body, color * 0.9f, new Vector3(0, 0.22f, 0.72f), new Vector3(0.42f, 0.38f, 0.48f), false, "head");
            Look.Prim(PrimitiveType.Cube, body, color * 0.5f, new Vector3(0, 0.18f, 0.98f), new Vector3(0.16f, 0.12f, 0.12f), false, "snout");
            Look.Prim(PrimitiveType.Capsule, body, color * 0.8f, new Vector3(0, 0.15f, -0.72f), new Vector3(0.08f, 0.22f, 0.08f), false, "tail").transform.localRotation = Quaternion.Euler(-50, 0, 0);
            int i = 0;
            foreach (var z in new[] { 0.42f, -0.42f })
            foreach (var x in new[] { -0.2f, 0.2f })
            {
                var leg = Pivot(body, "leg" + (i++), new Vector3(x, -0.1f, z));
                Look.Prim(PrimitiveType.Capsule, leg, color * 0.7f, new Vector3(0, -0.28f, 0), new Vector3(0.14f, 0.28f, 0.14f), false, "leg");
            }
            return body;
        }

        static float MeasureHeight(Renderer[] rs, Transform root, float fallback)
        {
            if (rs == null || rs.Length == 0) return fallback;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return Mathf.Max(0.5f, b.max.y - root.position.y);
        }

        public static Transform FindDeep(Transform t, string preferred)
        {
            if (!string.IsNullOrEmpty(preferred)) { var p = FindName(t, preferred); if (p != null) return p; }
            foreach (var n in HandNames) { var f = FindName(t, n); if (f != null) return f; }
            return null;
        }

        static Transform FindName(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++) { var r = FindName(t.GetChild(i), name); if (r != null) return r; }
            return null;
        }

        static readonly MaterialPropertyBlock Mpb = new MaterialPropertyBlock();
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        static void Tint(Renderer[] rs, Color c)
        {
            foreach (var r in rs)
            {
                r.GetPropertyBlock(Mpb);
                Mpb.SetColor(BaseColorId, c); Mpb.SetColor(Look.ColorId, c);
                r.SetPropertyBlock(Mpb);
            }
        }

        /// <summary>Вспышка попадания: у примитивов — белый цвет, у моделей — короткий «удар» масштабом.</summary>
        public static void ApplyFlash(UnitRig rig, float flash, bool dead)
        {
            if (rig.IsPrefab || rig.Renderers == null) return;
            for (int i = 0; i < rig.Renderers.Length; i++)
            {
                var r = rig.Renderers[i];
                if (r == null) continue;
                var c = rig.BaseColors != null && i < rig.BaseColors.Length ? rig.BaseColors[i] : rig.BaseColor;
                if (flash > 0) Look.SetColor(r, Color.white, 1.5f);
                else Look.SetColor(r, dead ? c * 0.4f : c, rig.Emission);
            }
        }

        // ---------- Оружие в руке ----------
        public static void UpdateWeapon(GameSession s, UnitRig rig, string itemDefId)
        {
            if (rig.Hand == null || rig.WeaponDefId == itemDefId) return;
            if (rig.Weapon != null) Object.Destroy(rig.Weapon);
            rig.WeaponDefId = itemDefId;
            if (itemDefId == null) return;
            var ve = VisualCatalog.Resolve("item", itemDefId);
            if (ve != null)
            {
                rig.Weapon = Object.Instantiate(ve.Prefab, rig.Hand);
                rig.Weapon.transform.localPosition = new Vector3(0, ve.yOffset, 0);
                rig.Weapon.transform.localRotation = Quaternion.Euler(0, ve.rotationY, 0);
                rig.Weapon.transform.localScale *= ve.scale;
                return;
            }
            if (!s.Content.Items.TryGetValue(itemDefId, out var def)) return;
            var col = Look.Hex(def.color, Color.gray);
            var w = new GameObject("weapon");
            w.transform.SetParent(rig.Hand, false);
            switch (def.weaponType)
            {
                case "club": Look.Prim(PrimitiveType.Cylinder, w.transform, col, new Vector3(0, 0.1f, 0.35f), new Vector3(0.12f, 0.4f, 0.12f), false, "weapon").transform.localRotation = Quaternion.Euler(70, 0, 0); break;
                case "spear": Look.Prim(PrimitiveType.Cylinder, w.transform, col, new Vector3(0, 0, 0.6f), new Vector3(0.06f, 0.9f, 0.06f), false, "weapon").transform.localRotation = Quaternion.Euler(85, 0, 0); break;
                case "sword": Look.Prim(PrimitiveType.Cube, w.transform, col, new Vector3(0, 0, 0.5f), new Vector3(0.07f, 0.03f, 0.8f), false, "weapon"); break;
                case "sling": Look.Prim(PrimitiveType.Sphere, w.transform, col, new Vector3(0, -0.2f, 0), new Vector3(0.18f, 0.18f, 0.18f), false, "weapon"); break;
            }
            rig.Weapon = w;
        }

        public static void SetMounted(GameSession s, UnitRig rig, bool mounted)
        {
            if (mounted && rig.Mount == null)
            {
                var ve = VisualCatalog.Resolve("unit", "horse");
                if (ve != null)
                {
                    rig.Mount = Object.Instantiate(ve.Prefab, rig.Root.transform);
                    rig.Mount.transform.localScale *= ve.scale;
                    rig.Mount.transform.localPosition = new Vector3(0, ve.yOffset, 0);
                }
                else
                {
                    rig.Mount = new GameObject("mount");
                    rig.Mount.transform.SetParent(rig.Root.transform, false);
                    var body = Look.Prim(PrimitiveType.Capsule, rig.Mount.transform, new Color(0.5f, 0.32f, 0.18f), new Vector3(0, 0.75f, 0), new Vector3(0.8f, 1.1f, 0.8f));
                    body.transform.localRotation = Quaternion.Euler(90, 0, 0);
                    Look.Prim(PrimitiveType.Sphere, rig.Mount.transform, new Color(0.45f, 0.28f, 0.15f), new Vector3(0, 1.25f, 1.05f), new Vector3(0.45f, 0.5f, 0.6f));
                }
            }
            if (rig.Mount != null) rig.Mount.SetActive(mounted);
        }

        // ---------- Постройки ----------
        public static GameObject CreateBuilding(GameSession s, BuildingDef d, Color nationColor, bool hungry, Transform parent, Vector3 pos, float yaw)
        {
            var bt = new GameObject(d.name);
            bt.transform.SetParent(parent, false);
            bt.transform.position = pos;
            bt.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var ve = VisualCatalog.Resolve("building", d.id);
            if (ve != null)
            {
                var m = Object.Instantiate(ve.Prefab, bt.transform);
                m.transform.localPosition = new Vector3(0, ve.yOffset, 0);
                m.transform.localRotation = Quaternion.Euler(0, ve.rotationY, 0);
                m.transform.localScale *= ve.scale;
                if (ve.tintByNation) Tint(m.GetComponentsInChildren<Renderer>(), nationColor);
                return bt;
            }
            var col = Look.Hex(d.color);
            if (hungry && d.housing > 0) col *= 0.6f; // голод виден: дома тускнеют
            var t = bt.transform;
            switch (d.id)
            {
                case "fire_pit":
                    for (int i = 0; i < 6; i++)
                        Look.Prim(PrimitiveType.Cube, t, new Color(0.45f, 0.42f, 0.4f), new Vector3(Mathf.Cos(i) * 0.8f, 0.1f, Mathf.Sin(i) * 0.8f), new Vector3(0.3f, 0.2f, 0.3f));
                    var fire = Look.Prim(PrimitiveType.Sphere, t, new Color(1f, 0.55f, 0.15f), new Vector3(0, 0.35f, 0), new Vector3(0.7f, 0.6f, 0.7f), false, "flame");
                    Look.SetColor(fire.GetComponent<Renderer>(), new Color(1f, 0.55f, 0.15f), 1.2f);
                    fire.AddComponent<FlameFlicker>();
                    break;
                case "field":
                    Look.Prim(PrimitiveType.Cube, t, col, new Vector3(0, 0.08f, 0), new Vector3(d.size, 0.16f, d.size));
                    for (int i = 0; i < 4; i++) Look.Prim(PrimitiveType.Cube, t, col * 0.8f, new Vector3(-d.size * 0.375f + i * d.size * 0.25f, 0.2f, 0), new Vector3(0.15f, 0.12f, d.size * 0.9f));
                    break;
                case "pen":
                    for (int i = 0; i < 8; i++)
                    {
                        float a = i * Mathf.PI / 4;
                        Look.Prim(PrimitiveType.Cube, t, col, new Vector3(Mathf.Cos(a) * d.size * 0.5f, 0.4f, Mathf.Sin(a) * d.size * 0.5f), new Vector3(0.15f, 0.8f, 0.15f));
                    }
                    Look.Prim(PrimitiveType.Capsule, t, new Color(0.9f, 0.88f, 0.8f), new Vector3(0.3f, 0.45f, 0), new Vector3(0.45f, 0.4f, 0.45f)).transform.localRotation = Quaternion.Euler(90, 30, 0);
                    break;
                case "eternal_spire":
                case "mirror_of_sky":
                    Look.Prim(PrimitiveType.Cylinder, t, col * 0.8f, new Vector3(0, 0.5f, 0), new Vector3(d.size, 0.5f, d.size));
                    var top = Look.Prim(PrimitiveType.Cylinder, t, col, new Vector3(0, d.height * 0.5f, 0), new Vector3(d.size * 0.35f, d.height * 0.5f, d.size * 0.35f));
                    Look.SetColor(top.GetComponent<Renderer>(), col, 0.6f);
                    break;
                case "hut":
                    var cone = Look.Prim(PrimitiveType.Cube, t, col, new Vector3(0, d.height * 0.45f, 0), new Vector3(d.size * 0.65f, d.size * 0.65f, d.size * 0.65f));
                    cone.transform.localRotation = Quaternion.Euler(45, 0, 45);
                    Look.Prim(PrimitiveType.Cube, t, col * 0.5f, new Vector3(0, 0.4f, d.size * 0.42f), new Vector3(0.6f, 0.8f, 0.1f));
                    break;
                default:
                    Look.Prim(PrimitiveType.Cube, t, col, new Vector3(0, d.height * 0.4f, 0), new Vector3(d.size, d.height * 0.8f, d.size));
                    var roof = Look.Prim(PrimitiveType.Cube, t, Color.Lerp(col, nationColor, 0.5f) * 0.85f, new Vector3(0, d.height * 0.85f, 0), Vector3.one * d.size * 0.6f);
                    roof.transform.localRotation = Quaternion.Euler(45, 0, 45);
                    Look.Prim(PrimitiveType.Cube, t, col * 0.45f, new Vector3(0, 0.45f, d.size * 0.5f), new Vector3(0.6f, 0.9f, 0.06f), false, "door");
                    if (d.id == "palace" || d.id == "chief_house")
                        Look.Prim(PrimitiveType.Cube, t, nationColor, new Vector3(d.size * 0.3f, d.height + 0.8f, 0), new Vector3(0.12f, 1.6f, 0.12f));
                    if (d.id == "palace")
                        for (int i = 0; i < 4; i++)
                            Look.Prim(PrimitiveType.Cylinder, t, col * 0.9f, new Vector3((i % 2 - 0.5f) * d.size, d.height * 0.7f, (i / 2 - 0.5f) * d.size), new Vector3(0.7f, d.height * 0.7f, 0.7f));
                    break;
            }
            return bt;
        }

        public static GameObject CreateWallSegment(Transform parent, bool stone, Vector3 pos, Quaternion rot, float length)
        {
            var ve = VisualCatalog.Resolve("building", stone ? "wall_stone" : "wall_wood");
            if (ve != null)
            {
                var m = Object.Instantiate(ve.Prefab, parent);
                m.transform.position = pos + Vector3.up * ve.yOffset; m.transform.rotation = rot;
                m.transform.localScale *= ve.scale;
                return m;
            }
            var w = Look.Prim(PrimitiveType.Cube, parent, stone ? new Color(0.62f, 0.62f, 0.6f) : new Color(0.45f, 0.32f, 0.2f), Vector3.zero, new Vector3(length, 2f, 0.6f));
            w.transform.position = pos + Vector3.up; w.transform.rotation = rot;
            return w;
        }

        // ---------- Точки интереса ----------
        public static GameObject CreatePoi(PoiDef def, Transform parent, Vector3 pos)
        {
            var go = new GameObject("POI " + def.name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var ve = VisualCatalog.Resolve("poi", def.id, def.kind);
            if (ve != null)
            {
                var m = Object.Instantiate(ve.Prefab, go.transform);
                m.transform.localPosition = new Vector3(0, ve.yOffset, 0);
                m.transform.localScale *= ve.scale;
                return go;
            }
            var col = Look.Hex(def.color);
            var t = go.transform;
            switch (def.kind)
            {
                case "ruins":
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 1.25f, h = 0.8f + (i % 3) * 0.9f;
                        Look.Prim(PrimitiveType.Cube, t, col, new Vector3(Mathf.Cos(a) * 2.2f, h * 0.5f, Mathf.Sin(a) * 2.2f), new Vector3(0.7f, h, 0.7f));
                    }
                    Look.Prim(PrimitiveType.Cube, t, col * 0.8f, new Vector3(0, 0.1f, 0), new Vector3(3.5f, 0.2f, 3.5f));
                    break;
                case "cache":
                    Look.Prim(PrimitiveType.Cube, t, col, new Vector3(0, 0.35f, 0), new Vector3(0.9f, 0.6f, 0.6f));
                    Look.Prim(PrimitiveType.Cube, t, col * 0.6f, new Vector3(0, 0.67f, 0), new Vector3(0.95f, 0.08f, 0.65f));
                    break;
                case "lair":
                    Look.Prim(PrimitiveType.Sphere, t, col, new Vector3(0, 0.2f, 0), new Vector3(4.5f, 1.6f, 4f));
                    Look.Prim(PrimitiveType.Sphere, t, Color.black, new Vector3(0, 0.4f, -1.8f), new Vector3(1.4f, 1f, 0.6f));
                    for (int i = 0; i < 4; i++) Look.Prim(PrimitiveType.Capsule, t, new Color(0.9f, 0.88f, 0.8f), new Vector3(Mathf.Cos(i * 1.7f) * 2.8f, 0.08f, Mathf.Sin(i * 1.7f) * 2.8f), new Vector3(0.08f, 0.3f, 0.08f)).transform.localRotation = Quaternion.Euler(90, i * 40, 0);
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
                    var f = Look.Prim(PrimitiveType.Sphere, t, new Color(1f, 0.5f, 0.2f), new Vector3(0, 0.25f, 0), new Vector3(0.7f, 0.5f, 0.7f), false, "flame");
                    Look.SetColor(f.GetComponent<Renderer>(), new Color(1f, 0.5f, 0.2f), 1.2f);
                    f.AddComponent<FlameFlicker>();
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

        // ---------- Ресурсные узлы (инстансинг) ----------
        static readonly Dictionary<string, NodeVisual> Nodes = new Dictionary<string, NodeVisual>();

        public static NodeVisual Node(NodeDef node)
        {
            if (Nodes.TryGetValue(node.id, out var v)) return v;
            v = new NodeVisual();
            var ve = VisualCatalog.Resolve("node", node.id, node.shape);
            if (ve != null)
            {
                var rootInv = ve.Prefab.transform.worldToLocalMatrix;
                foreach (var mf in ve.Prefab.GetComponentsInChildren<MeshFilter>())
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mr == null || mf.sharedMesh == null || mr.sharedMaterial == null) continue;
                    var mat = new Material(mr.sharedMaterial) { enableInstancing = true };
                    v.Parts.Add(new NodePart
                    {
                        Mesh = mf.sharedMesh, Material = mat,
                        Local = Matrix4x4.TRS(Vector3.up * ve.yOffset, Quaternion.identity, Vector3.one * ve.scale) * rootInv * mf.transform.localToWorldMatrix
                    });
                }
            }
            if (v.Parts.Count == 0)
            {
                var col = Look.Hex(node.color);
                switch (node.shape)
                {
                    case "tree":
                        v.Parts.Add(Part(PrimitiveType.Cylinder, new Vector3(0, 0.9f, 0), new Vector3(0.35f, 0.9f, 0.35f), new Color(0.42f, 0.3f, 0.2f)));
                        v.Parts.Add(Part(PrimitiveType.Sphere, new Vector3(0, 2.3f, 0), new Vector3(1.7f, 1.9f, 1.7f), null));
                        v.Parts.Add(Part(PrimitiveType.Sphere, new Vector3(0.3f, 3.1f, 0.1f), new Vector3(1.1f, 1.2f, 1.1f), null));
                        break;
                    case "rock":
                        v.Parts.Add(Part(PrimitiveType.Cube, new Vector3(0, 0.3f, 0), new Vector3(1.1f, 0.7f, 0.9f), null, Quaternion.Euler(8, 0, 12)));
                        v.Parts.Add(Part(PrimitiveType.Cube, new Vector3(0.35f, 0.2f, 0.25f), new Vector3(0.5f, 0.45f, 0.5f), null, Quaternion.Euler(0, 30, 20)));
                        break;
                    default:
                        v.Parts.Add(Part(PrimitiveType.Sphere, new Vector3(0, 0.35f, 0), new Vector3(0.9f, 0.6f, 0.9f), null));
                        v.Parts.Add(Part(PrimitiveType.Sphere, new Vector3(0.15f, 0.55f, 0.1f), new Vector3(0.2f, 0.2f, 0.2f), col * 1.3f));
                        break;
                }
            }
            Nodes[node.id] = v;
            return v;
        }

        static NodePart Part(PrimitiveType t, Vector3 pos, Vector3 scale, Color? fixedColor, Quaternion? rot = null) => new NodePart
        {
            Mesh = Look.Mesh(t), Material = Look.LitInstanced, Local = Matrix4x4.TRS(pos, rot ?? Quaternion.identity, scale),
            FixedColor = fixedColor, UsesInstanceColor = true
        };

        // ---------- Снаряды, добыча, эффекты, дым ----------
        public static GameObject CreateProjectile(GameSession s, Projectile p, Transform parent)
        {
            var ve = VisualCatalog.Resolve("projectile", p.AbilityId, p.Mystic ? "mystic" : "default");
            if (ve != null) { var m = Object.Instantiate(ve.Prefab, parent); m.transform.localScale *= ve.scale; return m; }
            Color c = Color.white;
            if (p.AbilityId != null && s.Content.Abilities.TryGetValue(p.AbilityId, out var a)) c = Look.Hex(a.color, Color.white);
            else if (p.Mystic) c = new Color(0.75f, 0.5f, 1f);
            var go = Look.Prim(PrimitiveType.Sphere, parent, c, Vector3.zero, Vector3.one * (p.Aoe > 0 ? 0.7f : 0.35f));
            Look.SetColor(go.GetComponent<Renderer>(), c, 0.8f);
            return go;
        }

        public static GameObject CreateLoot(GameSession s, LootDrop l, Transform parent)
        {
            string item = l.Item != null ? l.Item.DefId : l.Resource;
            var ve = VisualCatalog.Resolve("loot", item, l.Item != null ? "equipment" : "resource");
            if (ve != null) { var m = Object.Instantiate(ve.Prefab, parent); m.transform.localScale *= ve.scale; return m; }
            var c = s.Content.Items.TryGetValue(item, out var d) ? Look.Hex(d.color) : Color.white;
            var go = Look.Prim(l.Item != null ? PrimitiveType.Cube : PrimitiveType.Sphere, parent, c, Vector3.zero, Vector3.one * (l.Item != null ? 0.45f : 0.35f));
            Look.SetColor(go.GetComponent<Renderer>(), c, item == "essence" ? 1.2f : 0.3f);
            return go;
        }

        /// <summary>Эффект-префаб (Resources/Fx/&lt;key&gt;). Возвращает false, если префаба нет — тогда рисуется примитивная вспышка.</summary>
        public static bool TrySpawnFx(Transform parent, string key, Vector3 pos, Vector3 dir, float scale)
        {
            var ve = VisualCatalog.Resolve("fx", key);
            if (ve == null) return false;
            var go = Object.Instantiate(ve.Prefab, pos + Vector3.up * ve.yOffset, dir.sqrMagnitude > 0.01f ? Quaternion.LookRotation(dir) : Quaternion.identity, parent);
            go.transform.localScale *= ve.scale * Mathf.Max(0.2f, scale);
            float life = ve.life;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>()) life = Mathf.Max(life, ps.main.duration + ps.main.startLifetime.constantMax);
            Object.Destroy(go, life);
            return true;
        }

        public static GameObject CreateZone(Transform parent, out bool fallback)
        {
            var ve = VisualCatalog.Resolve("zone", "smoke_zone");
            fallback = ve == null;
            var go = new GameObject("Smoke");
            go.transform.SetParent(parent, false);
            if (ve != null) { var m = Object.Instantiate(ve.Prefab, go.transform); m.name = "model"; return go; }
            var outer = Look.Prim(PrimitiveType.Cylinder, go.transform, new Color(0.42f, 0.33f, 0.55f, 0.55f), Vector3.zero, Vector3.one, true, "outer");
            Look.Prim(PrimitiveType.Cylinder, go.transform, new Color(0.3f, 0.25f, 0.4f, 0.35f), Vector3.zero, Vector3.one, true, "inner");
            for (int i = 0; i < 10; i++) Look.Prim(PrimitiveType.Sphere, go.transform, new Color(0.5f, 0.42f, 0.62f, 0.35f), Vector3.zero, Vector3.one, true, "puff" + i);
            var r = outer.GetComponent<Renderer>(); var mpb = new MaterialPropertyBlock(); r.GetPropertyBlock(mpb); mpb.SetFloat(Look.PulseId, 1f); r.SetPropertyBlock(mpb);
            return go;
        }
    }

    /// <summary>Мерцание огня у примитивных костров.</summary>
    public sealed class FlameFlicker : MonoBehaviour
    {
        Vector3 _base; float _seed;
        void Start() { _base = transform.localScale; _seed = Random.value * 10f; }
        void Update()
        {
            float k = 1f + Mathf.Sin(Time.time * 9f + _seed) * 0.08f + Mathf.Sin(Time.time * 23f + _seed) * 0.05f;
            transform.localScale = new Vector3(_base.x * (2f - k), _base.y * k * 1.1f, _base.z * (2f - k));
        }
    }
}
