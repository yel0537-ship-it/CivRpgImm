using System;
using System.Collections.Generic;
using System.Linq;
using Immortal.Core;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using Immortal.Core.Commands;
using Immortal.Core.Narrative;
using NUnit.Framework;

namespace Immortal.Tests
{
    /// <summary>
    /// Бот проходит игру от новой партии до концовки только через команды и тики:
    /// сбор, ремесло, лагерь, вербовка, стройка, наместник, поручения, прыжки во времени, мегасооружение.
    /// </summary>
    public class ScriptedPlaythroughTest
    {
        sealed class Bot
        {
            readonly GameSession s;
            public readonly List<string> Log = new List<string>();
            public Bot(GameSession s) { this.s = s; }
            Unit Hero => s.HeroUnit;
            public void Note(string t) => Log.Add($"[{s.Year} г.] {t}");

            public void Run(int ticks) => TestUtil.Ticks(s, ticks);

            bool WalkTo(Vec2 p, float near, float maxSec = 60)
            {
                s.Execute(new MoveTo(p));
                return TestUtil.TickUntil(s, () => Vec2.Distance(Hero.Pos, p) < near || (Hero.Path == null && Vec2.Distance(Hero.Pos, p) < near + 4) || s.State.GameOver,
                    TestUtil.Seconds(s, maxSec));
            }

            int NearestNode(string item)
            {
                s.Map.TileXY(Hero.Pos, out int cx, out int cy);
                int best = -1; float bd = float.MaxValue;
                for (int dy = -30; dy <= 30; dy++)
                for (int dx = -30; dx <= 30; dx++)
                {
                    if (!s.Map.InBounds(cx + dx, cy + dy)) continue;
                    int t = s.Map.TileIndex(cx + dx, cy + dy);
                    var n = s.Map.NodeAt(t);
                    if (n == null || !n.yields.ContainsKey(item)) continue;
                    float d = dx * dx + dy * dy;
                    if (d < bd) { bd = d; best = t; }
                }
                return best;
            }

            public void Gather(string item, int amount)
            {
                for (int i = 0; i < 60 && s.State.Hero.Count(item) < amount && !s.State.GameOver; i++)
                {
                    int t = NearestNode(item);
                    if (t < 0) { WalkTo(Hero.Pos + Vec2.FromAngle(i) * 40, 3, 15); continue; }
                    int before = s.State.Hero.Count(item);
                    s.Execute(Interact.Gather(t));
                    TestUtil.TickUntil(s, () => s.State.Hero.Count(item) > before || s.State.Hero.Pending == null, TestUtil.Seconds(s, 25));
                }
            }

            public bool Recruit(NpcState npc, int attempts = 8)
            {
                for (int a = 0; a < attempts && !npc.Recruited && !npc.Hostile && npc.Alive && !s.State.GameOver; a++)
                {
                    if (s.State.Months < npc.CooldownUntilMonths)
                        TestUtil.TickUntil(s, () => s.State.Months >= npc.CooldownUntilMonths, TestUtil.Seconds(s, 200));
                    if (s.State.Hero.Count("berries") < 5) Gather("berries", 10);
                    WalkTo(npc.Pos, 3, 90);
                    TestUtil.TickUntil(s, () => s.FindUnit(npc.UnitId) != null, 40);
                    var u = s.FindUnit(npc.UnitId);
                    if (u == null) continue;
                    s.Execute(Interact.Unit(u.Id));
                    TestUtil.TickUntil(s, () => s.State.Narrative.Active != null, TestUtil.Seconds(s, 20), closeDialogs: false);
                    if (s.State.Narrative.Active == null) continue;
                    var opts = DialogueEngine.Options(s);
                    var pick = opts.FirstOrDefault(o => o.Enabled && o.Text.StartsWith("Предложить дар"))
                               ?? opts.FirstOrDefault(o => o.Enabled && o.Text.StartsWith("Я сделал"))
                               ?? opts.FirstOrDefault(o => o.Enabled && o.Text.StartsWith("Наплести"))
                               ?? opts.FirstOrDefault(o => o.Enabled && o.Text.StartsWith("Позвать"));
                    if (pick == null) { TestUtil.CloseAll(s); continue; }
                    s.Execute(new ChooseOption(pick.Index));
                    TestUtil.CloseAll(s);
                }
                if (npc.Recruited) Note($"завербован {npc.Name}");
                return npc.Recruited;
            }

            /// <summary>Охота в дыму за мистическим ресурсом.</summary>
            public void HuntInSmoke(float seconds)
            {
                var zone = s.State.Pois.Where(p => p.DefId == "zone").OrderBy(p => Vec2.Distance(p.Pos, Hero.Pos)).FirstOrDefault();
                if (zone == null || Hero.Hp < Hero.MaxHp * 0.8f) return;
                int defeats = s.State.Hero.Defeats;
                WalkTo(zone.Pos, zone.Radius * 0.5f, 150);
                int max = TestUtil.Seconds(s, seconds);
                for (int i = 0; i < max && s.State.Hero.Defeats == defeats && !s.State.GameOver; i++)
                {
                    if (Hero.Hp < Hero.MaxHp * 0.35f) { WalkTo(zone.Pos + (Hero.Pos - zone.Pos).Normalized * (zone.Radius + 25), 4, 30); break; }
                    if (i % 10 == 0)
                    {
                        var foe = s.State.Units.Where(u => u.Kind == UnitKind.Spirit && u.Ai != AiState.Dead).OrderBy(u => Vec2.Distance(u.Pos, Hero.Pos)).FirstOrDefault();
                        if (foe != null) { if (Hero.TargetId != foe.Id) s.Execute(new AttackUnit(foe.Id)); s.Execute(new UseAbility(1, foe.Pos)); s.Execute(new UseAbility(4, foe.Pos)); }
                        else if (Hero.Path == null) s.Execute(new MoveTo(zone.Pos + Vec2.FromAngle(i * 0.01f) * zone.Radius * 0.5f));
                    }
                    TestUtil.Ticks(s, 1);
                }
                TestUtil.Ticks(s, 40);
            }

            public void ToCapitalAndDeposit()
            {
                var cap = s.Capital(s.PlayerNation); if (cap == null) return;
                WalkTo(cap.Pos, 6, 120);
                s.Execute(new DepositAll());
            }
        }

        [Test]
        public void BotPlaysFromNewGameToEnding()
        {
            var s = TestUtil.NewGame(101);
            var bot = new Bot(s);
            try
            {
                // 1. Изгой: сбор и полевое ремесло.
                bot.Gather("wood", 6); bot.Gather("stone", 2);
                s.Execute(new Craft("club", false));
                Assert.IsNotNull(s.State.Hero.WeaponId, "дубина изготовлена в поле");
                bot.Gather("berries", 15);

                // 2. Лагерь и первые люди.
                s.Execute(new FoundCamp());
                TestUtil.CloseAll(s);
                Assert.IsNotNull(s.PlayerNation);
                var cap = s.Capital(s.PlayerNation);
                bot.Note("основан лагерь");
                var start = s.HeroUnit.Pos;
                foreach (var npc in s.State.Npcs.OrderBy(n => Vec2.Distance(n.Pos, start)).ToList())
                {
                    if (cap.Population >= 8 || Vec2.Distance(npc.Pos, start) > 160) break;
                    bot.Recruit(npc);
                }
                Assert.GreaterOrEqual(s.State.Companions.Count, 3, string.Join("\n", bot.Log));

                // 3. Первые постройки → деревня → роль вождя.
                for (int i = 0; i < 6 && cap.StageIndex < 1; i++)
                {
                    bot.Gather("wood", 30);
                    bot.ToCapitalAndDeposit();
                    foreach (var b in new[] { "hut", "field", "hut" }) s.Execute(new Build(cap.Id, b));
                    bot.Run(TestUtil.Seconds(s, 70));
                    bot.Note($"стройка: нас. {cap.Population:0.0}, построек {cap.Buildings.Count}");
                }
                Assert.GreaterOrEqual(cap.StageIndex, 1, "лагерь должен стать деревней\n" + string.Join("\n", bot.Log));
                Assert.AreEqual(Roles.Chief, s.State.Hero.Role);
                bot.Note("деревня, вождь");

                // 4. Правитель: наместник, приоритеты, поручения, прыжки во времени.
                s.Execute(new SetTechPriority("society"));
                for (int jump = 0; jump < 20 && s.State.EndingId == null; jump++)
                {
                    var pn = s.PlayerNation;
                    if (cap.NationId != pn.Id) cap = s.Capital(pn);
                    var alive = s.State.Companions.Where(c => c.Alive).ToList();
                    if (cap.GovernorId == null || !s.Companion(cap.GovernorId).Alive)
                    {
                        var gov = alive.FirstOrDefault(c => c.Role != CompanionRoles.Errand);
                        if (gov != null) s.Execute(new AssignCompanion(gov.Id, CompanionRoles.Governor, cap.Id));
                    }
                    foreach (var c in alive.Where(c => c.Id != cap.GovernorId && c.Role != CompanionRoles.Errand))
                        s.Execute(new AssignCompanion(c.Id, CompanionRoles.Errand, "essence"));
                    if (s.Content.EraOrder(pn.EraId) >= 2) s.Execute(new SetTechPriority("knowledge"));
                    if (s.State.Hero.VitalityYears < 25 && s.State.Hero.RejuvenationUnlocked) s.Execute(new Rejuvenate());

                    TestUtil.CloseAll(s);
                    TestUtil.TickUntil(s, () => s.State.Hero.CombatTimer <= 0, TestUtil.Seconds(s, 30));
                    s.Execute(new TimeJump(25));
                    TestUtil.CloseAll(s);
                    bot.Note($"прыжок: эпоха {pn.EraId}, {CitySystem.Stage(s, cap).name}, нас. {cap.Population:0}, техн. {pn.Researched.Count}, ресурс {HeroSystem.EssenceAvailable(s)}, спутн. {s.State.Companions.Count(c => c.Alive)}");

                    // Потомки рядом со столицей — новые спутники.
                    foreach (var d in s.State.Npcs.Where(n => n.IsDescendant && !n.Recruited && n.Alive && !n.Hostile).Take(5).ToList()) bot.Recruit(d, 2);
                    if (s.Content.EraOrder(pn.EraId) >= 1) bot.HuntInSmoke(180);
                    bot.ToCapitalAndDeposit();

                    if (pn.Researched.Contains("essence_alchemy") && !cap.HasBuilding("laboratory")) s.Execute(new Build(cap.Id, "laboratory"));
                    if (pn.Researched.Contains("eternity_formula")) s.Execute(new Build(cap.Id, "eternal_spire"));
                    if (s.State.GameOver) break;
                }
            }
            finally { TestContext.WriteLine(string.Join("\n", bot.Log)); }

            Assert.IsTrue(s.State.GameOver, "игра должна закончиться\n" + string.Join("\n", bot.Log));
            Assert.IsNotNull(s.State.EndingId);
            TestContext.WriteLine("Концовка: " + s.State.EndingId);
            Assert.AreNotEqual("old_age", s.State.EndingId, "бот должен дойти до настоящей концовки\n" + string.Join("\n", bot.Log));
        }
    }
}
