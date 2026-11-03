using System;
using System.Linq;
using Immortal.Core;
using Immortal.Core.Action;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using Immortal.Core.Commands;
using Immortal.Core.World;
using UnityEngine;
using UnityEngine.UIElements;

namespace Immortal.Presentation.UI
{
    /// <summary>
    /// Панель разработчика (F12, только в редакторе и Development Build): быстрые проверки без прохождения.
    /// Меняет состояние напрямую — для игры не предназначена.
    /// </summary>
    public sealed class DevPanel
    {
        readonly VisualElement _box;
        readonly Func<GameSession> _session;
        readonly Action _refreshWorld;

        public DevPanel(VisualElement root, Func<GameSession> session, Action refreshWorld)
        {
            _session = session; _refreshWorld = refreshWorld;
            _box = Ui.Box(new Color(0.08f, 0.02f, 0.02f, 0.92f), 8);
            Ui.Absolute(_box, left: 330, top: 16);
            _box.style.width = 520;
            Ui.Show(_box, false);
            root.Add(_box);
        }

        public static bool Allowed => Application.isEditor || Debug.isDebugBuild;
        public bool Visible => _box.style.display == DisplayStyle.Flex;

        public void Toggle()
        {
            if (!Allowed) return;
            bool show = !Visible;
            Ui.Show(_box, show);
            if (show) Build();
        }

        void Build()
        {
            _box.Clear();
            _box.Add(Ui.Label("Панель разработчика (F12)", 16, Ui.Bad, true));
            var s = _session();
            if (s == null) { _box.Add(Ui.Label("Нет партии.", 14)); return; }
            void B(string text, Action a) { var row = _box.Children().LastOrDefault() as VisualElement; _box.Add(Ui.Button(text, () => { a(); _refreshWorld(); s.Message("[dev] " + text); }, true, 13)); }
            var h = s.State.Hero;
            B("+100 всех ресурсов (герою и державе)", () =>
            {
                foreach (var r in new[] { "wood", "stone", "berries", "meat", "hide", "fiber", "ore", "bronze", "food" }) { h.Add(r, 100); s.PlayerNation?.Add(r, 100); }
            });
            B("+10 мистического ресурса", () => { h.Add("essence", 10); HeroSystem.OnEssencePicked(s); });
            B("Открыть всю карту", () =>
            {
                Vision.Reveal(s.State, s.Map, new Vec2(s.Map.WorldSize / 2, s.Map.WorldSize / 2), s.Map.WorldSize);
                foreach (var p in s.State.Pois) p.Discovered = true;
                s.FogRevision++; s.State.WorldMods.Revision++;
            });
            B("Бессмертие вкл/выкл", () => { if (!s.State.Narrative.Flags.Remove("true_immortality")) s.State.Narrative.Flags.Add("true_immortality"); });
            B("Полное здоровье и энергия", () => { var u = s.HeroUnit; if (u != null) u.Hp = u.MaxHp; h.Energy = HeroSystem.MaxEnergy(s); });
            B("+3 очка атрибутов, +20 ко всем навыкам", () => { h.AttributePoints += 3; foreach (var k in h.Skills.Keys.ToList()) h.Skills[k] = Mathf.Min(100, h.Skills[k] + 20); });
            B("Бронзовый меч и доспех (бронзовое качество)", () =>
            {
                foreach (var id in new[] { "bronze_sword", "bronze_armor" }) { var it = LootSystem.RollItem(s, id, "bronze"); h.Items.Add(it); LootSystem.Equip(s, it.Id); }
            });
            B("Основать лагерь (если нет) и +6 спутников", () =>
            {
                if (s.State.PlayerNationId == null) s.Execute(new FoundCamp());
                foreach (var npc in s.State.Npcs.Where(n => !n.Recruited && n.Alive).Take(6).ToList()) RecruitmentSystem.Recruit(s, npc, 30);
            });
            B("Столица: +40 жителей и следующая ступень", () =>
            {
                var cap = s.Capital(s.PlayerNation); if (cap == null) return;
                cap.Population += 40;
                if (cap.StageIndex + 1 < s.Content.Stages.Count)
                {
                    foreach (var b in s.Content.Stages[cap.StageIndex + 1].requiredBuildings) if (!cap.HasBuilding(b)) CitySystem.PlaceBuilding(s, cap, b, free: true);
                    s.PlayerNation.Add("wood", 200); s.PlayerNation.Add("stone", 200);
                    CitySystem.TryAdvanceStage(s, cap);
                }
            });
            B("Изучить все технологии текущей эпохи", () =>
            {
                var pn = s.PlayerNation; if (pn == null) return;
                foreach (var t in s.Content.TechFile.techs.Where(t => t.era == pn.EraId)) TechSystem.Complete(s, pn, t);
            });
            B("Заспавнить волков рядом", () => { var u = s.HeroUnit; for (int i = 0; i < 4; i++) SpawnSystem.CreateEnemy(s, "wolf", u.Pos + Vec2.FromAngle(i * 1.5f) * 9f, Teams.Wild); });
            B("Заспавнить медведя-босса", () => { var u = s.HeroUnit; SpawnSystem.CreateEnemy(s, "bear", u.Pos + new Vec2(8, 0), Teams.Wild); });
            B("Прыжок на 25 лет", () => s.Execute(new TimeJump(25)));
            B("Телепорт в столицу соперника", () =>
            {
                var rival = s.State.Nations.FirstOrDefault(n => !n.IsPlayer && !n.Destroyed);
                var cap = s.Capital(rival); var u = s.HeroUnit;
                if (cap != null && u != null) { u.Pos = s.Map.NearestWalkable(cap.Pos + new Vec2(0, -10)); u.PrevPos = u.Pos; Vision.Reveal(s.State, s.Map, u.Pos, 40); s.FogRevision++; }
            });
            B("Телепорт в ближайший дым", () =>
            {
                var u = s.HeroUnit;
                var z = s.State.Pois.Where(p => p.DefId == "zone").OrderBy(p => Vec2.Distance(p.Pos, u.Pos)).FirstOrDefault();
                if (z != null) { u.Pos = s.Map.NearestWalkable(z.Pos); u.PrevPos = u.Pos; z.Discovered = true; }
            });
        }
    }
}
