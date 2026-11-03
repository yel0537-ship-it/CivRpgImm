using Immortal.Core.Character;
using Immortal.Core.Content;

namespace Immortal.Core.Narrative
{
    /// <summary>
    /// Цели-подсказки основного пути (objectives.json): текущая — первая невыполненная, у которой выполнены условия показа.
    /// Проверяются раз в секунду; выполнение даёт опыт и событие для интерфейса.
    /// </summary>
    public static class ObjectiveSystem
    {
        public static ObjectiveDef Current(GameSession s)
        {
            var done = s.State.Narrative.Objectives;
            foreach (var o in s.Content.Objectives)
            {
                if (done.Contains(o.id)) continue;
                if (!Conditions.All(s, o.show, null)) continue;
                return o;
            }
            return null;
        }

        public static bool IsDone(GameSession s, ObjectiveDef o)
        {
            if (o.done == null || o.done.Count == 0) return false;
            foreach (var set in o.done)
                if (Conditions.All(s, set, null)) return true;
            return false;
        }

        public static void Tick(GameSession s)
        {
            // Несколько целей могли выполниться разом (например, после прыжка во времени).
            for (int guard = 0; guard < 8; guard++)
            {
                var o = Current(s);
                if (o == null || !IsDone(s, o)) return;
                s.State.Narrative.Objectives.Add(o.id);
                if (o.xp > 0) HeroSystem.GainXp(s, o.xp);
                s.Emit(new GameEvent { Type = GameEventType.ObjectiveCompleted, Id = o.id, Text = o.title });
            }
        }
    }
}
