using System;
using System.Linq;
using Immortal.Core;
using Immortal.Core.Action;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using Immortal.Core.Commands;
using Immortal.Core.Narrative;
using Immortal.Core.Time;
using NUnit.Framework;

namespace Immortal.Tests
{
    public class SettlementTests
    {
        public static GameSession WithCamp(int seed = 41, int companions = 7)
        {
            var s = TestUtil.NewGame(seed);
            s.Execute(new FoundCamp());
            TestUtil.CloseAll(s);
            var cap = s.Capital(s.PlayerNation);
            for (int i = 0; i < companions; i++)
            {
                var c = new CompanionState { Id = s.State.NewId("comp"), Name = "Спутник " + i, LifespanYears = 400, BirthMonths = s.State.Months - 240, Relation = 40, Speech = 30, Mind = 6 };
                s.State.Companions.Add(c);
                cap.Population += 1;
            }
            return s;
        }

        [Test]
        public void CampGrowsToRoyalCapitalInSimulation()
        {
            var s = WithCamp();
            var pn = s.PlayerNation; var cap = s.Capital(pn);
            var gov = s.State.Companions[0];
            s.Execute(new AssignCompanion(gov.Id, CompanionRoles.Governor, cap.Id));
            pn.Add("wood", 200); pn.Add("stone", 200);
            s.State.Narrative.Flags.Add("true_immortality");
            int stage = cap.StageIndex;
            for (int i = 0; i < 12 && cap.StageIndex < 6; i++)
            {
                TimeJumpSystem.Jump(s, 25, "test");
                TestUtil.CloseAll(s);
                Assert.GreaterOrEqual(cap.StageIndex, stage, "ступень не должна падать");
                stage = cap.StageIndex;
            }
            Assert.AreEqual("royal_capital", CitySystem.Stage(s, cap).id, $"столица на ступени {CitySystem.Stage(s, cap).name}, нас. {cap.Population:0}");
            Assert.AreEqual("ancient_kingdoms", pn.EraId);
            Assert.AreEqual(Roles.King, s.State.Hero.Role);
            Assert.IsTrue(cap.HasBuilding("palace"));
        }

        [Test]
        public void HungerIsVisibleAndHurts()
        {
            var s = WithCamp(42, 10);
            var cap = s.Capital(s.PlayerNation);
            cap.FoodStock = 0; s.PlayerNation.Treasury["food"] = 0;
            float pop = cap.Population;
            for (int i = 0; i < 12; i++) CitySystem.Step(s, cap, 1f);
            Assert.IsTrue(cap.Hungry);
            Assert.Less(cap.Population, pop);
            Assert.Greater(cap.Refugees, 0);
        }

        [Test]
        public void NeglectedCitySecedes()
        {
            var s = WithCamp(43, 12);
            var pn = s.PlayerNation;
            var colony = CitySystem.Found(s, pn, s.Capital(pn).Pos + new Vec2(150, 0), "Дальний", 1, false);
            colony.Population = 20; colony.FoodStock = 0;
            var gov = s.State.Companions[1]; gov.Relation = -80;
            s.Execute(new AssignCompanion(gov.Id, CompanionRoles.Governor, colony.Id));
            bool rebellionAsked = false;
            for (int m = 0; m < 120 && colony.NationId != null; m++)
            {
                colony.FoodStock = 0; pn.Treasury.Clear(); // бедность и изоляция
                for (int w = 0; w < 4; w++) CitySystem.Step(s, colony, 0.25f);
                CitySystem.StepMonth(s, colony);
                if (s.State.Narrative.Active?.DialogueId == "dlg_rebellion")
                {
                    rebellionAsked = true;
                    var let = DialogueEngine.Options(s).First(o => o.Text.StartsWith("Отпустить"));
                    s.Execute(new ChooseOption(let.Index));
                }
                TestUtil.CloseAll(s);
            }
            Assert.IsTrue(rebellionAsked, "мятеж должен подаваться через старого друга");
            Assert.IsNull(colony.NationId, "город должен отделиться");
            Assert.AreEqual(CompanionRoles.Rebel, gov.Role);
        }

        [Test]
        public void TravelToOwnCityTakesTime()
        {
            var s = WithCamp(45, 4);
            var pn = s.PlayerNation;
            var far = CitySystem.Found(s, pn, s.Capital(pn).Pos + new Vec2(180, 30), "Дальний Брод", 1, false);
            Immortal.Core.World.Vision.Reveal(s.State, s.Map, far.Pos, 10f);
            double before = s.State.Months;
            s.Execute(new TravelTo(far.Id));
            Assert.Less(Vec2.Distance(s.HeroUnit.Pos, far.Pos), 12f, "герой прибыл");
            Assert.GreaterOrEqual(s.State.Months - before, 1.0, "дорога заняла время");
            var rival = s.State.Nations.First(n => !n.IsPlayer);
            Assert.IsFalse(TravelTo.CanTravel(s, s.Capital(rival), out _, out _), "в чужую столицу без открытых границ нельзя");
        }

        [Test]
        public void SettlersFoundNewCity()
        {
            var s = WithCamp(44, 12);
            var pn = s.PlayerNation; var cap = s.Capital(pn);
            cap.StageIndex = 2; cap.Population = 30; pn.Add("food", 50);
            int before = CitySystem.CityCount(s, pn);
            s.Execute(new SendSettlers(cap.Id));
            Assert.AreEqual(1, s.State.Armies.Count(a => a.Kind == "settlers"));
            for (int i = 0; i < 6; i++) { s.InTimeJump = true; ArmySystem.StepMonth(s); s.InTimeJump = false; }
            Assert.AreEqual(before + 1, CitySystem.CityCount(s, pn));
        }
    }

    public class CivilizationTests
    {
        [Test]
        public void EraChangeStartsChapterAndOffersJump()
        {
            var s = SettlementTests.WithCamp(51, 10);
            var pn = s.PlayerNation; var cap = s.Capital(pn);
            int chapter = s.State.Chapter;
            foreach (var t in new[] { "stone_tools", "agriculture", "oral_tradition" }) TechSystem.Complete(s, pn, s.Content.Tech[t]);
            Assert.AreEqual("primitive", pn.EraId, "без ступени эпоха не наступает");
            cap.StageIndex = s.Content.StageIndex["city_state"];
            EraSystem.Check(s, pn);
            Assert.AreEqual("city_states", pn.EraId);
            Assert.AreEqual(chapter + 1, s.State.Chapter);
            Assert.AreEqual("dlg_era_city_states", s.State.Narrative.Active?.DialogueId);
            Assert.IsNotEmpty(s.State.Narrative.JumpOptions);
        }

        [Test]
        public void WorldStateConvergesAfterLeavingAndReturning()
        {
            var s = TestUtil.NewGame(52);
            var camp = s.State.Pois.First(p => p.DefId == "camp");
            var hero = s.HeroUnit;
            hero.Pos = s.Map.NearestWalkable(camp.Pos + new Vec2(30, 0)); hero.PrevPos = hero.Pos;
            TestUtil.Ticks(s, 20);
            Assert.IsTrue(camp.Active, "лагерь разворачивается рядом с героем");
            var bandit = s.State.Units.First(u => u.PoiId == camp.Id);
            CombatSystem.Kill(s, bandit, hero);
            TestUtil.Ticks(s, 40);
            int alive = s.State.Units.Count(u => u.PoiId == camp.Id && u.Ai != AiState.Dead);
            hero.Pos = s.Map.NearestWalkable(camp.Pos + new Vec2(120, 0)); hero.PrevPos = hero.Pos;
            TestUtil.Ticks(s, 20);
            Assert.IsFalse(camp.Active);
            Assert.AreEqual(alive, camp.EnemiesRemaining, "при сворачивании результат сохраняется");
            hero.Pos = s.Map.NearestWalkable(camp.Pos + new Vec2(30, 0)); hero.PrevPos = hero.Pos;
            TestUtil.Ticks(s, 20);
            Assert.AreEqual(alive, s.State.Units.Count(u => u.PoiId == camp.Id && u.Ai != AiState.Dead), "при возвращении — то же число врагов");
        }

        [Test]
        public void ClearingOutpostExtendsTerritory()
        {
            var s = SettlementTests.WithCamp(53, 2);
            var outpost = s.State.Pois.First(p => p.DefId == "outpost");
            Assert.AreNotEqual(s.PlayerNation, TerritorySystem.OwnerAt(s, outpost.Pos));
            outpost.Active = true;
            SpawnSystem.OnPoiCleared(s, outpost, true);
            Assert.AreEqual(s.State.PlayerNationId, outpost.OwnerNationId);
            Assert.AreEqual(s.PlayerNation, TerritorySystem.OwnerAt(s, outpost.Pos));
        }

        [Test]
        public void LeaderDeathStartsRegencyAndHeirClosesWindow()
        {
            var s = SettlementTests.WithCamp(54, 2);
            var rival = s.State.Nations.First(n => !n.IsPlayer);
            var colony = CitySystem.Found(s, rival, s.Capital(rival).Pos + new Vec2(60, 10), "Окраина", 1, false);
            NationSystem.OnLeaderKilled(s, rival.Id, true);
            Assert.IsFalse(rival.Leader.Alive);
            Assert.AreEqual(Autonomy.Vulnerable, colony.AutonomyState);
            s.State.Months = rival.Leader.RegencyUntilMonths + 1;
            NationSystem.StepMonth(s, rival);
            Assert.IsTrue(rival.Leader.Alive);
            Assert.IsFalse(rival.Leader.Immortal, "наследник смертен");
            Assert.AreEqual(Autonomy.Loyal, colony.AutonomyState);
        }

        [Test]
        public void PersonalMeetingIsStrongerThanEnvoy()
        {
            var s = SettlementTests.WithCamp(55, 2);
            var rival = s.State.Nations.First(n => !n.IsPlayer);
            Assert.Greater(Diplomacy.AcceptChance(s, rival, "trade", true), Diplomacy.AcceptChance(s, rival, "trade", false));
        }

        [Test]
        public void HeroBattleAffectsNationFate()
        {
            var s = SettlementTests.WithCamp(56, 2);
            var rival = s.State.Nations.First(n => !n.IsPlayer);
            Diplomacy.DeclareWar(s, s.State.PlayerNationId, rival.Id);
            var cap = s.Capital(s.PlayerNation);
            var army = ArmySystem.Create(s, rival, s.Capital(rival), 15, "army");
            army.Pos = cap.Pos + new Vec2(4, 0);
            army.TargetCityId = cap.Id; army.State = ArmyStates.Sieging;
            s.HeroUnit.Pos = cap.Pos; s.HeroUnit.PrevPos = cap.Pos;
            s.State.Hero.Body = 12; HeroSystem.RefreshUnitStats(s); s.HeroUnit.Hp = s.HeroUnit.MaxHp;
            TestUtil.Ticks(s, 25);
            Assert.IsTrue(army.Expanded, "рядом с героем битва идёт в реальном времени");
            // Герой лично добивает солдат.
            for (int i = 0; i < 20 * 120 && army.Strength > 0; i++)
            {
                var foe = s.State.Units.FirstOrDefault(u => u.ArmyId == army.Id && u.Ai != AiState.Dead);
                if (foe != null) CombatSystem.DealDamage(s, s.HeroUnit, foe, 999, false);
                s.Tick(); s.Events.Clear(); TestUtil.CloseAll(s);
            }
            Assert.LessOrEqual(army.Strength, 0.01f, "армия разбита действиями героя");
            Assert.AreEqual(s.State.PlayerNationId, cap.NationId);
        }
    }

    public class WorldSimulationTest
    {
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void WorldLivesThreeHundredYearsWithoutPlayer(int seed)
        {
            var s = TestUtil.NewGame(seed);
            s.State.Narrative.Flags.Add("true_immortality");
            var startTech = s.State.Nations.Select(n => n.Researched.Count).ToArray();
            Assert.DoesNotThrow(() => TimeJumpSystem.Jump(s, 300, "test"));
            Assert.IsFalse(s.State.GameOver);
            for (int i = 0; i < s.State.Nations.Count; i++)
            {
                var n = s.State.Nations[i];
                Assert.Greater(n.Researched.Count, startTech[i], $"{n.Name} должна развиваться");
                Assert.Greater(s.Content.EraOrder(n.EraId) + (n.Destroyed ? 1 : 0), 0, $"{n.Name} должна сменить эпоху");
            }
            foreach (var c in s.State.Cities)
            {
                Assert.That(c.Population, Is.InRange(0.9f, 400f), c.Name);
                Assert.That(c.Loyalty, Is.InRange(0f, 100f));
                Assert.IsFalse(float.IsNaN(c.FoodStock));
            }
            foreach (var n in s.State.Nations)
                foreach (var kv in n.Treasury) Assert.That(kv.Value, Is.InRange(-1f, 1e6f), $"{n.Name}.{kv.Key}");
            Assert.Less(s.State.Armies.Count, 60);
            Assert.Less(s.State.Cities.Count, 40);
        }
    }

    public class TimeJumpTests
    {
        [Test]
        public void HeroSurvivesJumpAndWorldVisiblyChanges()
        {
            var s = SettlementTests.WithCamp(61, 10);
            var pn = s.PlayerNation; var cap = s.Capital(pn);
            s.Execute(new AssignCompanion(s.State.Companions[0].Id, CompanionRoles.Governor, cap.Id));
            pn.Add("wood", 100); pn.Add("stone", 60);
            int modsBefore = s.State.WorldMods.Terrain.Count;
            int buildingsBefore = cap.Buildings.Count;
            int chronicleBefore = s.State.Chronicle.Count;
            s.State.Hero.Role = Roles.Chief;
            s.Execute(new TimeJump(50));
            Assert.IsFalse(s.State.GameOver);
            Assert.Greater(s.State.Hero.VitalityYears, 0);
            Assert.AreNotEqual(AiState.Dead, s.HeroUnit.Ai);
            Assert.Greater(cap.Buildings.Count, buildingsBefore, "город вырос");
            Assert.Greater(s.State.WorldMods.Terrain.Count, modsBefore, "местность изменилась");
            Assert.IsTrue(s.State.Narrative.ChronicleOpen);
            var entries = Chronicle.Since(s, s.State.Narrative.ChronicleFrom);
            Assert.Greater(entries.Count, 1);
            Assert.IsTrue(entries.Any(e => e.Category == "jump_end"));
            // Хроника соответствует симуляции: все упомянутые постройки ступеней реально есть.
            foreach (var e in entries.Where(e => e.Category == "stage")) Assert.IsTrue(s.State.Cities.Any(c => e.Text.Contains(c.Name)));
        }

        [Test]
        public void CompanionsAgeDieAndLeaveDescendants()
        {
            var s = SettlementTests.WithCamp(62, 0);
            var c = new CompanionState { Id = "c_old", Name = "Старый Тор", LifespanYears = 30, BirthMonths = s.State.Months - 28 * 12, Relation = -60 };
            s.State.Companions.Add(c);
            s.State.Hero.Role = Roles.Chief;
            s.Execute(new TimeJump(25));
            Assert.IsFalse(c.Alive);
            Assert.IsTrue(s.State.Pois.Any(p => p.DefId == "grave" && p.Data == c.Id), "могила в мире");
            var d = s.State.Npcs.FirstOrDefault(n => n.AncestorCompanionId == c.Id);
            Assert.IsNotNull(d, "остаётся потомок");
            Assert.Less(d.InheritedRelation, 0, "обида переходит к потомкам");
        }
    }

    public class EndingsTests
    {
        static GameSession Kingdom(int seed)
        {
            var s = SettlementTests.WithCamp(seed, 6);
            var pn = s.PlayerNation; var cap = s.Capital(pn);
            pn.EraId = "ancient_kingdoms";
            cap.StageIndex = s.Content.StageIndex["royal_capital"]; cap.Population = 120;
            foreach (var t in s.Content.TechFile.techs) if (t.era != "ancient_kingdoms") TechSystem.Complete(s, pn, t);
            foreach (var r in new[] { "stone", "wood", "bronze", "ore" }) pn.Treasury[r] = 2000;
            TestUtil.CloseAll(s);
            return s;
        }

        [Test]
        public void TrueImmortality()
        {
            var s = Kingdom(71); var pn = s.PlayerNation; var cap = s.Capital(pn);
            pn.Treasury["essence"] = 40;
            TechSystem.Complete(s, pn, s.Content.Tech["philosophy"]);
            TechSystem.Complete(s, pn, s.Content.Tech["essence_alchemy"]);
            s.Execute(new Build(cap.Id, "laboratory"));
            Assert.IsTrue(cap.HasBuilding("laboratory"));
            TechSystem.AddScience(s, pn, 5000); // доводит «Формулу вечности» через обычный прогресс
            TestUtil.CloseAll(s);
            if (!pn.Researched.Contains("eternity_formula")) TechSystem.Complete(s, pn, s.Content.Tech["eternity_formula"]);
            s.Execute(new Build(cap.Id, "eternal_spire"));
            Assert.AreEqual("immortality", s.State.EndingId);
            Assert.IsTrue(s.State.Narrative.Flags.Contains("true_immortality"));
        }

        [Test]
        public void Awakening()
        {
            var s = Kingdom(72); var pn = s.PlayerNation; var cap = s.Capital(pn);
            foreach (var t in s.Content.TechFile.techs.Where(t => t.branch == "knowledge")) TechSystem.Complete(s, pn, t);
            s.Execute(new Build(cap.Id, "laboratory"));
            pn.Treasury["essence"] = 60;
            s.Execute(new Build(cap.Id, "mirror_of_sky"));
            Assert.IsNull(s.State.EndingId, "без намёков симуляции зеркало молчит");
            foreach (var h in new[] { "a", "b", "c" }) SimulationHints.Register(s, h, null);
            s.Execute(new ActivateMirror());
            Assert.AreEqual("awakening", s.State.EndingId);
        }

        [Test]
        public void InfluenceVictoryWithRegencyAccelerator()
        {
            var s = Kingdom(73); var pn = s.PlayerNation;
            var rival = s.State.Nations.First(n => !n.IsPlayer);
            foreach (var n in s.State.Nations) if (!n.IsPlayer) n.InfluenceTotal = 2500;
            pn.InfluenceTotal = 4500;
            EndingSystem.CheckMonthly(s);
            Assert.IsNull(s.State.EndingId, "соперники слишком влиятельны");
            foreach (var n in s.State.Nations.Where(n => !n.IsPlayer).ToList())
            {
                DialogueEngine.Start(s, "dlg_leader", new DialogueState { NationId = n.Id });
                Effects.Apply(s, new Immortal.Core.Content.EffectDef { killLeader = true }, s.State.Narrative.Active);
                DialogueEngine.End(s);
                n.InfluenceTotal = Math.Min(n.InfluenceTotal, 2200);
            }
            EndingSystem.CheckMonthly(s);
            Assert.AreEqual("influence", s.State.EndingId);
        }

        [Test]
        public void MilitaryVictory()
        {
            var s = Kingdom(74); var pn = s.PlayerNation; var cap = s.Capital(pn);
            pn.Treasury["food"] = 5000; cap.Population = 260;
            foreach (var n in s.State.Nations.Where(n => !n.IsPlayer).ToList())
            {
                s.Execute(new DeclareWar(n.Id));
                cap.Population = 260;
                s.Execute(new RecruitArmy(cap.Id, 400));
                var army = s.State.Armies.Last(a => a.NationId == pn.Id);
                s.Execute(new OrderArmy(army.Id, n.OriginalCapitalId));
            }
            s.State.Narrative.Flags.Add("true_immortality");
            for (int i = 0; i < 30 && s.State.EndingId == null; i++) { s.InTimeJump = true; WorldSimulation.StepMonth(s); s.InTimeJump = false; TestUtil.CloseAll(s); }
            Assert.AreEqual("military", s.State.EndingId);
        }

        [Test]
        public void DeathOfOldAgeEndsGame()
        {
            var s = TestUtil.NewGame(75);
            s.State.Hero.VitalityYears = 0.001;
            TestUtil.Ticks(s, 40);
            Assert.IsTrue(s.State.GameOver);
            Assert.AreEqual("old_age", s.State.EndingId);
        }
    }
}
