using System.Linq;
using Immortal.Core;
using Immortal.Core.Commands;
using Immortal.Core.Content;
using Immortal.Core.World;
using NUnit.Framework;

namespace Immortal.Tests
{
    public class CoreTests
    {
        [Test]
        public void ContentValidatorFindsNoErrors()
        {
            var errors = ContentValidator.Validate(TestUtil.Content());
            Assert.IsEmpty(errors, string.Join("\n", errors));
        }

        [Test]
        public void SameSeedAndCommandsGiveSameState()
        {
            ulong Run()
            {
                var s = TestUtil.NewGame(777);
                var hero = s.HeroUnit;
                s.Execute(new MoveTo(hero.Pos + new Vec2(12, 6)));
                TestUtil.Ticks(s, 400);
                s.Execute(new SetMoveInput(new Vec2(1, 0)));
                TestUtil.Ticks(s, 100);
                s.Execute(new SetMoveInput(Vec2.Zero));
                s.Execute(new UseAbility(1, s.HeroUnit.Pos + new Vec2(5, 0)));
                TestUtil.Ticks(s, 1200);
                return SaveLoad.Hash(s);
            }
            Assert.AreEqual(Run(), Run());
        }

        [Test]
        public void DifferentSeedsGiveDifferentWorlds()
        {
            var a = TestUtil.NewGame(1); var b = TestUtil.NewGame(2);
            Assert.AreNotEqual(SaveLoad.Hash(a), SaveLoad.Hash(b));
        }

        [Test]
        public void SaveLoadRoundTrip()
        {
            var s = TestUtil.NewGame(42);
            TestUtil.Ticks(s, 600);
            string json = SaveLoad.Serialize(s);
            var loaded = SaveLoad.Load(TestUtil.Content(), json);
            Assert.AreEqual(json, SaveLoad.Serialize(loaded));
            // Продолжение после загрузки идёт так же, как без неё.
            TestUtil.Ticks(s, 300); TestUtil.Ticks(loaded, 300);
            Assert.AreEqual(SaveLoad.Hash(s), SaveLoad.Hash(loaded));
        }

        [Test]
        public void TimeStopsInDialogue()
        {
            var s = GameSession.NewGame(TestUtil.Content(), 5);
            Assert.IsNotNull(s.State.Narrative.Active, "вступительный диалог должен открыться");
            double m = s.State.Months;
            for (int i = 0; i < 100; i++) s.Tick();
            Assert.AreEqual(m, s.State.Months);
        }

        [Test]
        public void HeroWalksAcrossSeveralRegionsWithoutLoading()
        {
            var s = TestUtil.NewGame(9);
            var regions = new System.Collections.Generic.HashSet<int>();
            var hero = s.HeroUnit;
            // Идём по кругу через центры регионов.
            foreach (var c in s.Map.RegionCenters.Take(4))
            {
                s.Execute(new MoveTo(c));
                TestUtil.TickUntil(s, () => Vec2.Distance(s.HeroUnit.Pos, c) < 6f || s.HeroUnit.Path == null, 20 * 200);
                regions.Add(s.Map.RegionIndexAt(s.HeroUnit.Pos));
                if (s.HeroUnit.Ai == AiState.Dead || s.State.GameOver) break;
            }
            Assert.GreaterOrEqual(regions.Count, 2, "герой должен пройти хотя бы через два региона");
        }

        [Test]
        public void PathfinderAvoidsWater()
        {
            var s = TestUtil.NewGame(3);
            var start = s.HeroUnit.Pos;
            var path = s.Paths.FindPath(start, start + new Vec2(60, 40));
            Assert.IsNotNull(path);
            Vec2 prev = start;
            foreach (var p in path)
            {
                Assert.IsTrue(s.Map.Walkable(p), $"точка пути {p} непроходима");
                prev = p;
            }
        }

        [Test]
        public void FogIsRevealedAroundHero()
        {
            var s = TestUtil.NewGame(3);
            int idx = s.Map.TileAt(s.HeroUnit.Pos);
            Assert.IsTrue(Vision.IsExplored(s.State, idx));
        }
    }
}
