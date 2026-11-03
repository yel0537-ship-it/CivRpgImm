using System;
using System.Collections;
using System.IO;
using System.Linq;
using Immortal.Core;
using Immortal.Core.Action;
using Immortal.Core.Character;
using Immortal.Core.Commands;
using Immortal.Core.Narrative;
using Immortal.Core.Time;
using Immortal.Core.World;
using UnityEngine;
using UiScreen = Immortal.Presentation.UI.Screen;

namespace Immortal.Presentation
{
    /// <summary>
    /// Сценарии снимков для tools/screenshots: Standalone-сборка запускается с «-scenario имя -shot путь.png»
    /// (или «-scenario all -shotdir папка»), ставит игру в нужное состояние, сохраняет PNG и выходит.
    /// </summary>
    public sealed class ScreenshotRunner : MonoBehaviour
    {
        public static readonly string[] All = { "menu", "hud", "dialogue", "combat", "smoke", "map", "ruler", "city", "chronicle", "end" };
        GameRoot _root;
        string _scenario, _shot, _dir;

        public static void TryStart(GameRoot root)
        {
            var args = Environment.GetCommandLineArgs();
            string Arg(string name) { int i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
            var scenario = Arg("-scenario");
            if (scenario == null) return;
            var r = root.gameObject.AddComponent<ScreenshotRunner>();
            r._root = root; r._scenario = scenario; r._shot = Arg("-shot"); r._dir = Arg("-shotdir") ?? "Screenshots";
        }

        IEnumerator Start()
        {
            yield return null;
            var list = _scenario == "all" ? All : new[] { _scenario };
            foreach (var sc in list)
            {
                Setup(sc);
                for (int i = 0; i < 45; i++) yield return null;
                string path = _scenario == "all" || _shot == null ? Path.Combine(_dir, sc + ".png") : _shot;
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                ScreenCapture.CaptureScreenshot(path);
                for (int i = 0; i < 10; i++) yield return null;
                Debug.Log($"[Снимок] {sc} → {path}");
            }
            Application.Quit();
        }

        GameSession Fresh(int seed = 7)
        {
            var s = GameSession.NewGame(_root.Content, seed);
            DialogueEngine.AutoResolve(s);
            _root.StartSession(s);
            for (int i = 0; i < 40; i++) s.Tick();
            return s;
        }

        static void Companions(GameSession s, int n)
        {
            var cap = s.Capital(s.PlayerNation);
            foreach (var npc in s.State.Npcs.Take(n).ToList()) RecruitmentSystem.Recruit(s, npc, 30);
            if (cap != null) cap.Population += 4;
        }

        void Setup(string sc)
        {
            switch (sc)
            {
                case "menu": _root.ForceScreen(UiScreen.Menu); break;
                case "hud":
                {
                    var s = Fresh();
                    var item = LootSystem.RollItem(s, "club", "settlement"); s.State.Hero.Items.Add(item); LootSystem.Equip(s, item.Id);
                    s.Message("Добро пожаловать в долину Истока.");
                    break;
                }
                case "dialogue":
                {
                    var s = Fresh();
                    var npc = s.State.Npcs.OrderBy(n => Vec2.Distance(n.Pos, s.HeroUnit.Pos)).First();
                    s.HeroUnit.Pos = s.Map.NearestWalkable(npc.Pos + new Vec2(2, 0)); s.HeroUnit.PrevPos = s.HeroUnit.Pos;
                    for (int i = 0; i < 20; i++) s.Tick();
                    DialogueEngine.Start(s, "dlg_recruit", new DialogueState { NpcId = npc.Id });
                    break;
                }
                case "combat":
                {
                    var s = Fresh();
                    var hero = s.HeroUnit;
                    for (int i = 0; i < 4; i++) { var u = SpawnSystem.CreateEnemy(s, i == 3 ? "bandit_archer" : "wolf", hero.Pos + Vec2.FromAngle(i * 1.6f) * 5f, Teams.Wild); u.Ai = AiState.Chase; u.TargetId = hero.Id; }
                    for (int i = 0; i < 25; i++) s.Tick();
                    break;
                }
                case "smoke":
                {
                    var s = Fresh();
                    s.Execute(new FoundCamp()); DialogueEngine.AutoResolve(s);
                    s.PlayerNation.EraId = "city_states";
                    var z = s.State.Pois.Where(p => p.DefId == "zone").OrderBy(p => Vec2.Distance(p.Pos, s.HeroUnit.Pos)).First();
                    s.HeroUnit.Pos = s.Map.NearestWalkable(z.Pos); s.HeroUnit.PrevPos = s.HeroUnit.Pos;
                    z.Discovered = true;
                    for (int i = 0; i < 120; i++) { s.Tick(); if (s.State.Narrative.Active != null) DialogueEngine.AutoResolve(s); }
                    _root.StartSession(s);
                    break;
                }
                case "map":
                {
                    var s = Fresh();
                    Vision.Reveal(s.State, s.Map, s.HeroUnit.Pos, 220f);
                    foreach (var p in s.State.Pois) if (Vec2.Distance(p.Pos, s.HeroUnit.Pos) < 220f) p.Discovered = true;
                    _root.ForceScreen(UiScreen.Map);
                    break;
                }
                case "ruler":
                {
                    var s = Fresh();
                    s.Execute(new FoundCamp()); DialogueEngine.AutoResolve(s);
                    Companions(s, 4);
                    s.PlayerNation.Add("wood", 60); s.PlayerNation.Add("stone", 30);
                    _root.ForceScreen(UiScreen.Ruler);
                    break;
                }
                case "city":
                case "chronicle":
                {
                    var s = Fresh(11);
                    s.Execute(new FoundCamp()); DialogueEngine.AutoResolve(s);
                    Companions(s, 6);
                    var cap = s.Capital(s.PlayerNation);
                    s.Execute(new AssignCompanion(s.State.Companions[0].Id, CompanionRoles.Governor, cap.Id));
                    s.PlayerNation.Add("wood", 200); s.PlayerNation.Add("stone", 150);
                    s.State.Narrative.Flags.Add("true_immortality");
                    s.State.Hero.Role = Roles.Chief;
                    s.Execute(new TimeJump(60));
                    DialogueEngine.AutoResolve(s);
                    if (sc == "city") { s.Execute(new CloseChronicle()); Vision.Reveal(s.State, s.Map, cap.Pos, 60f); }
                    _root.StartSession(s);
                    if (sc == "chronicle") _root.ForceScreen(UiScreen.Chronicle);
                    break;
                }
                case "end":
                {
                    var s = Fresh();
                    Core.Civilization.EndingSystem.Trigger(s, "awakening");
                    break;
                }
            }
        }
    }
}
