using System;
using System.Collections.Generic;
using Immortal.Core.Character;
using Immortal.Core.Content;
using Immortal.Core.Narrative;

namespace Immortal.Core.Civilization
{
    /// <summary>Технологии: четыре направления, пассивный прогресс, приоритет, личное ускорение, случайные открытия.</summary>
    public static class TechSystem
    {
        public static bool Available(GameSession s, NationState n, TechDef t)
        {
            if (n.Researched.Contains(t.id)) return false;
            if (s.Content.EraOrder(t.era) > s.Content.EraOrder(n.EraId)) return false;
            foreach (var p in t.prereqs) if (!n.Researched.Contains(p)) return false;
            if (t.requiresBuilding != null)
            {
                bool has = false;
                foreach (var c in s.State.Cities) if (c.NationId == n.Id && c.HasBuilding(t.requiresBuilding)) { has = true; break; }
                if (!has) return false;
            }
            return true;
        }

        public static TechDef CurrentTarget(GameSession s, NationState n)
        {
            TechDef best = null, any = null;
            foreach (var t in s.Content.TechFile.techs)
            {
                if (!Available(s, n, t)) continue;
                if (t.branch == n.PriorityBranch && (best == null || t.cost < best.cost)) best = t;
                if (any == null || t.cost < any.cost) any = t;
            }
            return best ?? any;
        }

        public static float Bonus(GameSession s, NationState n, string key)
        {
            float v = 0;
            foreach (var id in n.Researched)
                if (s.Content.Tech.TryGetValue(id, out var t) && t.bonus.TryGetValue(key, out var b)) v += b;
            return v;
        }

        public static void AddScience(GameSession s, NationState n, float amount)
        {
            if (n == null || amount <= 0) return;
            var t = CurrentTarget(s, n);
            if (t == null) return;
            if (n.IsPlayer)
            {
                // Учёные-спутники ускоряют открытия.
                foreach (var c in s.State.Companions)
                    if (c.Alive && c.Role == CompanionRoles.Scholar) amount *= 1.0f + c.Mind * 0.03f;
            }
            float cur = n.TechProgress.TryGetValue(t.id, out var p) ? p : 0;
            cur += amount;
            if (cur >= t.cost)
            {
                if (t.requiresEssence > 0 && n.Get("essence") < t.requiresEssence)
                {
                    n.TechProgress[t.id] = t.cost;
                    if (n.IsPlayer && !s.State.Narrative.Flags.Contains("need_essence_" + t.id))
                    {
                        s.State.Narrative.Flags.Add("need_essence_" + t.id);
                        s.Message($"«{t.name}» требует {t.requiresEssence} мистического ресурса в запасах.");
                    }
                    return;
                }
                if (t.requiresEssence > 0) n.Add("essence", -t.requiresEssence);
                n.TechProgress[t.id] = t.cost;
                Complete(s, n, t);
                float over = cur - t.cost;
                if (over > 1) AddScience(s, n, over * 0.5f);
            }
            else n.TechProgress[t.id] = cur;
        }

        public static void Complete(GameSession s, NationState n, TechDef t)
        {
            if (n.Researched.Contains(t.id)) return;
            n.Researched.Add(t.id);
            if (n.IsPlayer)
            {
                foreach (var r in t.unlockRecipes) if (!s.State.Hero.KnownRecipes.Contains(r)) s.State.Hero.KnownRecipes.Add(r);
                s.Emit(new GameEvent { Type = GameEventType.TechResearched, Id = t.id, Text = t.name });
                s.Message($"Открытие: {t.name}");
                Chronicle.Add(s, "tech", new Dictionary<string, string> { ["tech"] = t.name, ["nation"] = n.Name });
            }
            EraSystem.Check(s, n);
        }

        public static void Step(GameSession s, NationState n, float months)
        {
            // Небольшой базовый прогресс даже у крошечных общин.
            AddScience(s, n, 0.4f * months);
        }

        public static bool TryRandomDiscovery(GameSession s, float chance)
        {
            var n = s.PlayerNation;
            if (n == null || !s.Rng.Chance(chance)) return false;
            var list = new List<TechDef>();
            foreach (var t in s.Content.TechFile.techs) if (t.randomDiscovery && Available(s, n, t)) list.Add(t);
            if (list.Count == 0) return false;
            var pick = list[s.Rng.NextInt(list.Count)];
            s.Message($"Озарение! {pick.name}.");
            Complete(s, n, pick);
            return true;
        }

        public static bool InvestEssence(GameSession s)
        {
            var n = s.PlayerNation;
            if (n == null) return false;
            if (!HeroSystem.SpendEssence(s, 1)) { s.Message("Нет мистического ресурса."); return false; }
            AddScience(s, n, s.Balance.essence.sciencePerEssence);
            s.Message($"Ресурс вложен в науку (+{s.Balance.essence.sciencePerEssence:0}).");
            return true;
        }

        public static int CountEraTechs(GameSession s, NationState n, string era)
        {
            int k = 0;
            foreach (var id in n.Researched) if (s.Content.Tech.TryGetValue(id, out var t) && t.era == era) k++;
            return k;
        }

        public static bool AllKnowledge(GameSession s, NationState n)
        {
            foreach (var t in s.Content.TechFile.techs)
                if (t.branch == "knowledge" && s.Content.Era[t.era].implemented && !n.Researched.Contains(t.id)) return false;
            return true;
        }
    }

    /// <summary>Смена эпохи — сильный сюжетный момент: новая глава, роль, облик мира, предложение прыжка.</summary>
    public static class EraSystem
    {
        public static bool CanAdvance(GameSession s, NationState n, out EraDef next)
        {
            next = s.Content.NextEra(n.EraId);
            if (next == null || !next.implemented) return false;
            if (TechSystem.CountEraTechs(s, n, n.EraId) < next.requiresTechs) return false;
            if (next.requiresStage != null)
            {
                int need = s.Content.StageIndex[next.requiresStage];
                bool ok = false;
                foreach (var c in s.State.Cities) if (c.NationId == n.Id && c.StageIndex >= need) { ok = true; break; }
                if (!ok) return false;
            }
            return true;
        }

        public static void Check(GameSession s, NationState n)
        {
            if (n == null || !CanAdvance(s, n, out var next)) return;
            n.EraId = next.id;
            Chronicle.Add(s, "era", new Dictionary<string, string> { ["nation"] = n.Name, ["era"] = next.name });
            if (!n.IsPlayer) return;
            s.State.Chapter++;
            HeroSystem.SetRole(s, next.role);
            s.Emit(new GameEvent { Type = GameEventType.EraChanged, Id = next.id, Text = next.name });
            s.Message($"Наступает новая эпоха: {next.name}");
            s.State.Narrative.JumpOptions = new List<int>(s.Balance.time.jumpOptions);
            s.State.Narrative.JumpReason = "era";
            EventSystem.Fire(s, "era_" + next.id, new DialogueState());
            s.Emit(new GameEvent { Type = GameEventType.Autosave });
            // Стадии, ожидавшие эпоху, могут сразу вырасти.
            foreach (var c in s.State.Cities) if (c.NationId == n.Id) CitySystem.TryAdvanceStage(s, c);
        }
    }
}
