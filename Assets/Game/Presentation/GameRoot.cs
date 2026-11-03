using System;
using System.IO;
using Immortal.Core;
using Immortal.Core.Commands;
using Immortal.Core.Content;
using Immortal.Presentation.UI;
using UnityEngine;
using UnityEngine.UIElements;
using UiScreen = Immortal.Presentation.UI.Screen;

namespace Immortal.Presentation
{
    /// <summary>
    /// Единственная сцена Boot: всё остальное создаётся кодом. Корень связывает симуляцию (Game.Core) с видами,
    /// вводом, камерой и интерфейсом. Конечный автомат экранов: Menu, World, Map, Ruler, Dialogue, Chronicle, End.
    /// </summary>
    public sealed class GameRoot : MonoBehaviour, IGameShell
    {
        public static GameRoot Instance { get; private set; }
        public GameSession Session { get; private set; }
        public ContentDatabase Content { get; private set; }
        public UiScreen Current { get; private set; } = UiScreen.Menu;

        CameraRig _cam;
        WorldView _world;
        UnitViews _units;
        Markers _markers;
        Ambience _ambience;
        UIDocument _doc;
        Hud _hud;
        Screens _screens;
        float _acc;
        Vector2 _lastMove;
        bool _holdMove;
        float _holdTimer, _autosaveTimer;
        Vector3 _cursorWorld;
        bool _cursorValid;
        AudioService _audio;
        float _musicTimer, _stepDist;
        Vector3 _lastHeroWorld;
        public AudioService Audio => _audio;
        /// <summary>Ускорение времени ×5 вне боя (T).</summary>
        public bool FastForward;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AutoCreate()
        {
            if (FindFirstObjectByType<GameRoot>() == null) new GameObject("GameRoot").AddComponent<GameRoot>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            Application.targetFrameRate = 60;
            Look.Init();
            Content = ContentDatabase.Load(name =>
            {
                var ta = Resources.Load<TextAsset>("Data/" + name);
                return ta != null ? ta.text : null;
            });
            Look.InitTerrain(Content);
            var errors = ContentValidator.Validate(Content);
            foreach (var e in errors) Debug.LogWarning("[Контент] " + e);

            _cam = new CameraRig();
            _world = new WorldView(transform);
            _units = new UnitViews(transform, _world);
            _markers = new Markers(transform, _world);
            _ambience = new Ambience(transform, _world);
            CreateEventSystem();
            BuildUi();
            _audio = AudioService.Create(transform);
            Ui.OnClickSound = () => _audio.Play("ui_click");
            CreateSun();
            SetScreen(UiScreen.Menu);
            ScreenshotRunner.TryStart(this);
        }

        /// <summary>Солнце нужно только моделям со стандартными материалами; примитивы освещает свой шейдер.</summary>
        void CreateSun()
        {
            if (FindFirstObjectByType<Light>() != null) return;
            var go = new GameObject("Sun");
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.1f;
            l.color = new Color(1f, 0.96f, 0.88f);
            l.shadows = LightShadows.Soft;
            go.transform.rotation = Quaternion.LookRotation(-new Vector3(0.35f, 0.85f, -0.4f).normalized);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.45f, 0.47f, 0.52f);
        }

        void CreateEventSystem()
        {
            if (FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null) return;
            var go = new GameObject("EventSystem");
            go.transform.SetParent(transform, false);
            go.AddComponent<UnityEngine.EventSystems.EventSystem>();
#if ENABLE_INPUT_SYSTEM && IMMORTAL_INPUT_SYSTEM
            go.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
#else
            go.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
#endif
        }

        void BuildUi()
        {
            var go = new GameObject("UI");
            go.SetActive(false);
            go.transform.SetParent(transform, false);
            _doc = go.AddComponent<UIDocument>();
            _doc.panelSettings = Ui.CreatePanelSettings();
            go.SetActive(true);
            var root = _doc.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            Ui.ApplyFont(root);
            _hud = new Hud();
            _screens = new Screens(this);
            root.Add(_hud.Root);
            root.Add(_screens.Root);
        }

        // ---------- IGameShell ----------
        public void Exec(IGameCommand c)
        {
            if (Session == null) return;
            Session.Execute(c);
            ProcessEvents();
        }

        public void SetScreen(UiScreen s)
        {
            if (s != Current && (s == UiScreen.Map || s == UiScreen.Ruler || s == UiScreen.Inventory || s == UiScreen.Craft || s == UiScreen.Interior))
                _audio?.Play("ui_open");
            Current = s;
            Ui.Show(_hud.Root, Session != null && (s == UiScreen.World || s == UiScreen.Dialogue));
            _screens.Show(s);
        }

        public void NewGame() => StartSession(GameSession.NewGame(Content, Environment.TickCount & 0xFFFFF));

        public void StartSession(GameSession s)
        {
            Session = s;
            _world.Bind(s);
            _units.Bind(s);
            _markers.Bind(s);
            _ambience.Bind(s);
            _acc = 0; _autosaveTimer = 0;
            var hero = s.HeroUnit;
            if (hero != null) _cam.Snap(_world.Ground(hero.Pos));
            SetScreen(UiScreen.World);
            ProcessEvents();
        }

        public void Load(string slot)
        {
            var s = SaveSystem.Load(Content, slot);
            if (s == null) { _hud.Message("Сохранение не найдено.", Ui.Bad); return; }
            StartSession(s);
            _hud.Message($"Загружено: {SaveSystem.Label(slot)}");
        }

        public void Save(string slot)
        {
            if (Session == null) return;
            SaveSystem.Save(Session, slot);
            _hud.Message($"Сохранено: {SaveSystem.Label(slot)}");
        }

        public string SaveInfo(string slot) => SaveSystem.Info(slot);

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------- Кадр ----------
        void Update()
        {
            float dt = Time.deltaTime;
            if (Session == null) { if (Current != UiScreen.Menu && Current != UiScreen.Settings) SetScreen(UiScreen.Menu); _audio.SetMusic("menu"); _audio.SetAmbient(); return; }
            var st = Session.State;

            // Автомат экранов, управляемый состоянием симуляции.
            if (st.GameOver) { if (Current != UiScreen.End) SetScreen(UiScreen.End); }
            else if (st.Narrative.Active != null) { if (Current != UiScreen.Dialogue) SetScreen(UiScreen.Dialogue); else _screens.RefreshDialogue(); }
            else if (st.Narrative.ChronicleOpen) { if (Current != UiScreen.Chronicle) SetScreen(UiScreen.Chronicle); }
            else if (Current == UiScreen.Dialogue) SetScreen(UiScreen.World);

            HandleGlobalKeys();
            if (Session == null) return;

            var hero = Session.HeroUnit;
            if (Current == UiScreen.World)
            {
                HandleWorldInput(dt);
                if (FastForward && Session.State.Hero.CombatTimer > 0) { FastForward = false; _hud.Notice("Бой — обычный ход времени", 2f); }
                int speed = FastForward ? 5 : 1;
                _acc += dt * speed;
                int guard = 0;
                while (_acc >= Session.Dt && guard++ < 8 * speed)
                {
                    Session.Tick();
                    _acc -= Session.Dt;
                    ProcessEvents();
                    if (Current != UiScreen.World || Session == null) break;
                }
                if (_acc > Session.Dt * 8 * speed) _acc = 0;
                _autosaveTimer += dt;
                if (_autosaveTimer > 180f && Session.State.Hero.CombatTimer <= 0) { _autosaveTimer = 0; SaveSystem.Save(Session, "auto"); }
            }
            if (Session == null) return;
            float alpha = Current == UiScreen.World ? Mathf.Clamp01(_acc / Session.Dt) : 1f;
            hero = Session.HeroUnit;
            var heroPos = hero != null ? Vec2.Lerp(hero.PrevPos, hero.Pos, alpha) : Vec2.Zero;
            var heroWorld = _world.Ground(heroPos);

            bool smoke = st.Hero.InSmoke;
            Shader.SetGlobalVector(Look.HeroPosId, heroWorld);
            Shader.SetGlobalFloat(Look.VisionId, smoke ? Session.Balance.vision.smokeRadius : Session.Balance.vision.radius + 4f);
            Shader.SetGlobalFloat(Look.FogDarkId, smoke ? 0.88f : 0.5f);
            Shader.SetGlobalColor(Look.SeasonId, SeasonTint(Core.Time.WorldClock.Month(Session)));

            _audio.SetListener(heroWorld);
            UpdateAudio(dt, hero, heroWorld);
            _cam.Update(heroWorld + Vector3.up * 1f, Current == UiScreen.World && !_screens.IsOverUi(_doc.rootVisualElement.panel, GameInput.MousePosition) ? GameInput.Scroll : 0f, dt);
            _world.Update(heroPos, dt);
            _units.Sync(alpha, dt);
            _markers.Update(dt);
            _ambience.Update(heroPos, heroWorld, dt);
            UpdateLabels(heroPos);
            _hud.Update(Session, _doc.rootVisualElement.panel, _cam.Cam, dt);
        }

        /// <summary>Времена года видны в мире: зимой холоднее и светлее, осенью — рыжее.</summary>
        static Color SeasonTint(int month)
        {
            Color winter = new Color(0.92f, 0.97f, 1.1f, 1), spring = new Color(0.97f, 1.05f, 0.95f, 1), summer = new Color(1.02f, 1f, 0.94f, 1), autumn = new Color(1.1f, 0.95f, 0.8f, 1);
            Color[] seq = { winter, winter, spring, spring, spring, summer, summer, summer, autumn, autumn, autumn, winter };
            return seq[Mathf.Clamp(month, 0, 11)];
        }

        // ---------- Звук по ситуации ----------
        void UpdateAudio(float dt, Unit hero, Vector3 heroWorld)
        {
            var st = Session.State;
            if (hero != null && Current == UiScreen.World && hero.Ai != AiState.Dead)
            {
                float moved = Vector3.Distance(new Vector3(heroWorld.x, 0, heroWorld.z), new Vector3(_lastHeroWorld.x, 0, _lastHeroWorld.z));
                if (moved < 3f) _stepDist += moved;
                if (_stepDist > (st.Hero.Mounted ? 2.4f : 1.7f)) { _stepDist = 0; _audio.Play(st.Hero.Mounted ? "hoof" : "footstep", heroWorld, 0.5f); }
            }
            _lastHeroWorld = heroWorld;

            _musicTimer -= dt;
            if (_musicTimer > 0) return;
            _musicTimer = 0.5f;
            if (Current == UiScreen.Menu || Current == UiScreen.Settings || Current == UiScreen.End) { _audio.SetMusic("menu"); return; }
            if (Current == UiScreen.Chronicle) { _audio.SetMusic("chronicle", "menu"); return; }
            string region = hero != null ? Session.Map.RegionAt(hero.Pos).id : null;
            bool fighting = false;
            if (hero != null && st.Hero.CombatTimer > 0)
                foreach (var u in st.Units)
                    if (u.Ai != AiState.Dead && Session.IsHostile(Teams.Player, u.Team) && Vec2.Distance(u.Pos, hero.Pos) < 20f) { fighting = true; break; }
            if (st.Hero.InSmoke) { _audio.SetMusic("smoke"); _audio.SetAmbient("smoke"); return; }
            if (fighting) _audio.SetMusic("combat");
            else _audio.SetMusic("region:" + region, "era:" + Session.PlayerEra, "world");
            _audio.SetAmbient("region:" + region, "wind");
        }

        void Sfx(string key, Vec2 pos, float volume = 1f, string fallback = null) => _audio.Play(key, _world.Ground(pos, 1f), volume, fallback);

        void HandleGlobalKeys()
        {
            if (GameInput.Down(GameInput.Key.F5)) Save("1");
            if (GameInput.Down(GameInput.Key.F9) && SaveSystem.Info("1") != null) { Load("1"); return; }
            if (Current == UiScreen.Dialogue) { int d = GameInput.DigitDown(); if (d > 0) _screens.ChooseByNumber(d); return; }
            if (Current == UiScreen.End || Current == UiScreen.Chronicle) return;
            if (GameInput.Down(GameInput.Key.Escape)) SetScreen(Current == UiScreen.World ? UiScreen.Menu : UiScreen.World);
            Toggle(GameInput.Key.M, UiScreen.Map);
            Toggle(GameInput.Key.Tab, UiScreen.Ruler);
            Toggle(GameInput.Key.I, UiScreen.Inventory);
            Toggle(GameInput.Key.C, UiScreen.Craft);
            Toggle(GameInput.Key.J, UiScreen.Journal);
            if (Current == UiScreen.World && GameInput.Down(GameInput.Key.T))
            {
                FastForward = !FastForward;
                _hud.Notice(FastForward ? "Время ускорено ×5 (T — обычный ход)" : "Обычный ход времени", 2f);
            }
        }

        void Toggle(GameInput.Key key, UiScreen screen)
        {
            if (!GameInput.Down(key)) return;
            if (Current == screen) SetScreen(UiScreen.World);
            else if (Current == UiScreen.World || Current == UiScreen.Map || Current == UiScreen.Ruler || Current == UiScreen.Inventory || Current == UiScreen.Craft || Current == UiScreen.Journal) SetScreen(screen);
        }

        // ---------- Ввод в мире ----------
        void HandleWorldInput(float dt)
        {
            var hero = Session.HeroUnit;
            if (hero == null) return;
            var mouse = GameInput.MousePosition;
            bool overUi = _screens.IsOverUi(_doc.rootVisualElement.panel, mouse);
            _cursorValid = CursorPoint(mouse, hero, out _cursorWorld);
            var cursor = Look.ToCore(_cursorWorld);

            var move = GameInput.MoveAxis();
            if ((move - _lastMove).sqrMagnitude > 0.001f) { Session.Execute(new SetMoveInput(new Vec2(move.x, move.y))); _lastMove = move; }

            if (!overUi && _cursorValid)
            {
                var pick = Picker.Pick(Session, cursor);
                var h = _doc.rootVisualElement.panel;
                var pp = h != null ? RuntimePanelUtils.ScreenToPanel(h, new Vector2(mouse.x, UnityEngine.Screen.height - mouse.y)) : Vector2.zero;
                _hud.SetHover(pick.Label, pp);
                _markers.Hover(pick.Command != null ? pick.Pos : (Vec2?)null, pick.Radius, HoverColor(pick.Kind));
                if (GameInput.LeftDown)
                {
                    var cmd = pick.Command;
                    if (cmd != null) { Session.Execute(cmd); _holdMove = false; }
                    else { Session.Execute(new MoveTo(cursor)); _holdMove = true; _holdTimer = 0; _markers.Click(cursor); }
                }
                else if (GameInput.LeftHeld && _holdMove)
                {
                    _holdTimer += dt;
                    if (_holdTimer > 0.15f) { _holdTimer = 0; Session.Execute(new MoveTo(cursor)); }
                }
                for (int slot = 1; slot <= 4; slot++)
                    if (GameInput.Down(GameInput.Key.Alpha1 + (slot - 1))) Session.Execute(new UseAbility(slot, cursor));
                if (GameInput.Down(GameInput.Key.Space))
                {
                    var dir = move.sqrMagnitude > 0.01f ? new Vec2(move.x, move.y) : (cursor - hero.Pos);
                    Session.Execute(new Dash(dir));
                }
            }
            else { _hud.SetHover(null, Vector2.zero); _markers.Hover(null, 0, Color.clear); }
            if (!GameInput.LeftHeld) _holdMove = false;

            if (GameInput.Down(GameInput.Key.R)) Session.Execute(new ToggleMount());
            if (GameInput.Down(GameInput.Key.F))
            {
                if (Session.State.PlayerNationId == null) Session.Execute(new FoundCamp());
                else Session.Execute(new DepositAll());
            }
            ProcessEvents();
        }

        static Color HoverColor(string kind) => kind switch
        {
            "enemy" => new Color(1f, 0.25f, 0.2f, 0.7f), "npc" => new Color(1f, 1f, 1f, 0.6f), "node" => new Color(0.5f, 1f, 0.5f, 0.6f),
            "loot" => new Color(1f, 0.9f, 0.4f, 0.7f), _ => new Color(1f, 0.85f, 0.4f, 0.55f)
        };

        bool CursorPoint(Vector2 mouse, Unit hero, out Vector3 point)
        {
            float y = _world.GroundHeight(hero.Pos);
            if (!_cam.GroundPoint(mouse, y, out point)) return false;
            // Одно уточнение по рельефу.
            float y2 = _world.GroundHeight(Look.ToCore(point));
            if (Mathf.Abs(y2 - y) > 0.3f && _cam.GroundPoint(mouse, y2, out var p2)) point = p2;
            return true;
        }

        // ---------- События симуляции ----------
        void ProcessEvents()
        {
            if (Session == null) return;
            var events = Session.Events;
            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                switch (e.Type)
                {
                    case GameEventType.Hit:
                        _units.OnHit(e.UnitId);
                        var hp = _units.UnitWorldPos(e.UnitId, 0.2f);
                        var tu = Session.FindUnit(e.UnitId);
                        bool ours = tu != null && tu.Team == Teams.Player;
                        Sfx(ours ? "hit_hero" : "hit", e.Pos);
                        if (tu != null && tu.Kind == UnitKind.Hero) _cam.Shake(Mathf.Clamp(e.Value / 40f, 0.1f, 0.6f));
                        else if (e.Crit) _cam.Shake(0.15f);
                        if (hp.HasValue && GameSettings.DamageNumbers) _hud.Floating(hp.Value, $"{e.Value:0}", e.Crit ? new Color(1f, 0.85f, 0.2f) : tu != null && tu.Team == Teams.Player ? new Color(1f, 0.35f, 0.3f) : Color.white, e.Crit ? 24 : 18);
                        break;
                    case GameEventType.Death:
                        _units.Burst("death", _world.Ground(e.Pos, 0.8f), 1f, new Color(0.9f, 0.9f, 0.9f));
                        Sfx("death", e.Pos);
                        break;
                    case GameEventType.Pickup:
                        _hud.Floating(_world.Ground(e.Pos, 1.5f), e.Text, e.Id == "essence" ? Ui.Mystic : Ui.Good, 16);
                        Sfx(e.Id == "essence" ? "essence" : "pickup", e.Pos);
                        break;
                    case GameEventType.Gathered:
                        _world.OnGathered((int)e.Value);
                        Sfx("gather", e.Pos);
                        _hud.Floating(_world.Ground(e.Pos, 1.5f), e.Text, Ui.Good, 15);
                        break;
                    case GameEventType.Message: _hud.Message(e.Text); break;
                    case GameEventType.AbilityCast:
                        if (e.Id == "explosion")
                        {
                            var col = e.Text != null && Content.Abilities.TryGetValue(e.Text, out var ad) ? Look.Hex(ad.color, new Color(1f, 0.5f, 0.2f)) : new Color(1f, 0.5f, 0.2f);
                            Sfx("explosion_" + e.Text, e.Pos, 1f, "explosion");
                            if (Session.HeroUnit != null && Vec2.Distance(e.Pos, Session.HeroUnit.Pos) < 16f) _cam.Shake(0.35f);
                            _units.Burst(e.Text != null && ViewFactory.TrySpawnFx(transform, "explosion_" + e.Text, _world.Ground(e.Pos, 0.3f), Vector3.zero, e.Value) ? "__none" : "explosion", _world.Ground(e.Pos, 0.6f), e.Value, col, 0.4f);
                        }
                        else
                        {
                            var caster = Session.FindUnit(e.UnitId);
                            if (caster != null && Content.Abilities.TryGetValue(e.Id ?? "", out var a))
                            {
                                _units.OnStrike(caster.Id, a.mystic || a.type == "buff" || a.type == "area");
                                Sfx("ability:" + a.id, caster.Pos, 1f, a.id == "dash" ? "dash" : a.mystic || a.type == "buff" || a.type == "area" ? "cast" : a.type == "projectile" ? "shoot" : "swing");
                                if (a.type == "arc" || a.type == "buff")
                                    _units.Slash("slash_" + a.id, _world.Ground(caster.Pos), new Vector3(caster.Facing.x, 0, caster.Facing.y), a.type == "buff" ? 2.5f : a.range, Look.Hex(a.color, Color.white));
                            }
                        }
                        break;
                    case GameEventType.EraChanged: _hud.Notice($"Новая эпоха: {e.Text}", 7f); _world.Bind(Session); _audio.Play("era"); break;
                    case GameEventType.StageChanged: _hud.Notice($"Поселение выросло: {e.Text}", 5f); _audio.Play("objective"); break;
                    case GameEventType.TimeJumped:
                        _audio.Play("timejump");
                        _world.Bind(Session); _units.Bind(Session); _markers.Bind(Session); _ambience.Bind(Session);
                        if (Session.HeroUnit != null) _cam.Snap(_world.Ground(Session.HeroUnit.Pos));
                        break;
                    case GameEventType.HeroDefeated: _hud.Notice("Вы пали… и уснули на десятилетия.", 6f); _audio.Play("death"); _cam.Shake(0.8f); break;
                    case GameEventType.Discovered: _hud.Message($"Открыто место: {e.Text}", Ui.Gold); break;
                    case GameEventType.CompanionDied: _hud.Message($"Умер спутник: {e.Text}", Ui.Bad); _audio.Play("sad"); break;
                    case GameEventType.TechResearched: _hud.Notice($"Открытие: {e.Text}", 3f); break;
                    case GameEventType.BattleStarted: _hud.Notice($"Битва! ({e.Text})", 3f); break;
                    case GameEventType.ZoneEntered: _hud.Notice("Дым", 2f); _audio.Play("zone_enter"); break;
                    case GameEventType.Autosave: SaveSystem.Save(Session, "auto"); break;
                    case GameEventType.Rejuvenated: _audio.Play("essence"); _units.Burst("rejuvenate", _world.Ground(Session.HeroUnit.Pos, 1f), 2.5f, Ui.Mystic, 0.8f); break;
                    case GameEventType.Strike: _units.OnStrike(e.UnitId, false); Sfx(e.Id == "ranged" ? "shoot" : "strike", e.Pos, 0.8f); break;
                    case GameEventType.WindupStarted: Sfx("windup", e.Pos, 0.5f); break;
                    case GameEventType.Aggro:
                        var ap = _units.UnitWorldPos(e.UnitId, 0.5f);
                        if (ap.HasValue) _hud.Floating(ap.Value, "!", new Color(1f, 0.3f, 0.2f), 30);
                        Sfx("aggro", e.Pos, 0.6f, "windup");
                        break;
                    case GameEventType.Recruited: _audio.Play("recruit"); break;
                    case GameEventType.BuildingBuilt: if (Session.HeroUnit != null && Vec2.Distance(e.Pos, Session.HeroUnit.Pos) < 60f) Sfx("build", e.Pos); break;
                    case GameEventType.Crafted: _audio.Play("build"); break;
                    case GameEventType.DialogueStarted: _audio.Play("dialogue"); break;
                    case GameEventType.LevelUp: if (Session.HeroUnit != null) _units.Burst("levelup", _world.Ground(Session.HeroUnit.Pos, 1f), 1.5f, Ui.Gold, 0.6f); _hud.Message(e.Text, Ui.Gold); break;
                    case GameEventType.ObjectiveCompleted: _hud.Notice($"Цель выполнена: {e.Text}", 4f); _audio.Play("objective"); break;
                    case GameEventType.OpenScreen: OpenFromWorld(e); break;
                }
            }
            events.Clear();
        }

        void OpenFromWorld(GameEvent e)
        {
            switch (e.Id)
            {
                case "ruler": _screens.RulerCityId = e.Text; _screens.RulerTab = "city"; SetScreen(UiScreen.Ruler); break;
                case "craft_workshop": SetScreen(UiScreen.Craft); break;
                case "persuade": _screens.RulerTab = "diplo"; SetScreen(UiScreen.Ruler); break;
                case "interior":
                    var parts = e.Text.Split('|');
                    if (Content.Buildings.TryGetValue(parts[0], out var d))
                    {
                        var city = Session.City(parts.Length > 1 ? parts[1] : null);
                        _screens.InteriorText = $"{d.name} — {city?.Name}\n\n{d.description}\n\n" + InteriorServices(d.id);
                        SetScreen(UiScreen.Interior);
                    }
                    break;
            }
        }

        static string InteriorServices(string id) => id switch
        {
            "school" => "Писцы ведут счёт и хроники. Назначайте спутников учёными (Tab → Люди), чтобы ускорить открытия.",
            "shrine" => "Жрецы слушают дым. Здесь говорят, что голоса в дыму повторяют одни и те же слова.",
            "laboratory" => "Учёные изучают мистический ресурс. Отсюда начинается путь к последним технологиям.",
            "palace" => "Тронный зал. Отсюда правят царством (Tab).",
            "settlement_center" => "Сказители, площадь, общий котёл. Люди помнят ваших старых друзей.",
            "market" => "Торг и слухи со всего мира.",
            _ => "Люди заняты своими делами."
        };

        void UpdateLabels(Vec2 hero)
        {
            var panel = _doc.rootVisualElement.panel;
            _hud.BeginLabels();
            if (Current == UiScreen.World || Current == UiScreen.Dialogue)
            {
                foreach (var c in Session.State.Cities)
                {
                    if (Vec2.Distance(c.Pos, hero) > 90f || !Core.World.Vision.IsExplored(Session.State, Session.Map.TileAt(c.Pos))) continue;
                    var n = Session.Nation(c.NationId);
                    _hud.WorldLabel(panel, _cam.Cam, "city" + c.Id, _world.Ground(c.Pos, 6f), $"{c.Name} · {Core.Civilization.CitySystem.Stage(Session, c).name}", n != null ? Look.Hex(n.Color) : Color.white, 16);
                }
                foreach (var u in Session.State.Units)
                {
                    if (u.Kind != UnitKind.Npc && u.Kind != UnitKind.Leader) continue;
                    if (Vec2.Distance(u.Pos, hero) > 16f || u.InSmoke != Session.State.Hero.InSmoke) continue;
                    _hud.WorldLabel(panel, _cam.Cam, "u" + u.Id, _world.Ground(u.Pos, 2.8f), u.Name, u.Kind == UnitKind.Leader ? Ui.Gold : u.Team == Teams.Wild ? Ui.Bad : Ui.Text, 14);
                }
                var hs = Session.State.Hero;
                if (hs.Pending != null && hs.Pending.Kind == "gather" && hs.GatherProgress > 0 && Session.HeroUnit != null)
                {
                    float k = Mathf.Clamp01(hs.GatherProgress / Session.Balance.hero.gatherTime);
                    _hud.WorldLabel(panel, _cam.Cam, "gather", _world.Ground(Session.HeroUnit.Pos, 2.9f), "Сбор " + new string('▮', (int)(k * 10)) + new string('▯', 10 - (int)(k * 10)), Ui.Good, 14);
                }
                foreach (var p in Session.State.Pois)
                {
                    if (!p.Discovered || Vec2.Distance(p.Pos, hero) > 28f) continue;
                    _hud.WorldLabel(panel, _cam.Cam, "p" + p.Id, _world.Ground(p.Pos, 3.5f), p.Name + (p.Cleared && p.DefId != "grave" ? " (пусто)" : ""), Ui.Dim, 13);
                }
            }
            _hud.EndLabels();
        }

        /// <summary>Для сценариев снимков.</summary>
        public void ForceScreen(UiScreen s) => SetScreen(s);
        public Hud HudView => _hud;
    }

    /// <summary>Сохранения: GameState целиком в JSON в persistentDataPath, несколько слотов и автосохранение.</summary>
    public static class SaveSystem
    {
        static string PathFor(string slot) => System.IO.Path.Combine(Application.persistentDataPath, $"immortal_{slot}.json");
        public static string Label(string slot) => slot == "auto" ? "автосохранение" : $"слот {slot}";

        public static void Save(GameSession s, string slot)
        {
            try
            {
                File.WriteAllText(PathFor(slot), SaveLoad.Serialize(s));
                var h = s.State.Hero;
                File.WriteAllText(PathFor(slot) + ".meta", $"{s.Year} г., {Core.Character.HeroSystem.RoleName(h.Role)}, глава {s.State.Chapter}");
            }
            catch (Exception e) { Debug.LogError("Не удалось сохранить: " + e.Message); }
        }

        public static GameSession Load(ContentDatabase db, string slot)
        {
            var p = PathFor(slot);
            if (!File.Exists(p)) return null;
            try { return SaveLoad.Load(db, File.ReadAllText(p)); }
            catch (Exception e) { Debug.LogError("Не удалось загрузить: " + e.Message); return null; }
        }

        public static string Info(string slot)
        {
            var p = PathFor(slot);
            if (!File.Exists(p)) return null;
            string meta = File.Exists(p + ".meta") ? File.ReadAllText(p + ".meta") + " · " : "";
            return $"({meta}{File.GetLastWriteTime(p):dd.MM HH:mm})";
        }
    }
}
