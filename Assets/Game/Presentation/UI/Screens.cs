using System;
using System.Collections.Generic;
using System.Linq;
using Immortal.Core;
using Immortal.Core.Action;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using Immortal.Core.Commands;
using Immortal.Core.Narrative;
using Immortal.Core.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Immortal.Presentation.UI
{
    public enum Screen { Menu, World, Map, Ruler, Dialogue, Chronicle, End, Inventory, Craft, Interior, Settings }

    /// <summary>То, что экраны просят у корня игры.</summary>
    public interface IGameShell
    {
        GameSession Session { get; }
        void Exec(IGameCommand c);
        void SetScreen(Screen s);
        void NewGame();
        void Load(string slot);
        void Save(string slot);
        string SaveInfo(string slot);
        void Quit();
    }

    /// <summary>Все полноэкранные экраны, собранные кодом. Перестраиваются по запросу (время в них стоит).</summary>
    public sealed class Screens
    {
        readonly IGameShell _g;
        public readonly VisualElement Root;
        readonly Dictionary<Screen, VisualElement> _layers = new Dictionary<Screen, VisualElement>();
        readonly VisualElement _dialogueBox, _menuBox, _endBox, _chronicleBox, _mapBox, _rulerBox, _invBox, _craftBox, _interiorBox, _settingsBox;
        string _dialogueKey;
        public string RulerTab = "city";
        public string RulerCityId;
        public string InteriorText;
        public bool WorkshopMode;
        Texture2D _mapTex;

        public Screens(IGameShell g)
        {
            _g = g;
            Root = new VisualElement { pickingMode = PickingMode.Ignore };
            Ui.Absolute(Root, 0, 0, 0, 0);
            _menuBox = Layer(Screen.Menu, new Color(0.03f, 0.03f, 0.05f, 0.96f));
            _dialogueBox = Layer(Screen.Dialogue, new Color(0, 0, 0, 0.35f));
            _dialogueBox.parent.style.justifyContent = Justify.FlexEnd;
            _chronicleBox = Layer(Screen.Chronicle, new Color(0.02f, 0.02f, 0.03f, 0.9f));
            _endBox = Layer(Screen.End, new Color(0.01f, 0.01f, 0.02f, 0.97f));
            _mapBox = Layer(Screen.Map, new Color(0.02f, 0.03f, 0.04f, 0.97f));
            _rulerBox = Layer(Screen.Ruler, new Color(0, 0, 0, 0.6f));
            _invBox = Layer(Screen.Inventory, new Color(0, 0, 0, 0.55f));
            _craftBox = Layer(Screen.Craft, new Color(0, 0, 0, 0.55f));
            _interiorBox = Layer(Screen.Interior, new Color(0, 0, 0, 0.55f));
            _settingsBox = Layer(Screen.Settings, new Color(0.03f, 0.03f, 0.05f, 0.96f));
        }

        VisualElement Layer(Screen s, Color bg)
        {
            var full = Ui.FullScreen(bg);
            var box = Ui.Box();
            box.style.maxWidth = new Length(92, LengthUnit.Percent);
            box.style.maxHeight = new Length(92, LengthUnit.Percent);
            full.Add(box);
            Ui.Show(full, false);
            Root.Add(full);
            _layers[s] = full;
            return box;
        }

        public void Show(Screen current)
        {
            foreach (var kv in _layers) Ui.Show(kv.Value, kv.Key == current);
            switch (current)
            {
                case Screen.Menu: BuildMenu(); break;
                case Screen.Chronicle: BuildChronicle(); break;
                case Screen.End: BuildEnd(); break;
                case Screen.Map: BuildMap(); break;
                case Screen.Ruler: BuildRuler(); break;
                case Screen.Inventory: BuildInventory(); break;
                case Screen.Craft: BuildCraft(); break;
                case Screen.Interior: BuildInterior(); break;
                case Screen.Settings: BuildSettings(); break;
                case Screen.Dialogue: _dialogueKey = null; RefreshDialogue(); break;
            }
        }

        public bool IsOverUi(IPanel panel, Vector2 screenPos)
        {
            if (panel == null) return false;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, UnityEngine.Screen.height - screenPos.y));
            var picked = panel.Pick(p);
            return picked != null && picked.pickingMode == PickingMode.Position && picked != Root;
        }

        void Exec(IGameCommand c, Action rebuild)
        {
            _g.Exec(c);
            rebuild?.Invoke();
        }

        static Label Title(string t) { var l = Ui.Label(t, 26, Ui.Gold, true); l.style.marginBottom = 10; return l; }

        // ---------- Главное меню ----------
        void BuildMenu()
        {
            var b = _menuBox; b.Clear();
            b.style.width = 560;
            b.Add(Title("IMMORTAL"));
            b.Add(Ui.Label("Один бессмертный. Тысячи лет. Мир, который растёт у вас на глазах.", 16, Ui.Dim));
            b.Add(new VisualElement { style = { height = 12 } });
            if (_g.Session != null && !_g.Session.State.GameOver) b.Add(Ui.Button("Продолжить", () => _g.SetScreen(Screen.World)));
            b.Add(Ui.Button("Новая игра", () => _g.NewGame()));
            foreach (var slot in new[] { "auto", "1", "2", "3" })
            {
                string info = _g.SaveInfo(slot);
                var row = Ui.Row();
                row.Add(Ui.Button(slot == "auto" ? $"Загрузить автосохранение {info}" : $"Загрузить слот {slot} {info}", () => _g.Load(slot), info != null));
                if (slot != "auto" && _g.Session != null && !_g.Session.State.GameOver) row.Add(Ui.Button("Сохранить сюда", () => { _g.Save(slot); BuildMenu(); }));
                b.Add(row);
            }
            b.Add(Ui.Button("Настройки", () => _g.SetScreen(Screen.Settings)));
            b.Add(Ui.Button("Выход", () => _g.Quit()));
            b.Add(Ui.Label("F5 — быстрое сохранение, F9 — быстрая загрузка (слот 1).", 13, Ui.Dim));
        }

        // ---------- Диалог ----------
        public void RefreshDialogue()
        {
            var s = _g.Session; if (s == null) return;
            var act = s.State.Narrative.Active;
            if (act == null) return;
            var opts = DialogueEngine.Options(s);
            string key = act.DialogueId + "|" + act.NodeId + "|" + opts.Count + "|" + string.Join(",", opts.Select(o => o.Enabled ? "1" : "0"));
            if (key == _dialogueKey) return;
            _dialogueKey = key;
            var b = _dialogueBox; b.Clear();
            b.style.width = 980; b.style.marginBottom = 60;
            string speaker = DialogueEngine.Speaker(s);
            if (!string.IsNullOrEmpty(speaker)) b.Add(Ui.Label(speaker, 20, Ui.Gold, true));
            b.Add(Ui.Label(DialogueEngine.Text(s), 18));
            b.Add(new VisualElement { style = { height = 8 } });
            int n = 1;
            foreach (var o in opts)
            {
                int idx = o.Index;
                string hint = string.IsNullOrEmpty(o.Hint) ? "" : $"   [{o.Hint}]";
                b.Add(Ui.Button($"{n}. {o.Text}{hint}", () => { _g.Exec(new ChooseOption(idx)); _dialogueKey = null; RefreshDialogue(); }, o.Enabled));
                n++;
            }
        }

        public void ChooseByNumber(int number)
        {
            var s = _g.Session; if (s?.State.Narrative.Active == null) return;
            var opts = DialogueEngine.Options(s);
            if (number < 1 || number > opts.Count || !opts[number - 1].Enabled) return;
            _g.Exec(new ChooseOption(opts[number - 1].Index));
            _dialogueKey = null; RefreshDialogue();
        }

        // ---------- Хроника ----------
        void BuildChronicle()
        {
            var s = _g.Session; var b = _chronicleBox; b.Clear();
            b.style.width = 900;
            b.Add(Title("Хроника"));
            var sv = Ui.Scroll(620);
            int from = s.State.Narrative.ChronicleOpen ? s.State.Narrative.ChronicleFrom : Math.Max(0, s.State.Chronicle.Count - 60);
            foreach (var e in Chronicle.Since(s, from))
                sv.Add(Ui.Label(e.Text, 16, e.Glitch ? Ui.Mystic : e.Category == "jump_end" ? Ui.Gold : Ui.Text));
            b.Add(sv);
            b.Add(Ui.Button("Продолжить", () =>
            {
                if (s.State.Narrative.ChronicleOpen) _g.Exec(new CloseChronicle());
                _g.SetScreen(Screen.World);
            }));
        }

        // ---------- Концовка ----------
        void BuildEnd()
        {
            var s = _g.Session; var b = _endBox; b.Clear();
            b.style.width = 820;
            string id = s.State.EndingId ?? "old_age";
            string T(string k) => s.Content.ChronicleFile.templates.TryGetValue(k, out var l) && l.Count > 0 ? l[0] : k;
            b.Add(Title(T("ending_title_" + id)));
            b.Add(Ui.Label(T("ending_text_" + id), 18));
            var pn = s.PlayerNation;
            b.Add(new VisualElement { style = { height = 10 } });
            b.Add(Ui.Label($"{s.DateText}. Прожито лет: {s.State.Months / 12:0}. Спутников было: {s.State.Companions.Count}, жив{(s.State.Companions.Count(c => c.Alive) == 1 ? "" : "ы")}: {s.State.Companions.Count(c => c.Alive)}.", 15, Ui.Dim));
            if (pn != null) b.Add(Ui.Label($"{pn.Name}: городов {CitySystem.CityCount(s, pn)}, людей {CitySystem.NationPopulation(s, pn):0}, открытий {pn.Researched.Count}, намёков на симуляцию замечено: {s.State.Narrative.HintsSeen}.", 15, Ui.Dim));
            b.Add(Ui.Button("Новая игра", () => _g.NewGame()));
            b.Add(Ui.Button("В меню", () => _g.SetScreen(Screen.Menu)));
        }

        // ---------- Карта ----------
        void BuildMap()
        {
            var s = _g.Session; var b = _mapBox; b.Clear();
            int size = s.Map.Size;
            if (_mapTex == null || _mapTex.width != size) { _mapTex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point }; }
            var px = new Color32[size * size];
            var own = s.State.TerritoryOwner; int cell = s.Balance.territoryCell; int nx = s.State.TerritoryCellsX;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                int i = y * size + x;
                Color c;
                if (!Vision.IsExplored(s.State, i)) c = new Color(0.04f, 0.04f, 0.05f);
                else
                {
                    c = Look.Hex(s.Map.TerrainAt(i).color);
                    if (own != null)
                    {
                        int o = own[(y / cell) * nx + (x / cell)];
                        if (o > 0) c = Color.Lerp(c, Look.Hex(s.State.Nations[o - 1].Color), 0.35f);
                    }
                }
                px[i] = c;
            }
            void Dot(Vec2 p, Color c, int r)
            {
                s.Map.TileXY(p, out int tx, out int ty);
                for (int dy = -r; dy <= r; dy++) for (int dx = -r; dx <= r; dx++)
                {
                    int x = tx + dx, y = ty + dy;
                    if (x >= 0 && y >= 0 && x < size && y < size) px[y * size + x] = c;
                }
            }
            foreach (var p in s.State.Pois) if (p.Discovered) Dot(p.Pos, s.Content.Poi[p.DefId].kind == "zone" ? new Color(0.6f, 0.4f, 0.9f) : Look.Hex(s.Content.Poi[p.DefId].color), 1);
            foreach (var a in s.State.Armies) if (Vision.IsExplored(s.State, s.Map.TileAt(a.Pos)) && a.Kind != "settlers") Dot(a.Pos, a.Kind == "raiders" ? Color.red : Look.Hex(s.Nation(a.NationId)?.Color, Color.red), 1);
            foreach (var c in s.State.Cities) if (Vision.IsExplored(s.State, s.Map.TileAt(c.Pos))) Dot(c.Pos, Color.white, 2);
            if (s.HeroUnit != null) Dot(s.HeroUnit.Pos, new Color(1f, 0.85f, 0.2f), 2);
            _mapTex.SetPixels32(px); _mapTex.Apply();

            b.Add(Title("Карта мира"));
            var row = Ui.Row(); row.style.alignItems = Align.FlexStart; row.style.flexWrap = Wrap.NoWrap;
            var holder = new VisualElement(); holder.style.width = 768; holder.style.height = 768;
            var img = new Image { image = _mapTex, scaleMode = ScaleMode.StretchToFill };
            img.style.width = 768; img.style.height = 768;
            // Строка 0 текстуры — юг мира: Image рисует её внизу, так что север сверху без переворота.
            holder.Add(img);
            float k = 768f / (size * s.Map.TileSize);
            foreach (var c in s.State.Cities)
            {
                if (!Vision.IsExplored(s.State, s.Map.TileAt(c.Pos))) continue;
                var l = Ui.Label(c.Name, 13, Color.white, true);
                Ui.Absolute(l, c.Pos.x * k + 6, 768 - c.Pos.y * k - 8);
                l.style.backgroundColor = new Color(0, 0, 0, 0.5f);
                holder.Add(l);
            }
            row.Add(holder);
            var legend = new VisualElement(); legend.style.marginLeft = 16; legend.style.width = 300;
            legend.Add(Ui.Label("Нации", 18, Ui.Gold, true));
            foreach (var n in s.State.Nations)
            {
                var r = Ui.Row(); var sw = new VisualElement(); sw.style.width = 14; sw.style.height = 14; sw.style.backgroundColor = Look.Hex(n.Color); sw.style.marginRight = 6;
                r.Add(sw); r.Add(Ui.Label($"{n.Name} ({s.Content.Era[n.EraId].name})" + (n.Destroyed ? " — пала" : ""), 14));
                legend.Add(r);
            }
            legend.Add(Ui.Label("Белые точки — города, жёлтая — вы, лиловые — дым, цветные — армии.", 13, Ui.Dim));
            legend.Add(Ui.Button("Закрыть (M)", () => _g.SetScreen(Screen.World)));
            row.Add(legend);
            b.Add(row);
        }

        // ---------- Меню правителя ----------
        public void BuildRuler()
        {
            var s = _g.Session; var b = _rulerBox; b.Clear();
            b.style.width = 1240; b.style.height = 820;
            var pn = s.PlayerNation;
            if (pn == null)
            {
                b.Add(Title("У вас нет общины"));
                b.Add(Ui.Label("Найдите свободное место (не в чужих землях и не в дыму) и основайте лагерь. Завербованные люди придут к вашему костру.", 17));
                b.Add(Ui.Button("Основать лагерь здесь (F)", () => { _g.Exec(new FoundCamp()); _g.SetScreen(Screen.World); }));
                b.Add(Ui.Button("Закрыть", () => _g.SetScreen(Screen.World)));
                return;
            }
            var cities = s.State.Cities.Where(c => c.NationId == pn.Id).ToList();
            if (RulerCityId == null || !cities.Any(c => c.Id == RulerCityId)) RulerCityId = pn.CapitalCityId ?? cities.FirstOrDefault()?.Id;
            var city = s.City(RulerCityId);

            b.Add(Title($"{pn.Name} · {HeroSystem.RoleName(s.State.Hero.Role)} · {s.PlayerEraDef.name}"));
            var tabs = Ui.Row(6);
            foreach (var (id, name) in new[] { ("city", "Город"), ("build", "Стройка"), ("tech", "Знания"), ("people", "Люди"), ("diplo", "Дипломатия"), ("army", "Войско"), ("time", "Время и законы") })
            {
                var tb = Ui.Button(name, () => { RulerTab = id; BuildRuler(); });
                if (id == RulerTab) tb.style.backgroundColor = new Color(0.45f, 0.36f, 0.2f);
                tabs.Add(tb);
            }
            tabs.Add(Ui.Button("Закрыть (Tab)", () => _g.SetScreen(Screen.World)));
            b.Add(tabs);
            if (cities.Count > 1)
            {
                var cr = Ui.Row(6);
                cr.Add(Ui.Label("Город: ", 15, Ui.Dim));
                foreach (var c in cities) { var cid = c.Id; var cb = Ui.Button(c.Name, () => { RulerCityId = cid; BuildRuler(); }); if (cid == RulerCityId) cb.style.color = Ui.Gold; cr.Add(cb); }
                b.Add(cr);
            }
            var body = Ui.Scroll(640);
            b.Add(body);
            Action re = BuildRuler;
            switch (RulerTab)
            {
                case "city": CityTab(s, pn, city, body, re); break;
                case "build": BuildTab(s, pn, city, body, re); break;
                case "tech": TechTab(s, pn, body, re); break;
                case "people": PeopleTab(s, pn, city, body, re); break;
                case "diplo": DiploTab(s, pn, body, re); break;
                case "army": ArmyTab(s, pn, city, body, re); break;
                case "time": TimeTab(s, pn, body, re); break;
            }
        }

        static string Res(GameSession s, NationState pn) =>
            string.Join(" · ", new[] { "wood", "stone", "food", "hide", "fiber", "ore", "bronze", "essence" }.Select(r => $"{s.Content.ItemName(r)}: {pn.Get(r):0}"));

        void CityTab(GameSession s, NationState pn, CityState c, VisualElement b, Action re)
        {
            if (c == null) { b.Add(Ui.Label("Нет городов.", 17)); return; }
            var st = CitySystem.Stage(s, c); var t = CitySystem.Totals(s, c);
            b.Add(Ui.Label($"{c.Name} — {st.name}", 22, Ui.Gold, true));
            b.Add(Ui.Label($"Население: {c.Population:0.0} (предел ступени {st.maxPop:0}) · жильё {t.Housing:0} · сытость {c.Satiety:0.00} · обеспеченность {c.Provision:0.00} · здоровье {c.Health:0.00}", 16));
            b.Add(Ui.Label($"Запас еды города: {c.FoodStock:0} · гарнизон {c.Garrison:0} · лояльность {c.Loyalty:0} ({AutonomyName(c.AutonomyState)})", 16, c.Loyalty < 40 ? Ui.Bad : Ui.Text));
            if (c.Hungry) b.Add(Ui.Label("Город голодает! Стройте поля и загоны, сдавайте добычу.", 16, Ui.Bad));
            b.Add(Ui.Label("Запасы народа: " + Res(s, pn), 15, Ui.Dim));
            var gov = s.Companion(c.GovernorId);
            b.Add(Ui.Label(gov != null ? $"Наместник: {gov.Name} ({Templates.RelationWord(gov.Relation)}) — сам ведёт стройку по очереди и специализации." : "Наместника нет: стройте сами или назначьте наместника во вкладке «Люди».", 15));
            var sr = Ui.Row(); sr.Add(Ui.Label("Специализация: ", 15));
            foreach (var (id, name) in new[] { ("economy", "Хозяйственная"), ("admin", "Административная"), ("military", "Военная"), ("science", "Научная") })
            {
                var sid = id;
                var btn = Ui.Button(name, () => Exec(new SetSpecialization(c.Id, sid), re));
                if (c.Specialization == id) btn.style.color = Ui.Gold;
                sr.Add(btn);
            }
            b.Add(sr);
            if (c.StageIndex + 1 < s.Content.Stages.Count)
            {
                var next = s.Content.Stages[c.StageIndex + 1];
                var req = new List<string> { $"население {next.minPop:0}" };
                foreach (var r in next.requiredBuildings) req.Add((c.HasBuilding(r) ? "✓ " : "✗ ") + s.Content.Buildings[r].name);
                foreach (var kv in next.cost) req.Add($"{kv.Value} {s.Content.ItemName(kv.Key)}");
                b.Add(Ui.Label($"Следующая ступень — {next.name}: {string.Join(", ", req)}" + (next.unlocksEra != null ? $". Откроет: {s.Content.Era[next.unlocksEra].name}" : ""), 15, Ui.Gold));
            }
            if (c.BuildQueue.Count > 0) b.Add(Ui.Label("Очередь: " + string.Join(", ", c.BuildQueue.Select(q => s.Content.Buildings[q].name)), 15, Ui.Dim));
            b.Add(Ui.Label("Постройки: " + string.Join(", ", c.Buildings.GroupBy(x => x.DefId).Select(g => $"{s.Content.Buildings[g.Key].name}×{g.Count()}")), 14, Ui.Dim));
        }

        static string AutonomyName(string a) => a switch
        {
            Autonomy.Vulnerable => "уязвим", Autonomy.Entrenched => "закрепился в самоуправлении", Autonomy.Legitimate => "вольный город", _ => "верен"
        };

        void BuildTab(GameSession s, NationState pn, CityState c, VisualElement b, Action re)
        {
            if (c == null) return;
            b.Add(Ui.Label("Запасы: " + Res(s, pn), 15, Ui.Dim));
            b.Add(Ui.Label("«Строить» — сразу из запасов. «В очередь» — наместник построит, когда хватит ресурсов.", 14, Ui.Dim));
            foreach (var d in s.Content.BuildingList)
            {
                if (d.id == "fire_pit" || !CitySystem.Unlocked(s, pn, d) && d.tech == null) continue;
                bool can = CitySystem.CanBuild(s, c, d.id, out var why);
                bool unlocked = CitySystem.Unlocked(s, pn, d);
                var row = Ui.Row();
                string cost = string.Join(", ", d.cost.Select(kv => $"{kv.Value} {s.Content.ItemName(kv.Key)}"));
                var lbl = Ui.Label($"{d.name} ({cost}){(d.megastructure ? " — МЕГАСООРУЖЕНИЕ" : "")}", 15, unlocked ? (can ? Ui.Text : Ui.Dim) : new Color(0.4f, 0.4f, 0.4f));
                lbl.style.width = 520;
                row.Add(lbl);
                var id = d.id;
                row.Add(Ui.Button("Строить", () => Exec(new Build(c.Id, id), re), can));
                row.Add(Ui.Button("В очередь", () => Exec(new QueueBuild(c.Id, id), re), unlocked));
                if (!can) row.Add(Ui.Label(why, 13, Ui.Dim));
                b.Add(row);
                b.Add(Ui.Label(d.description, 13, Ui.Dim));
            }
            if (c.HasBuilding("mirror_of_sky")) b.Add(Ui.Button("Активировать Зеркало небес", () => Exec(new ActivateMirror(), re)));
        }

        void TechTab(GameSession s, NationState pn, VisualElement b, Action re)
        {
            var cur = TechSystem.CurrentTarget(s, pn);
            b.Add(Ui.Label(cur != null ? $"Сейчас изучается: {cur.name} ({(pn.TechProgress.TryGetValue(cur.id, out var p) ? p : 0):0}/{cur.cost:0})" : "Все доступные знания этой эпохи открыты.", 17, Ui.Gold));
            var pr = Ui.Row(); pr.Add(Ui.Label("Приоритет: ", 15));
            foreach (var br in s.Content.TechFile.branches)
            {
                var id = br.id;
                var btn = Ui.Button(br.name, () => Exec(new SetTechPriority(id), re));
                if (pn.PriorityBranch == id) btn.style.color = Ui.Gold;
                pr.Add(btn);
            }
            b.Add(pr);
            b.Add(Ui.Button($"Вложить 1 мистический ресурс в науку (+{s.Balance.essence.sciencePerEssence:0})", () => Exec(new InvestEssence(), re), HeroSystem.EssenceAvailable(s) > 0));
            b.Add(Ui.Label("Ускоряют открытия: руины и находки из дыма, учёные-спутники, Школа и Святилище.", 14, Ui.Dim));
            foreach (var br in s.Content.TechFile.branches)
            {
                b.Add(Ui.Label(br.name, 18, Ui.Gold, true));
                foreach (var t in s.Content.TechFile.techs.Where(t => t.branch == br.id))
                {
                    bool done = pn.Researched.Contains(t.id);
                    bool avail = TechSystem.Available(s, pn, t);
                    string era = s.Content.Era[t.era].name;
                    string extra = (t.requiresEssence > 0 ? $", нужно {t.requiresEssence} ресурса" : "") + (t.requiresBuilding != null ? $", нужна {s.Content.Buildings[t.requiresBuilding].name}" : "");
                    b.Add(Ui.Label($"{(done ? "✓" : avail ? "•" : " ")} {t.name} — {era}, {t.cost:0}{extra}. {t.description}", 14, done ? Ui.Good : avail ? Ui.Text : Ui.Dim));
                }
            }
        }

        void PeopleTab(GameSession s, NationState pn, CityState city, VisualElement b, Action re)
        {
            b.Add(Ui.Label("Спутники — живые люди: они стареют, умирают и оставляют потомков. С вами ходят до трёх.", 14, Ui.Dim));
            foreach (var c in s.State.Companions.OrderByDescending(x => x.Alive))
            {
                double age = c.AgeYears(s.State.Months);
                var box = Ui.Box(Ui.BgLight, 6, false); box.style.marginBottom = 4;
                if (!c.Alive)
                {
                    box.Add(Ui.Label($"† {c.Name} — умер в {s.Balance.startYear + (int)(c.DeathMonths / 12)} году", 15, Ui.Dim));
                    b.Add(box); continue;
                }
                box.Add(Ui.Label($"{c.Name} · {age:0} из ~{c.LifespanYears:0} лет · {Templates.RelationWord(c.Relation)} ({c.Relation:0}) · {CompanionSystem.RoleName(c.Role)}" + (c.Role == CompanionRoles.Errand ? $" ({CompanionSystem.ErrandName(c.ErrandKind)})" : ""), 15, c.Relation < -20 ? Ui.Bad : Ui.Text));
                box.Add(Ui.Label($"Бой {c.Combat:0}, сбор {c.Gathering:0}, речь {c.Speech:0}, ум {c.Mind:0}. Характер: {RecruitmentSystem.CharacterName(c.Character)}.", 13, Ui.Dim));
                if (c.Role != CompanionRoles.Errand && c.Role != CompanionRoles.Rebel)
                {
                    var row = Ui.Row(); var id = c.Id;
                    row.Add(Ui.Button("В отряд", () => Exec(new AssignCompanion(id, CompanionRoles.Party), re), c.Role != CompanionRoles.Party));
                    row.Add(Ui.Button("В лагерь", () => Exec(new AssignCompanion(id, CompanionRoles.Camp), re), c.Role != CompanionRoles.Camp));
                    if (city != null) row.Add(Ui.Button($"Наместник: {city.Name}", () => Exec(new AssignCompanion(id, CompanionRoles.Governor, city.Id), re)));
                    row.Add(Ui.Button("Учёный", () => Exec(new AssignCompanion(id, CompanionRoles.Scholar), re), c.Role != CompanionRoles.Scholar));
                    row.Add(Ui.Button("За ресурсами", () => Exec(new AssignCompanion(id, CompanionRoles.Errand, "resources"), re)));
                    row.Add(Ui.Button("Разведка", () => Exec(new AssignCompanion(id, CompanionRoles.Errand, "intel"), re)));
                    row.Add(Ui.Button("В дым за ресурсом", () => Exec(new AssignCompanion(id, CompanionRoles.Errand, "essence"), re)));
                    box.Add(row);
                }
                b.Add(box);
            }
            if (s.State.Companions.Count == 0) b.Add(Ui.Label("Пока никого. Ищите людей в мире и уговаривайте их (ЛКМ по человеку).", 16));
        }

        void DiploTab(GameSession s, NationState pn, VisualElement b, Action re)
        {
            b.Add(Ui.Label($"Репутация в мире: {s.State.Hero.Reputation:0}. Личная встреча с правителем (придите в его столицу) сильнее посольства." + (s.Content.EraOrder(pn.EraId) >= 2 ? " Посольство стоит 10 влияния." : ""), 15, Ui.Dim));
            foreach (var n in s.State.Nations.Where(n => !n.IsPlayer))
            {
                var r = Diplomacy.Rel(s, n.Id, pn.Id);
                var box = Ui.Box(Ui.BgLight, 8, false); box.style.marginBottom = 6;
                string leader = n.Leader.Alive ? $"{n.Leader.Title} {n.Leader.Name}" + (n.Leader.Immortal ? " (бессмертный)" : "") : (n.Leader.Captive ? $"{n.Leader.Name} в плену — регентство" : "регентство");
                box.Add(Ui.Label($"{n.Name} — {leader}", 18, Look.Hex(n.Color), true));
                box.Add(Ui.Label($"{s.Content.Era[n.EraId].name}, городов {CitySystem.CityCount(s, n)}, людей {CitySystem.NationPopulation(s, n):0}, сила {Diplomacy.Strength(s, n):0}. Отношение: {r.Attitude:0}. {(r.War ? "ВОЙНА" : "мир")}." +
                    (r.Agreements.Count > 0 ? " Соглашения: " + string.Join(", ", r.Agreements.Select(Diplomacy.AgreementName)) : ""), 15, r.War ? Ui.Bad : Ui.Text));
                var row = Ui.Row(); var id = n.Id;
                if (r.War) row.Add(Ui.Button("Предложить мир", () => Exec(new ProposeTreaty(id, "peace"), re)));
                else
                {
                    foreach (var ag in Diplomacy.Agreements)
                        if (!r.Agreements.Contains(ag)) { var a = ag; row.Add(Ui.Button(Diplomacy.AgreementName(ag), () => Exec(new ProposeTreaty(id, a), re))); }
                    row.Add(Ui.Button("Объявить войну", () => Exec(new DeclareWar(id), re)));
                }
                box.Add(row);
                foreach (var c in s.State.Cities.Where(c => c.NationId == n.Id && c.AutonomyState != Autonomy.Loyal))
                {
                    var cid = c.Id;
                    box.Add(Ui.Button($"Склонить уязвимый город {c.Name} (30 влияния или лично)", () => Exec(new PersuadeCity(cid), re)));
                }
                b.Add(box);
            }
            foreach (var c in s.State.Cities.Where(c => c.NationId == null))
            {
                var cid = c.Id;
                b.Add(Ui.Button($"Вольный город {c.Name}: склонить к союзу (30 влияния или лично)", () => Exec(new PersuadeCity(cid), re)));
            }
        }

        void ArmyTab(GameSession s, NationState pn, CityState city, VisualElement b, Action re)
        {
            b.Add(Ui.Label("Армии — настоящие отряды, идущие по миру. Если вы рядом, битва идёт вокруг вас.", 14, Ui.Dim));
            if (city != null)
            {
                var row = Ui.Row();
                row.Add(Ui.Button($"Собрать войско 20 в {city.Name} (40 еды, 4 чел.)", () => Exec(new RecruitArmy(city.Id, 20), re)));
                row.Add(Ui.Button($"Собрать войско 50 (100 еды, 10 чел.)", () => Exec(new RecruitArmy(city.Id, 50), re)));
                row.Add(Ui.Button("Отправить поселенцев (20 еды, 6 чел.)", () => Exec(new SendSettlers(city.Id), re)));
                b.Add(row);
            }
            foreach (var a in s.State.Armies.Where(a => a.NationId == pn.Id && a.Kind == "army"))
            {
                var box = Ui.Box(Ui.BgLight, 6, false); box.style.marginBottom = 4;
                var gen = s.Companion(a.GeneralId);
                box.Add(Ui.Label($"Войско {a.Strength:0} · {a.State}" + (a.TargetCityId != null ? $" → {s.City(a.TargetCityId)?.Name}" : "") + (gen != null ? $" · полководец {gen.Name}" : ""), 15));
                var row = Ui.Row(); var aid = a.Id;
                foreach (var c in s.State.Cities.Where(c => c.NationId != pn.Id && (c.NationId == null || Diplomacy.AtWar(s, pn.Id, c.NationId))).Take(8))
                {
                    var cid = c.Id;
                    row.Add(Ui.Button($"На {c.Name}", () => Exec(new OrderArmy(aid, cid), re)));
                }
                foreach (var comp in s.State.Companions.Where(c => c.Alive && c.Role == CompanionRoles.Camp).Take(3))
                {
                    var cid = comp.Id;
                    row.Add(Ui.Button($"Полководец: {comp.Name}", () => Exec(new AssignCompanion(cid, CompanionRoles.General, aid), re)));
                }
                box.Add(row);
                b.Add(box);
            }
        }

        void TimeTab(GameSession s, NationState pn, VisualElement b, Action re)
        {
            var ns = s.State.Narrative;
            b.Add(Ui.Label($"Табу на ресурс жизни: {(ns.Taboo == "strict" ? "строгое (казнь)" : ns.Taboo == "lenient" ? "мягкое" : "нет")}", 17));
            var tr = Ui.Row();
            tr.Add(Ui.Button("Строгое табу", () => Exec(new SetTabooCommand("strict"), re), ns.Taboo != "strict"));
            tr.Add(Ui.Button("Мягкое табу", () => Exec(new SetTabooCommand("lenient"), re), ns.Taboo != "lenient"));
            tr.Add(Ui.Button("Отменить табу", () => Exec(new SetTabooCommand("none"), re), ns.Taboo != "none"));
            b.Add(tr);
            b.Add(new VisualElement { style = { height = 10 } });
            b.Add(Ui.Label("Прыжок во времени: мир быстро проживёт годы без вас, хроника расскажет, что случилось. Жизненная сила тратится медленнее, чем в пути.", 15, Ui.Dim));
            var opts = ns.JumpOptions.Count > 0 ? ns.JumpOptions : s.Balance.time.jumpOptions;
            var jr = Ui.Row();
            bool can = Roles.Rank(s.State.Hero.Role) >= 1 || ns.JumpOptions.Count > 0;
            foreach (var y in opts) { int yy = y; jr.Add(Ui.Button($"Удалиться от дел на {y} лет", () => { _g.Exec(new TimeJump(yy)); _g.SetScreen(s.State.Narrative.ChronicleOpen ? Screen.Chronicle : Screen.World); }, can)); }
            b.Add(jr);
            if (!can) b.Add(Ui.Label("Сначала станьте вождём (лагерь должен вырасти в деревню).", 14, Ui.Bad));
            b.Add(Ui.Button("Открыть хронику", () => _g.SetScreen(Screen.Chronicle)));
            b.Add(Ui.Label($"Намёков на странность мира замечено: {ns.HintsSeen}.", 14, Ui.Mystic));
        }

        // ---------- Снаряжение ----------
        void BuildInventory()
        {
            var s = _g.Session; var b = _invBox; b.Clear();
            b.style.width = 1000;
            var h = s.State.Hero;
            b.Add(Title($"{h.Name} · {HeroSystem.RoleName(h.Role)}"));
            var sv = Ui.Scroll(700); b.Add(sv);
            sv.Add(Ui.Label($"Тело {h.Body:0} (с учётом возраста {HeroSystem.EffBody(s):0.0}) · Ум {h.Mind:0} ({HeroSystem.EffMind(s):0.0}) · Воля {h.Will:0}", 17));
            sv.Add(Ui.Label($"Вид на {HeroSystem.ApparentAge(h):0} лет. Чем меньше жизненной силы, тем слабее тело и сильнее ум и речь.", 14, Ui.Dim));
            if (h.AttributePoints > 0)
            {
                var ar = Ui.Row(); ar.Add(Ui.Label($"Очки атрибутов: {h.AttributePoints}", 15, Ui.Gold));
                foreach (var (id, name) in new[] { ("body", "+Тело"), ("mind", "+Ум"), ("will", "+Воля") }) { var a = id; ar.Add(Ui.Button(name, () => Exec(new SpendAttribute(a), BuildInventory))); }
                sv.Add(ar);
            }
            sv.Add(Ui.Label("Навыки: " + string.Join(", ", h.Skills.Select(kv => $"{HeroSystem.SkillName(kv.Key)} {kv.Value:0}")), 15));
            sv.Add(Ui.Label($"Опыт {h.Xp}/{s.Balance.hero.xpPerPoint} · репутация {h.Reputation:0} · поражений {h.Defeats}", 14, Ui.Dim));
            if (h.RejuvenationUnlocked)
                sv.Add(Ui.Button($"Омолодиться ({s.Balance.essence.rejuvenateCost} ресурса → +{s.Balance.essence.rejuvenateYears:0} лет; часть навыка и воспоминаний уйдёт)", () => Exec(new Rejuvenate(), BuildInventory), HeroSystem.EssenceAvailable(s) >= s.Balance.essence.rejuvenateCost));
            sv.Add(Ui.Label("Снаряжение", 19, Ui.Gold, true));
            foreach (var it in h.Items)
            {
                bool eq = it.Id == h.WeaponId || it.Id == h.ArmorId || it.Id == h.TrinketId;
                var def = s.Content.Items[it.DefId];
                string aff = it.Affixes.Count > 0 ? " · " + string.Join(", ", it.Affixes.Select(kv => $"{kv.Key} +{kv.Value:0.##}")) : "";
                var row = Ui.Row();
                var l = Ui.Label($"{LootSystem.ItemLabel(s, it)} — {(def.damage > 0 ? $"урон {def.damage * HeroSystem.QualityMult(s, it):0.0}" : def.armor > 0 ? $"броня {def.armor * HeroSystem.QualityMult(s, it):0.0}" : "оберег")}{aff}", 15, eq ? Ui.Gold : Ui.Text);
                l.style.width = 700; row.Add(l);
                var id = it.Id;
                if (!eq) row.Add(Ui.Button("Надеть", () => Exec(new Equip(id), BuildInventory)));
                sv.Add(row);
            }
            if (h.Items.Count == 0) sv.Add(Ui.Label("Ничего. Сделайте дубину (C) из дерева и камня.", 15, Ui.Dim));
            sv.Add(Ui.Label("Ресурсы при себе: " + (h.Inventory.Count == 0 ? "нет" : string.Join(", ", h.Inventory.Select(kv => $"{s.Content.ItemName(kv.Key)} {kv.Value}"))), 15));
            sv.Add(Ui.Label("Воспоминания: " + string.Join(", ", h.Memories.Select(m => HeroSystem.MemoryName(s, m))), 14, Ui.Mystic));
            b.Add(Ui.Button("Закрыть (I)", () => _g.SetScreen(Screen.World)));
        }

        // ---------- Ремесло ----------
        void BuildCraft()
        {
            var s = _g.Session; var b = _craftBox; b.Clear();
            b.style.width = 1000;
            bool shop = LootSystem.WorkshopNearby(s) != null;
            b.Add(Title(shop ? "Мастерская" : "Ремесло в поле"));
            b.Add(Ui.Label("В поле вещь получается сразу, но полевого качества. В Мастерской — качество эпохи, а сырьё можно брать из запасов народа.", 14, Ui.Dim));
            var sv = Ui.Scroll(620); b.Add(sv);
            foreach (var r in s.Content.ItemsFile.recipes)
            {
                var def = s.Content.Items[r.result];
                bool known = s.State.Hero.KnownRecipes.Contains(r.id) || (r.tech != null && s.PlayerNation != null && s.PlayerNation.Researched.Contains(r.tech));
                if (!known) continue;
                string inputs = string.Join(", ", r.inputs.Select(kv => $"{kv.Value} {s.Content.ItemName(kv.Key)} ({s.State.Hero.Count(kv.Key)})"));
                var row = Ui.Row();
                var l = Ui.Label($"{def.name}: {inputs}", 15); l.style.width = 560; row.Add(l);
                bool canField = LootSystem.CanCraft(s, r, false, out var whyF);
                bool canShop = LootSystem.CanCraft(s, r, true, out var whyS);
                var id = r.id;
                row.Add(Ui.Button("Сделать в поле", () => Exec(new Craft(id, false), BuildCraft), canField));
                row.Add(Ui.Button("В Мастерской", () => Exec(new Craft(id, true), BuildCraft), canShop));
                if (!canField && !canShop) row.Add(Ui.Label(whyS ?? whyF, 13, Ui.Dim));
                sv.Add(row);
            }
            b.Add(Ui.Button("Закрыть (C)", () => _g.SetScreen(Screen.World)));
        }

        // ---------- Настройки ----------
        void BuildSettings()
        {
            var b = _settingsBox; b.Clear();
            b.style.width = 620;
            b.Add(Title("Настройки"));
            void Volume(string name, System.Func<float> get, System.Action<float> set)
            {
                var row = Ui.Row();
                var l = Ui.Label($"{name}: {get() * 100:0}%", 17); l.style.width = 300; row.Add(l);
                row.Add(Ui.Button("−", () => { set(get() - 0.1f); BuildSettings(); }));
                row.Add(Ui.Button("+", () => { set(get() + 0.1f); BuildSettings(); }));
                b.Add(row);
            }
            void Toggle(string name, System.Func<bool> get, System.Action<bool> set)
            {
                b.Add(Ui.Button($"{name}: {(get() ? "вкл" : "выкл")}", () => { set(!get()); BuildSettings(); }));
            }
            Volume("Общая громкость", () => GameSettings.Master, v => GameSettings.Master = v);
            Volume("Музыка", () => GameSettings.Music, v => GameSettings.Music = v);
            Volume("Эффекты", () => GameSettings.Sfx, v => GameSettings.Sfx = v);
            Toggle("Числа урона", () => GameSettings.DamageNumbers, v => GameSettings.DamageNumbers = v);
            Toggle("Тряска камеры", () => GameSettings.CameraShake, v => GameSettings.CameraShake = v);
            Toggle("Звуки-заглушки, если нет файлов", () => GameSettings.SynthAudio, v => GameSettings.SynthAudio = v);
            b.Add(Ui.Label("Свои звуки, музыку, модели и текстуры можно положить в Resources — см. docs/ASSETS.md.", 14, Ui.Dim));
            b.Add(Ui.Button("Назад", () => { PlayerPrefs.Save(); _g.SetScreen(_g.Session != null && !_g.Session.State.GameOver ? Screen.Menu : Screen.Menu); }));
        }

        // ---------- Интерьер постройки ----------
        void BuildInterior()
        {
            var b = _interiorBox; b.Clear();
            b.style.width = 760;
            b.Add(Title("Внутри"));
            b.Add(Ui.Label(InteriorText ?? "", 17));
            b.Add(Ui.Button("Выйти", () => _g.SetScreen(Screen.World)));
        }
    }

    /// <summary>Эдикт о табу из меню правителя (с эпохи царств стоит влияния).</summary>
    public sealed class SetTabooCommand : IGameCommand
    {
        readonly string _mode;
        public SetTabooCommand(string mode) { _mode = mode; }
        public void Execute(GameSession s)
        {
            var pn = s.PlayerNation; if (pn == null) return;
            if (s.Content.EraOrder(pn.EraId) >= 2)
            {
                if (pn.Influence < 15) { s.Message("Нужно 15 влияния для эдикта."); return; }
                pn.Influence -= 15;
            }
            Effects.Apply(s, new Immortal.Core.Content.EffectDef { taboo = _mode }, new DialogueState());
            s.Message("Эдикт о табу объявлен.");
        }
    }
}
