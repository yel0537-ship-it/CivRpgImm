using System.Linq;
using Immortal.Core;
using Immortal.Core.Action;
using Immortal.Core.Character;
using Immortal.Core.Commands;
using Immortal.Core.Narrative;
using NUnit.Framework;

namespace Immortal.Tests
{
    public class RecruitmentTests
    {
        static NpcState Valley(GameSession s, string id) => s.State.Npcs.First(n => n.RecruitDefId == id);

        [TestCase(0.01f, RecruitOutcome.Full)]
        [TestCase(0.5f, RecruitOutcome.Partial)]
        [TestCase(0.85f, RecruitOutcome.Fail)]
        [TestCase(0.99f, RecruitOutcome.Crit)]
        public void AllOutcomesAreReachable(float roll, RecruitOutcome expected)
        {
            var s = TestUtil.NewGame(11);
            var npc = Valley(s, "rec_gorn"); // недоверчивый: шанс в середине шкалы, достижимы все четыре полосы
            s.State.Hero.Add("berries", 10);
            float chance = RecruitmentSystem.Chance(s, npc, "gift");
            Assert.That(chance, Is.InRange(0.2f, 0.8f));
            // Подбираем бросок относительно реального шанса, чтобы попасть в нужную полосу исходов.
            float r = expected switch
            {
                RecruitOutcome.Full => 0.0f,
                RecruitOutcome.Partial => chance - 0.05f,
                RecruitOutcome.Fail => chance + (0.93f - chance) * 0.5f,
                _ => 0.995f
            };
            s.Rng = new ScriptedRandom(r);
            var outcome = RecruitmentSystem.Resolve(s, npc, "gift", out _);
            Assert.AreEqual(expected, outcome, $"шанс {chance}, бросок {r}");
            switch (expected)
            {
                case RecruitOutcome.Full: Assert.IsTrue(npc.Recruited); Assert.Greater(s.State.Companions[0].Relation, 0); break;
                case RecruitOutcome.Partial: Assert.IsTrue(npc.Recruited); Assert.Less(s.State.Companions[0].Relation, 0); break;
                case RecruitOutcome.Fail: Assert.IsFalse(npc.Recruited); Assert.Greater(npc.CooldownUntilMonths, s.State.Months); break;
                case RecruitOutcome.Crit: Assert.IsTrue(npc.Hostile); Assert.Less(s.State.Hero.Reputation, 0); break;
            }
        }

        [Test]
        public void ChanceDependsOnInvestmentTraitsAndReputation()
        {
            var s = TestUtil.NewGame(11);
            var npc = Valley(s, "rec_ayna");
            float noGift = RecruitmentSystem.Chance(s, npc, "gift");
            s.State.Hero.Add("hide", 10);
            float withNeed = RecruitmentSystem.Chance(s, npc, "gift");
            Assert.Greater(withNeed, noGift);
            float before = RecruitmentSystem.Chance(s, npc, "trick");
            RecruitmentSystem.Observe(s, npc);
            Assert.Greater(RecruitmentSystem.Chance(s, npc, "trick"), before, "наблюдение раскрывает черты и помогает");
            s.State.Hero.Reputation = 50;
            Assert.Greater(RecruitmentSystem.Chance(s, npc, "trick"), before);
            Assert.Less(RecruitmentSystem.Chance(s, npc, "pressure"), RecruitmentSystem.Chance(s, Valley(s, "rec_dag"), "pressure"), "гордую сложнее продавить, чем робкого");
        }

        [Test]
        public void RecruitDialogueRunsThroughEngine()
        {
            var s = TestUtil.NewGame(11);
            var npc = Valley(s, "rec_tor");
            s.State.Hero.Add("berries", 10);
            s.Rng = new ScriptedRandom(0.0f);
            DialogueEngine.Start(s, "dlg_recruit", new DialogueState { NpcId = npc.Id });
            var gift = DialogueEngine.Options(s).First(o => o.Text.StartsWith("Предложить дар"));
            Assert.IsTrue(gift.Enabled);
            s.Execute(new ChooseOption(gift.Index));
            Assert.IsTrue(npc.Recruited);
            Assert.AreEqual("full", s.State.Narrative.Active.NodeId);
        }

        [Test]
        public void HelpQuestCompletesAndUnlocksHelpOption()
        {
            var s = TestUtil.NewGame(11);
            var npc = Valley(s, "rec_milka");
            DialogueEngine.Start(s, "dlg_recruit", new DialogueState { NpcId = npc.Id });
            Assert.IsFalse(DialogueEngine.Options(s).Any(o => o.Text.StartsWith("Я сделал")));
            s.Execute(new ChooseOption(DialogueEngine.Options(s).First(o => o.Text.StartsWith("Спросить")).Index));
            Assert.IsTrue(npc.QuestStarted);
            DialogueEngine.End(s);
            s.State.Hero.Add("fiber", 5);
            Assert.IsTrue(RecruitmentSystem.QuestDone(s, npc));
            DialogueEngine.Start(s, "dlg_recruit", new DialogueState { NpcId = npc.Id });
            Assert.IsTrue(DialogueEngine.Options(s).Any(o => o.Text.StartsWith("Я сделал") && o.Enabled));
        }
    }

    public class CombatBalanceTest
    {
        /// <summary>Простой бот боя: бьёт ближайшего врага, умения 1–2 по готовности.</summary>
        static (bool heroWon, float seconds) Fight(GameSession s, float maxSeconds, bool useSkills)
        {
            int max = TestUtil.Seconds(s, maxSeconds);
            for (int i = 0; i < max; i++)
            {
                var hero = s.HeroUnit;
                if (hero.Ai == AiState.Dead || s.State.Hero.Defeats > 0) return (false, i * s.Dt);
                var enemies = s.State.Units.Where(u => u.Team == Teams.Wild && u.Ai != AiState.Dead).ToList();
                if (enemies.Count == 0) return (true, i * s.Dt);
                var near = enemies.OrderBy(u => Vec2.Distance(u.Pos, hero.Pos)).First();
                if (hero.TargetId != near.Id) s.Execute(new AttackUnit(near.Id));
                if (useSkills && i % 10 == 0)
                {
                    s.Execute(new UseAbility(1, near.Pos));
                    if (enemies.Count > 1) s.Execute(new UseAbility(2, near.Pos));
                }
                s.Tick(); s.Events.Clear();
            }
            return (false, maxSeconds);
        }

        static GameSession Arena(string weapon, params string[] enemies)
        {
            var s = TestUtil.NewGame(21);
            // Чистая арена: убираем всех, кроме героя.
            foreach (var u in s.State.Units.Where(u => u.Kind != UnitKind.Hero).ToList()) s.RemoveUnit(u);
            foreach (var p in s.State.Pois) p.Cleared = true;
            s.State.Npcs.Clear();
            if (weapon != null)
            {
                var item = LootSystem.RollItem(s, weapon, "settlement");
                item.Affixes.Clear();
                s.State.Hero.Items.Add(item); LootSystem.Equip(s, item.Id);
            }
            var hero = s.HeroUnit;
            hero.Hp = hero.MaxHp;
            for (int i = 0; i < enemies.Length; i++)
            {
                var u = SpawnSystem.CreateEnemy(s, enemies[i], hero.Pos + Vec2.FromAngle(i * 1.3f) * 8f, Teams.Wild);
                u.Ai = AiState.Chase; u.TargetId = hero.Id;
            }
            return s;
        }

        [Test]
        public void NakedHeroBeatsTwoWolves()
        {
            var s = Arena(null, "wolf", "wolf");
            var (won, sec) = Fight(s, 60, false);
            Assert.IsTrue(won, "герой без оружия должен справиться с двумя волками");
            Assert.That(sec, Is.InRange(3f, 30f));
        }

        [Test]
        public void ClubHeroBeatsWolfPackWithSkills()
        {
            var s = Arena("club", "wolf", "wolf", "wolf", "wolf");
            var (won, sec) = Fight(s, 90, true);
            Assert.IsTrue(won);
            Assert.That(sec, Is.InRange(2f, 45f));
        }

        [Test]
        public void LoneFreshHeroLosesToFullBanditCamp()
        {
            var s = Arena(null, "bandit", "bandit", "bandit", "bandit", "bandit_archer", "bandit_archer", "bandit_chief");
            var (won, sec) = Fight(s, 120, false);
            Assert.IsFalse(won, "лагерь разбойников не должен браться голыми руками в одиночку");
        }

        [Test]
        public void ArmedHeroWithCompanionsTakesBanditCamp()
        {
            var s = Arena("stone_spear", "bandit", "bandit", "bandit", "bandit", "bandit_archer", "bandit_archer", "bandit_chief");
            s.State.Hero.Body = 7;
            HeroSystem.RefreshUnitStats(s); s.HeroUnit.Hp = s.HeroUnit.MaxHp;
            foreach (var name in new[] { "Айна", "Горн", "Сайна" })
            {
                var c = new CompanionState { Id = s.State.NewId("comp"), Name = name, Combat = 40, Relation = 30, LifespanYears = 60, BirthMonths = -300 };
                s.State.Companions.Add(c);
                CompanionSystem.SetRole(s, c, CompanionRoles.Party, null);
            }
            var (won, sec) = Fight(s, 150, true);
            Assert.IsTrue(won, "с копьём и тремя спутниками лагерь должен пасть");
            Assert.That(sec, Is.InRange(6f, 120f));
        }

        [Test]
        public void BossSlamIsTelegraphedAndCanBeDodged()
        {
            var s = Arena(null, "bear");
            var hero = s.HeroUnit;
            s.State.Hero.Body = 40; HeroSystem.RefreshUnitStats(s); hero.Hp = hero.MaxHp;
            var bear = s.State.Units.First(u => u.DefId == "bear");
            bear.Pos = hero.Pos + new Vec2(1.5f, 0); bear.AbilityCd = 0;
            Assert.IsTrue(TestUtil.TickUntil(s, () => s.State.Areas.Count > 0, 60), "медведь должен ударить по области");
            var area = s.State.Areas[0];
            Assert.Greater(area.Delay, 0.5f, "у удара по области есть видимая задержка");
            // Уходим из зоны, пока она «заряжается».
            var away = (hero.Pos - area.Pos).Normalized;
            hero.Pos = s.Map.NearestWalkable(area.Pos + away * (area.Radius + 3f)); hero.PrevPos = hero.Pos;
            bear.Pos = s.Map.NearestWalkable(hero.Pos + away * 12f); bear.Ai = AiState.Recover; bear.StateTimer = 2f;
            float hp = hero.Hp;
            TestUtil.Ticks(s, TestUtil.Seconds(s, 1.2f));
            Assert.GreaterOrEqual(hero.Hp, hp - 0.01f, "увернувшийся герой не получает урона");
        }

        [Test]
        public void EraPowerWorksOnlyInSmoke()
        {
            var s = TestUtil.NewGame(21);
            s.Execute(new FoundCamp());
            s.PlayerNation.EraId = "city_states";
            var hero = s.HeroUnit;
            s.State.Hero.Energy = 100;
            s.Execute(new UseAbility(4, hero.Pos + new Vec2(5, 0)));
            Assert.IsFalse(s.State.Hero.Cooldowns.ContainsKey("power_fireball"), "вне дыма сила эпохи не работает");
            var zone = s.State.Pois.First(p => p.DefId == "zone");
            hero.Pos = zone.Pos; hero.PrevPos = zone.Pos;
            ZoneSystem.TickRealtime(s, 1f);
            Assert.IsTrue(s.State.Hero.InSmoke);
            s.Execute(new UseAbility(4, hero.Pos + new Vec2(5, 0)));
            Assert.IsTrue(s.State.Hero.Cooldowns.ContainsKey("power_fireball"), "в дыму сила эпохи срабатывает");
        }

        [Test]
        public void DefeatLeadsToLongSleepNotGameOver()
        {
            var s = Arena(null, "bear", "bear", "bear");
            s.HeroUnit.Hp = 5;
            double before = s.State.Months;
            TestUtil.TickUntil(s, () => s.State.Hero.Defeats > 0, 20 * 60, closeDialogs: false);
            Assert.AreEqual(1, s.State.Hero.Defeats);
            Assert.IsFalse(s.State.GameOver);
            Assert.GreaterOrEqual(s.State.Months - before, 12 * 20, "после поражения проходят десятилетия");
            Assert.IsTrue(s.State.Narrative.ChronicleOpen);
        }
    }

    public class ImmortalityTests
    {
        [Test]
        public void FirstEssenceSceneFiresOnceAndUnlocksRejuvenation()
        {
            var s = TestUtil.NewGame(31);
            double vit = s.State.Hero.VitalityYears;
            LootSystem.Drop(s, s.HeroUnit.Pos, "essence", 1);
            TestUtil.Ticks(s, 2, closeDialogs: false);
            Assert.AreEqual("dlg_first_essence", s.State.Narrative.Active?.DialogueId);
            TestUtil.CloseAll(s);
            Assert.IsTrue(s.State.Hero.RejuvenationUnlocked);
            Assert.Greater(s.State.Hero.VitalityYears, vit);
            LootSystem.Drop(s, s.HeroUnit.Pos, "essence", 1);
            TestUtil.Ticks(s, 2, closeDialogs: false);
            Assert.IsNull(s.State.Narrative.Active, "сцена не должна повторяться");
        }

        [Test]
        public void RejuvenationCostsSkillAndMemories()
        {
            var s = TestUtil.NewGame(31);
            var h = s.State.Hero;
            h.RejuvenationUnlocked = true;
            h.VitalityYears = 30;
            h.Add("essence", 3);
            h.Memories.Add("ruins_carvings");
            float skillSum = h.Skills.Values.Sum();
            Assert.IsTrue(HeroSystem.Rejuvenate(s));
            Assert.AreEqual(45, h.VitalityYears, 0.01);
            Assert.Less(h.Skills.Values.Sum(), skillSum);
            Assert.IsFalse(h.Memories.Contains("ruins_carvings"));
            Assert.IsTrue(h.Memories.Contains("core_tribe"), "базовые воспоминания не теряются");
        }

        [Test]
        public void ForgottenMemoryChangesDialogueOptions()
        {
            var s = TestUtil.NewGame(31);
            var rival = s.State.Nations.First(n => !n.IsPlayer);
            var h = s.State.Hero;
            h.Memories.Add("ruins_carvings");
            DialogueEngine.Start(s, "dlg_leader", new DialogueState { NationId = rival.Id });
            Assert.IsTrue(DialogueEngine.Options(s).Any(o => o.Text.Contains("знаки")));
            DialogueEngine.End(s);
            h.RejuvenationUnlocked = true; h.Add("essence", 3); h.VitalityYears = 20;
            HeroSystem.Rejuvenate(s);
            DialogueEngine.Start(s, "dlg_leader", new DialogueState { NationId = rival.Id });
            Assert.IsFalse(DialogueEngine.Options(s).Any(o => o.Text.Contains("знаки")), "забытое воспоминание убирает вариант");
        }

        [Test]
        public void AgeShiftsBodyToMind()
        {
            var s = TestUtil.NewGame(31);
            var h = s.State.Hero;
            h.VitalityYears = h.MaxVitalityYears;
            float youngBody = HeroSystem.EffBody(s), youngMind = HeroSystem.EffMind(s);
            h.VitalityYears = 5;
            Assert.Less(HeroSystem.EffBody(s), youngBody);
            Assert.Greater(HeroSystem.EffMind(s), youngMind);
            Assert.Greater(HeroSystem.EffSpeech(s), HeroSystem.Skill(h, "speech"));
        }

        [Test]
        public void TabooChoiceHasConsequences()
        {
            var s = TestUtil.NewGame(31);
            s.Execute(new FoundCamp());
            TestUtil.CloseAll(s);
            DialogueEngine.Start(s, "dlg_taboo", new DialogueState());
            s.Execute(new ChooseOption(0));
            Assert.AreEqual("strict", s.State.Narrative.Taboo);
            var comp = new CompanionState { Id = "c_t", Name = "Тест", LifespanYears = 50, BirthMonths = -49 * 12, Relation = 10 };
            s.State.Companions.Add(comp);
            s.State.Hero.Add("essence", 3);
            DialogueEngine.Start(s, "dlg_dying_friend", new DialogueState { CompanionId = comp.Id });
            var opts = DialogueEngine.Options(s);
            Assert.IsFalse(opts.Any(o => o.Text == "Дать ресурс (3)"));
            var secret = opts.First(o => o.Text.Contains("тайно"));
            s.Execute(new ChooseOption(secret.Index));
            Assert.IsTrue(s.State.Narrative.Flags.Contains("taboo_broken"));
            Assert.AreEqual(62, comp.LifespanYears, 0.01);
        }
    }
}
