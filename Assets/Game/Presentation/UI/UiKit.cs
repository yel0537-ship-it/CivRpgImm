using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Immortal.Presentation.UI
{
    /// <summary>Сборка интерфейса UI Toolkit из C#: панели, подписи, кнопки. Кириллица — через системный шрифт.</summary>
    public static class Ui
    {
        public static readonly Color Bg = new Color(0.07f, 0.08f, 0.1f, 0.92f);
        public static readonly Color BgLight = new Color(0.14f, 0.15f, 0.18f, 0.95f);
        public static readonly Color Border = new Color(0.55f, 0.45f, 0.28f, 1f);
        public static readonly Color Gold = new Color(0.95f, 0.8f, 0.4f);
        public static readonly Color Text = new Color(0.92f, 0.9f, 0.85f);
        public static readonly Color Dim = new Color(0.6f, 0.6f, 0.62f);
        public static readonly Color Good = new Color(0.45f, 0.85f, 0.45f);
        public static readonly Color Bad = new Color(0.95f, 0.4f, 0.35f);
        public static readonly Color Mystic = new Color(0.75f, 0.6f, 1f);

        static Font _font;

        public static Font CyrillicFont()
        {
            if (_font != null) return _font;
            string[] names = { "Arial", "Segoe UI", "Roboto", "Noto Sans", "DejaVu Sans", "Liberation Sans", "Helvetica", "Verdana" };
            try { _font = Font.CreateDynamicFontFromOSFont(names, 18); } catch { _font = null; }
            if (_font == null)
            {
                try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
            }
            return _font;
        }

        public static PanelSettings CreatePanelSettings()
        {
            var ps = Resources.Load<PanelSettings>("UI/GamePanelSettings");
            if (ps == null)
            {
                ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.name = "Runtime Panel Settings";
            }
            ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            ps.referenceResolution = new Vector2Int(1920, 1080);
            ps.match = 0.5f;
            return ps;
        }

        public static void ApplyFont(VisualElement root)
        {
            var f = CyrillicFont();
            if (f != null) root.style.unityFontDefinition = new StyleFontDefinition(FontDefinition.FromFont(f));
            root.style.color = Text;
            root.style.fontSize = 17;
        }

        public static VisualElement Box(Color? bg = null, float pad = 12, bool border = true)
        {
            var e = new VisualElement();
            e.style.backgroundColor = bg ?? Bg;
            Pad(e, pad);
            if (border)
            {
                e.style.borderTopWidth = e.style.borderBottomWidth = e.style.borderLeftWidth = e.style.borderRightWidth = 1;
                e.style.borderTopColor = e.style.borderBottomColor = e.style.borderLeftColor = e.style.borderRightColor = Border;
            }
            Radius(e, 6);
            return e;
        }

        public static void Pad(VisualElement e, float p)
        {
            e.style.paddingTop = e.style.paddingBottom = e.style.paddingLeft = e.style.paddingRight = p;
        }

        public static void Radius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
        }

        public static Label Label(string text, float size = 17, Color? color = null, bool bold = false)
        {
            var l = new Label(text);
            l.style.fontSize = size;
            l.style.color = color ?? Text;
            l.style.whiteSpace = WhiteSpace.Normal;
            l.style.marginBottom = 2;
            if (bold) l.style.unityFontStyleAndWeight = FontStyle.Bold;
            return l;
        }

        public static Button Button(string text, Action onClick, bool enabled = true, float size = 16)
        {
            var b = new Button(() => { if (enabled) onClick?.Invoke(); }) { text = text };
            b.style.fontSize = size;
            b.style.color = enabled ? Text : Dim;
            b.style.backgroundColor = enabled ? new Color(0.22f, 0.2f, 0.17f) : new Color(0.15f, 0.15f, 0.15f);
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 1;
            var bc = enabled ? Border : new Color(0.3f, 0.3f, 0.3f);
            b.style.borderTopColor = b.style.borderBottomColor = b.style.borderLeftColor = b.style.borderRightColor = bc;
            Radius(b, 4);
            b.style.paddingTop = b.style.paddingBottom = 5; b.style.paddingLeft = b.style.paddingRight = 10;
            b.style.marginTop = b.style.marginBottom = 2; b.style.marginRight = 4;
            b.style.unityTextAlign = TextAnchor.MiddleLeft;
            b.style.whiteSpace = WhiteSpace.Normal;
            if (enabled)
            {
                b.RegisterCallback<PointerEnterEvent>(_ => b.style.backgroundColor = new Color(0.36f, 0.3f, 0.2f));
                b.RegisterCallback<PointerLeaveEvent>(_ => b.style.backgroundColor = new Color(0.22f, 0.2f, 0.17f));
            }
            return b;
        }

        public static VisualElement Row(float gap = 0)
        {
            var r = new VisualElement();
            r.style.flexDirection = FlexDirection.Row;
            r.style.flexWrap = Wrap.Wrap;
            r.style.alignItems = Align.Center;
            if (gap > 0) r.style.marginBottom = gap;
            return r;
        }

        public static VisualElement Bar(Color fill, out VisualElement fillEl, float width = 260, float height = 16)
        {
            var back = new VisualElement();
            back.style.width = width; back.style.height = height;
            back.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.9f);
            Radius(back, 3);
            fillEl = new VisualElement();
            fillEl.style.height = height; fillEl.style.width = width;
            fillEl.style.backgroundColor = fill;
            Radius(fillEl, 3);
            back.Add(fillEl);
            return back;
        }

        public static ScrollView Scroll(float maxHeight)
        {
            var s = new ScrollView(ScrollViewMode.Vertical);
            s.style.maxHeight = maxHeight;
            s.style.flexGrow = 1;
            return s;
        }

        public static void Absolute(VisualElement e, float? left = null, float? top = null, float? right = null, float? bottom = null)
        {
            e.style.position = Position.Absolute;
            if (left.HasValue) e.style.left = left.Value;
            if (top.HasValue) e.style.top = top.Value;
            if (right.HasValue) e.style.right = right.Value;
            if (bottom.HasValue) e.style.bottom = bottom.Value;
        }

        public static void Show(VisualElement e, bool show) => e.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;

        public static VisualElement FullScreen(Color bg)
        {
            var e = new VisualElement();
            Absolute(e, 0, 0, 0, 0);
            e.style.backgroundColor = bg;
            e.style.alignItems = Align.Center;
            e.style.justifyContent = Justify.Center;
            return e;
        }
    }
}
