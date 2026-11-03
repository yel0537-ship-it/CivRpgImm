using System.Collections.Generic;
using Immortal.Core;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Immortal.Presentation.UI
{
    /// <summary>HUD мира: полоски героя, время и эпоха, умения, журнал, подписи над объектами, числа урона.</summary>
    public sealed class Hud
    {
        public readonly VisualElement Root;
        readonly VisualElement _labels;
        readonly Label _date, _role, _essence, _influence, _vitalityText, _hpText, _energyText, _notice, _hover, _hint, _party;
        VisualElement _hpFill, _energyFill, _vitFill;
        readonly Label[] _slotName = new Label[5], _slotCd = new Label[5];
        readonly VisualElement[] _slot = new VisualElement[5];
        readonly Image[] _slotIcon = new Image[5];
        readonly string[] _slotAbility = new string[5];
        readonly VisualElement _log;
        readonly List<(Label l, float t)> _messages = new List<(Label, float)>();
        float _noticeTimer;
        readonly Label _objTitle, _objText;
        readonly VisualElement _objBox;
        readonly Image _mini;
        Texture2D _miniTex;
        Color32[] _miniPx;
        float _miniTimer;
        const int MiniTiles = 64, MiniPx = 128;

        sealed class FloatText { public Label L; public Vector3 World; public float T; }
        readonly List<FloatText> _floating = new List<FloatText>();
        readonly Stack<Label> _floatPool = new Stack<Label>();
        readonly Dictionary<string, Label> _nameLabels = new Dictionary<string, Label>();
        readonly HashSet<string> _usedLabels = new HashSet<string>();

        public Hud()
        {
            Root = new VisualElement();
            Ui.Absolute(Root, 0, 0, 0, 0);
            Root.pickingMode = PickingMode.Ignore;

            _labels = new VisualElement { pickingMode = PickingMode.Ignore };
            Ui.Absolute(_labels, 0, 0, 0, 0);
            Root.Add(_labels);

            var stats = Ui.Box(new Color(0.05f, 0.06f, 0.08f, 0.8f), 10);
            Ui.Absolute(stats, 16, 16);
            stats.style.width = 300;
            stats.pickingMode = PickingMode.Ignore;
            _date = Ui.Label("", 18, Ui.Gold, true);
            _role = Ui.Label("", 15, Ui.Dim);
            stats.Add(_date); stats.Add(_role);
            stats.Add(BarRow("Здоровье", new Color(0.8f, 0.2f, 0.2f), out _hpFill, out _hpText));
            stats.Add(BarRow("Энергия", new Color(0.25f, 0.55f, 0.95f), out _energyFill, out _energyText));
            stats.Add(BarRow("Жизненная сила", new Color(0.55f, 0.85f, 0.4f), out _vitFill, out _vitalityText));
            _essence = Ui.Label("", 15, Ui.Mystic);
            _influence = Ui.Label("", 15, Ui.Gold);
            stats.Add(_essence); stats.Add(_influence);
            Root.Add(stats);

            // Миникарта и текущая цель — справа сверху.
            var right = new VisualElement { pickingMode = PickingMode.Ignore };
            Ui.Absolute(right, right: 16, top: 16);
            right.style.width = 340;
            right.style.alignItems = Align.FlexEnd;
            var miniFrame = Ui.Box(new Color(0.05f, 0.06f, 0.08f, 0.8f), 4);
            miniFrame.pickingMode = PickingMode.Ignore;
            _mini = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            _mini.style.width = 200; _mini.style.height = 200;
            miniFrame.Add(_mini);
            right.Add(miniFrame);
            _objBox = Ui.Box(new Color(0.05f, 0.06f, 0.08f, 0.8f), 8);
            _objBox.pickingMode = PickingMode.Ignore;
            _objBox.style.marginTop = 8; _objBox.style.width = 340;
            _objTitle = Ui.Label("", 16, Ui.Gold, true); _objTitle.pickingMode = PickingMode.Ignore;
            _objText = Ui.Label("", 14); _objText.pickingMode = PickingMode.Ignore;
            _objBox.Add(_objTitle); _objBox.Add(_objText);
            right.Add(_objBox);
            Root.Add(right);

            _party = Ui.Label("", 14, Ui.Dim);
            Ui.Absolute(_party, 18, 260);
            _party.pickingMode = PickingMode.Ignore;
            Root.Add(_party);

            var bar = Ui.Row();
            Ui.Absolute(bar, bottom: 18);
            bar.style.left = new Length(50, LengthUnit.Percent);
            bar.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            bar.style.flexWrap = Wrap.NoWrap;
            bar.pickingMode = PickingMode.Ignore;
            string[] keys = { "1", "2", "3", "4", "Пробел" };
            for (int i = 0; i < 5; i++)
            {
                var s = Ui.Box(new Color(0.08f, 0.08f, 0.1f, 0.85f), 6);
                s.style.width = 130; s.style.height = 64; s.style.marginRight = 6;
                s.pickingMode = PickingMode.Ignore;
                var head = Ui.Row(); head.pickingMode = PickingMode.Ignore; head.style.flexWrap = Wrap.NoWrap;
                _slotIcon[i] = UiAssets.ImageOf(null, 26); Ui.Show(_slotIcon[i], false);
                head.Add(_slotIcon[i]);
                head.Add(Ui.Label(keys[i], 13, Ui.Gold, true));
                s.Add(head);
                _slotName[i] = Ui.Label("", 13); _slotCd[i] = Ui.Label("", 13, Ui.Dim);
                s.Add(_slotName[i]); s.Add(_slotCd[i]);
                _slot[i] = s;
                bar.Add(s);
            }
            Root.Add(bar);

            _log = new VisualElement { pickingMode = PickingMode.Ignore };
            Ui.Absolute(_log, right: 16, bottom: 110);
            _log.style.width = 440;
            _log.style.alignItems = Align.FlexEnd;
            Root.Add(_log);

            _notice = Ui.Label("", 26, Ui.Gold, true);
            Ui.Absolute(_notice, top: 70);
            _notice.style.left = new Length(50, LengthUnit.Percent);
            _notice.style.translate = new Translate(new Length(-50, LengthUnit.Percent), 0);
            _notice.style.unityTextAlign = TextAnchor.MiddleCenter;
            _notice.pickingMode = PickingMode.Ignore;
            Root.Add(_notice);

            _hover = Ui.Label("", 16, Color.white, true);
            _hover.style.position = Position.Absolute;
            _hover.style.backgroundColor = new Color(0, 0, 0, 0.6f);
            Ui.Pad(_hover, 4);
            _hover.pickingMode = PickingMode.Ignore;
            Root.Add(_hover);

            _hint = Ui.Label("ЛКМ — идти/бить/говорить · 1–4 умения · Пробел рывок · I снаряжение · C ремесло · M карта · J журнал · Tab правитель · F лагерь · R конь · T ускорить время · Esc меню", 13, Ui.Dim);
            Ui.Absolute(_hint, 16, bottom: 4);
            _hint.pickingMode = PickingMode.Ignore;
            Root.Add(_hint);
        }

        static VisualElement BarRow(string name, Color c, out VisualElement fill, out Label text)
        {
            var col = new VisualElement { pickingMode = PickingMode.Ignore };
            col.style.marginTop = 4;
            var row = Ui.Row(); row.pickingMode = PickingMode.Ignore;
            row.Add(Ui.Label(name, 13, Ui.Dim));
            text = Ui.Label("", 13); text.style.marginLeft = 8;
            row.Add(text);
            col.Add(row);
            col.Add(Ui.Bar(c, out fill, 276, 12));
            return col;
        }

        public void Message(string text, Color? color = null)
        {
            var l = Ui.Label(text, 15, color ?? Ui.Text);
            l.style.backgroundColor = new Color(0, 0, 0, 0.45f);
            Ui.Pad(l, 3);
            l.pickingMode = PickingMode.Ignore;
            _log.Add(l);
            _messages.Add((l, 7f));
            while (_messages.Count > 7) { _log.Remove(_messages[0].l); _messages.RemoveAt(0); }
        }

        public void Notice(string text, float seconds = 4f) { _notice.text = text; _noticeTimer = seconds; }

        public void Floating(Vector3 world, string text, Color color, float size = 18)
        {
            var l = _floatPool.Count > 0 ? _floatPool.Pop() : new Label();
            l.text = text; l.style.color = color; l.style.fontSize = size; l.style.position = Position.Absolute;
            l.style.unityFontStyleAndWeight = FontStyle.Bold;
            l.pickingMode = PickingMode.Ignore;
            Ui.Show(l, true);
            if (l.parent == null) _labels.Add(l);
            _floating.Add(new FloatText { L = l, World = world, T = 0 });
        }

        public void SetHover(string text, Vector2 panelPos)
        {
            _hover.text = text ?? "";
            Ui.Show(_hover, !string.IsNullOrEmpty(text));
            _hover.style.left = panelPos.x + 18; _hover.style.top = panelPos.y + 12;
        }

        /// <summary>Подпись над объектом мира (город, NPC, точка интереса). Вызывать каждый кадр между Begin/EndLabels.</summary>
        public void WorldLabel(IPanel panel, Camera cam, string key, Vector3 world, string text, Color color, float size = 15)
        {
            if (panel == null) return;
            _usedLabels.Add(key);
            if (!_nameLabels.TryGetValue(key, out var l))
            {
                l = Ui.Label(text, size, color, true);
                l.style.position = Position.Absolute;
                l.style.unityTextAlign = TextAnchor.MiddleCenter;
                l.style.backgroundColor = new Color(0, 0, 0, 0.35f);
                Ui.Pad(l, 2);
                l.pickingMode = PickingMode.Ignore;
                _labels.Add(l);
                _nameLabels[key] = l;
            }
            l.text = text; l.style.color = color;
            var vp = cam.WorldToViewportPoint(world);
            bool on = vp.z > 0 && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f;
            Ui.Show(l, on);
            if (!on) return;
            var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, cam);
            l.style.left = p.x - 60; l.style.top = p.y - 12;
            l.style.width = 120;
        }

        public void BeginLabels() => _usedLabels.Clear();

        public void EndLabels()
        {
            var drop = new List<string>();
            foreach (var kv in _nameLabels) if (!_usedLabels.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var k in drop) { _nameLabels[k].RemoveFromHierarchy(); _nameLabels.Remove(k); }
        }

        public void Update(GameSession s, IPanel panel, Camera cam, float dt)
        {
            if (panel == null) return;
            var h = s.State.Hero; var u = s.HeroUnit;
            _date.text = $"{s.DateText} · {s.PlayerEraDef.name}" + (GameRoot.Instance != null && GameRoot.Instance.FastForward ? "  ⏩×5" : "");
            _role.text = $"{HeroSystem.RoleName(h.Role)} · вид на {HeroSystem.ApparentAge(h):0} лет · глава {s.State.Chapter}" + (h.Mounted ? " · верхом" : "") + (h.AttributePoints > 0 ? $" · очков атрибутов: {h.AttributePoints} (I)" : "");
            if (u != null)
            {
                SetFill(_hpFill, u.Hp / Mathf.Max(1, u.MaxHp), 276); _hpText.text = $"{u.Hp:0}/{u.MaxHp:0}";
            }
            float maxE = HeroSystem.MaxEnergy(s);
            SetFill(_energyFill, h.Energy / maxE, 276); _energyText.text = $"{h.Energy:0}/{maxE:0}";
            SetFill(_vitFill, (float)(h.VitalityYears / h.MaxVitalityYears), 276);
            _vitalityText.text = s.State.Narrative.Flags.Contains("true_immortality") ? "∞" : $"{h.VitalityYears:0.0} лет";
            int ess = HeroSystem.EssenceAvailable(s);
            _essence.text = ess > 0 || h.RejuvenationUnlocked ? $"Мистический ресурс: {ess}" + (h.RejuvenationUnlocked ? " (омоложение — I)" : "") : "";
            var pn = s.PlayerNation;
            _influence.text = pn != null && Roles.Rank(h.Role) >= 2 ? $"Влияние правителя: {pn.Influence:0}" : pn != null ? $"Община: {CitySystem.NationPopulation(s, pn):0} чел." : "Нет общины (F — основать лагерь)";

            var obj = Immortal.Core.Narrative.ObjectiveSystem.Current(s);
            Ui.Show(_objBox, obj != null);
            if (obj != null) { _objTitle.text = "Цель: " + obj.title; _objText.text = obj.text; }
            _miniTimer -= dt;
            if (_miniTimer <= 0) { _miniTimer = 0.4f; DrawMinimap(s); }

            var party = new System.Text.StringBuilder();
            foreach (var id in h.PartyCompanionUnits)
            {
                var cu = s.FindUnit(id); if (cu == null) continue;
                party.AppendLine($"{cu.Name}: {cu.Hp:0}/{cu.MaxHp:0}");
            }
            _party.text = party.ToString();

            for (int i = 0; i < 5; i++)
            {
                Immortal.Core.Content.AbilityDef a = i < 4 ? HeroSystem.AbilityInSlot(s, i + 1) : null;
                string name = i == 4 ? "Рывок" : a?.name ?? (i == 3 ? "Сила эпохи —" : "—");
                string key = i == 4 ? "dash" : a?.id;
                float cd = key != null && h.Cooldowns.TryGetValue(key, out var c) ? c : 0;
                bool locked = i == 3 && (a == null || !h.InSmoke);
                if (_slotAbility[i] != key)
                {
                    _slotAbility[i] = key;
                    var tex = UiAssets.Icon("Abilities", key);
                    _slotIcon[i].image = tex;
                    Ui.Show(_slotIcon[i], tex != null);
                }
                _slotName[i].text = name;
                _slotName[i].style.color = locked ? Ui.Dim : (i == 3 ? Ui.Mystic : Ui.Text);
                _slotCd[i].text = cd > 0 ? $"{cd:0.0} с" : locked ? (a == null ? "нет в эту эпоху" : "только в дыму") : a != null ? $"{a.energy:0} эн." : "";
            }

            for (int i = _messages.Count - 1; i >= 0; i--)
            {
                var (l, t) = _messages[i];
                t -= dt;
                l.style.opacity = Mathf.Clamp01(t / 1.5f);
                if (t <= 0) { l.RemoveFromHierarchy(); _messages.RemoveAt(i); }
                else _messages[i] = (l, t);
            }
            if (_noticeTimer > 0) { _noticeTimer -= dt; _notice.style.opacity = Mathf.Clamp01(_noticeTimer); }
            else _notice.text = "";

            for (int i = _floating.Count - 1; i >= 0; i--)
            {
                var f = _floating[i];
                f.T += dt;
                var p = RuntimePanelUtils.CameraTransformWorldToPanel(panel, f.World + Vector3.up * f.T * 1.5f, cam);
                f.L.style.left = p.x - 20; f.L.style.top = p.y;
                f.L.style.opacity = 1f - f.T / 0.9f;
                if (f.T > 0.9f) { Ui.Show(f.L, false); _floatPool.Push(f.L); _floating.RemoveAt(i); }
            }
        }

        void DrawMinimap(GameSession s)
        {
            var hero = s.HeroUnit; if (hero == null) return;
            if (_miniTex == null)
            {
                _miniTex = new Texture2D(MiniPx, MiniPx, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                _miniPx = new Color32[MiniPx * MiniPx];
                _mini.image = _miniTex;
            }
            var map = s.Map;
            map.TileXY(hero.Pos, out int hx, out int hy);
            var own = s.State.TerritoryOwner; int cell = s.Balance.territoryCell; int nx = s.State.TerritoryCellsX;
            int scale = MiniPx / MiniTiles;
            var dark = new Color32(10, 11, 14, 255);
            for (int ty = 0; ty < MiniTiles; ty++)
            for (int tx = 0; tx < MiniTiles; tx++)
            {
                int wx = hx - MiniTiles / 2 + tx, wy = hy - MiniTiles / 2 + ty;
                Color32 c = dark;
                if (map.InBounds(wx, wy))
                {
                    int idx = wy * map.Size + wx;
                    if (Immortal.Core.World.Vision.IsExplored(s.State, idx))
                    {
                        Color col = Look.Hex(map.TerrainAt(idx).color);
                        if (own != null) { int o = own[(wy / cell) * nx + (wx / cell)]; if (o > 0) col = Color.Lerp(col, Look.Hex(s.State.Nations[o - 1].Color), 0.3f); }
                        c = col;
                    }
                }
                for (int py = 0; py < scale; py++) for (int px = 0; px < scale; px++) _miniPx[(ty * scale + py) * MiniPx + tx * scale + px] = c;
            }
            void Dot(Vec2 p, Color32 col, int r)
            {
                map.TileXY(p, out int tx, out int ty);
                int cx = (tx - hx + MiniTiles / 2) * scale, cy = (ty - hy + MiniTiles / 2) * scale;
                for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
                {
                    int x = cx + dx, y = cy + dy;
                    if (x >= 0 && y >= 0 && x < MiniPx && y < MiniPx) _miniPx[y * MiniPx + x] = col;
                }
            }
            foreach (var p in s.State.Pois) if (p.Discovered) Dot(p.Pos, Look.Hex(s.Content.Poi[p.DefId].color), 1);
            foreach (var c in s.State.Cities) Dot(c.Pos, new Color32(255, 255, 255, 255), 2);
            float vis = s.State.Hero.InSmoke ? s.Balance.vision.smokeRadius : s.Balance.vision.radius;
            foreach (var u in s.State.Units)
            {
                if (u.Ai == AiState.Dead || Vec2.Distance(u.Pos, hero.Pos) > vis) continue;
                if (u.Kind == UnitKind.Companion) Dot(u.Pos, new Color32(110, 190, 255, 255), 1);
                else if (s.IsHostile(Teams.Player, u.Team)) Dot(u.Pos, new Color32(230, 60, 50, 255), 1);
                else if (u.Kind == UnitKind.Npc) Dot(u.Pos, new Color32(235, 230, 210, 255), 1);
            }
            Dot(hero.Pos, new Color32(255, 215, 60, 255), 2);
            _miniTex.SetPixels32(_miniPx);
            _miniTex.Apply(false);
        }

        static void SetFill(VisualElement fill, float frac, float width) => fill.style.width = Mathf.Clamp01(frac) * width;
    }
}
