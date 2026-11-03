using System.Linq;
using Immortal.Core;
using Immortal.Core.Commands;
using Immortal.Core.Narrative;
using NUnit.Framework;

namespace Immortal.Tests
{
    public class ObjectiveTests
    {
        [Test]
        public void ObjectivesFollowMainPath()
        {
            var s = TestUtil.NewGame(81);
            Assert.AreEqual("obj_gather", ObjectiveSystem.Current(s).id);
            s.State.Hero.Add("wood", 3); s.State.Hero.Add("stone", 1);
            ObjectiveSystem.Tick(s);
            Assert.AreEqual("obj_club", ObjectiveSystem.Current(s).id);
            s.Execute(new Craft("club", false));
            ObjectiveSystem.Tick(s);
            Assert.AreEqual("obj_meet", ObjectiveSystem.Current(s).id);
            Assert.IsTrue(s.Events.Any(e => e.Type == GameEventType.ObjectiveCompleted && e.Id == "obj_club"));
        }

        [Test]
        public void SeveralObjectivesCanCompleteAtOnce()
        {
            var s = SettlementTests.WithCamp(82, 6);
            s.Capital(s.PlayerNation).StageIndex = 1;
            var item = Immortal.Core.Action.LootSystem.RollItem(s, "club", "field");
            s.State.Hero.Items.Add(item); s.State.Hero.WeaponId = item.Id;
            TestUtil.Ticks(s, s.TicksPerSecond + 1);
            Assert.AreEqual("obj_governor", ObjectiveSystem.Current(s).id);
        }

        [Test]
        public void EveryObjectiveExceptFinalHasCompletion()
        {
            foreach (var o in TestUtil.Content().Objectives.Take(TestUtil.Content().Objectives.Count - 1))
                Assert.IsNotEmpty(o.done, o.id);
        }
    }
}
