using System;
using System.Collections.Generic;
using Immortal.Core.Civilization;
using Immortal.Core.Content;
using Immortal.Core.Narrative;

namespace Immortal.Core.Character
{
    public enum RecruitOutcome { Full, Partial, Fail, Crit }

    /// <summary>
    /// Вербовка: шанс = база + вложения + черты + репутация + случайность.
    /// Подходы: дар, трюк, помощь с проблемой, давление.
    /// </summary>
    public static class RecruitmentSystem
    {
        public static readonly string[] Approaches = { "gift", "trick", "help", "pressure" };

        public static RecruitDef Def(GameSession s, NpcState npc) =>
            npc.RecruitDefId != null && s.Content.Recruits.TryGetValue(npc.RecruitDefId, out var d) ? d : null;

        public static string GiftItem(GameSession s, NpcState npc)
        {
            var h = s.State.Hero; int need = s.Balance.recruit.giftAmount;
            if (npc.Need != null && h.Count(npc.Need) >= need) return npc.Need;
            foreach (var alt in new[] { "meat", "berries", "hide", "wood" })
                if (h.Count(alt) >= need) return alt;
            return null;
        }

        public static bool QuestDone(GameSession s, NpcState npc)
        {
            if (npc.QuestDone) return true;
            var q = Def(s, npc)?.quest;
            if (q == null || !npc.QuestStarted) return false;
            if (q.type == "bring") return s.State.Hero.Count(q.item) >= q.amount;
            if (q.type == "clear")
                foreach (var p in s.State.Pois)
                    if (p.Cleared && s.Content.Poi[p.DefId].kind == q.poiKind && Vec2.Distance(p.Pos, npc.Home) < 110f) return true;
            return false;
        }

        /// <summary>Расчёт шанса с разбивкой на составляющие (для подсказки в интерфейсе).</summary>
        public static float Chance(GameSession s, NpcState npc, string approach, List<string> breakdown = null)
        {
            var rb = s.Balance.recruit; var h = s.State.Hero;
            var def = Def(s, npc);
            float c = npc.IsDescendant ? 0.3f + npc.InheritedRelation * 0.004f : (def?.baseChance ?? 0.35f);
            void Add(float v, string why) { c += v; if (breakdown != null && Math.Abs(v) > 0.001f) breakdown.Add($"{why}: {(v > 0 ? "+" : "")}{v * 100:0}%"); }
            breakdown?.Add($"база: {c * 100:0}%");

            switch (approach)
            {
                case "gift":
                    string gift = GiftItem(s, npc);
                    if (gift != null) Add(rb.giftBonus, "дар");
                    if (gift != null && gift == npc.Need) Add(rb.needMatchBonus, "угадана нужда");
                    break;
                case "trick":
                    Add(HeroSystem.EffMind(s) * 0.02f, "ум");
                    if (npc.Character == "naive") Add(0.2f, "доверчив");
                    if (npc.Character == "wary" || npc.Character == "clever") Add(-0.15f, "осторожен");
                    break;
                case "help":
                    if (QuestDone(s, npc)) Add(rb.helpBonus, "помощь оказана");
                    else Add(-0.3f, "помощь не оказана");
                    break;
                case "pressure":
                    Add(HeroSystem.EffBody(s) * 0.02f + h.Will * 0.02f, "сила и воля");
                    if (npc.Character == "timid") Add(0.2f, "робок");
                    if (npc.Character == "proud") Add(-0.2f, "горд");
                    break;
            }
            Add(-rb.distrustPenalty * npc.Distrust, "недоверие");
            Add(npc.KnownTraits.Count * 0.04f, "понимание человека");
            Add((float)h.Reputation * rb.repFactor, "репутация");
            Add(HeroSystem.EffSpeech(s) * rb.speechFactor, "речь");
            Add(-0.05f * npc.Attempts, "прошлые неудачи");
            return MathUtil.Clamp(c, 0.02f, 0.95f);
        }

        public static float EnergyCost(GameSession s, string approach)
        {
            var rb = s.Balance.recruit;
            return approach == "trick" ? rb.trickEnergy : approach == "help" ? rb.helpEnergy : approach == "pressure" ? rb.pressureEnergy : 0;
        }

        public static bool CanUse(GameSession s, NpcState npc, string approach, out string reason)
        {
            reason = null;
            if (s.State.Months < npc.CooldownUntilMonths) { reason = "человек пока не хочет говорить"; return false; }
            if (s.State.Hero.Energy < EnergyCost(s, approach)) { reason = "не хватает энергии"; return false; }
            if (approach == "gift" && GiftItem(s, npc) == null) { reason = $"нужно {s.Balance.recruit.giftAmount} ед. еды, шкур или нужного предмета"; return false; }
            if (approach == "help" && !QuestDone(s, npc)) { reason = "сначала помогите с бедой"; return false; }
            return true;
        }

        /// <summary>Разрешает попытку: платит цену, бросает кубик, применяет исход.</summary>
        public static RecruitOutcome Resolve(GameSession s, NpcState npc, string approach, out float chance)
        {
            var rb = s.Balance.recruit; var h = s.State.Hero;
            chance = Chance(s, npc, approach);
            h.Energy -= EnergyCost(s, approach);
            if (approach == "gift") { var g = GiftItem(s, npc); if (g != null) h.Add(g, -rb.giftAmount); }
            if (approach == "help")
            {
                var q = Def(s, npc)?.quest;
                if (q != null && q.type == "bring" && !npc.QuestDone) h.Add(q.item, -q.amount);
                npc.QuestDone = true;
            }

            float roll = s.Rng.NextFloat();
            float critT = rb.critBase - (approach == "pressure" ? rb.critPressure : 0f);
            RecruitOutcome outcome;
            if (roll < chance - rb.fullMargin) outcome = RecruitOutcome.Full;
            else if (roll < chance) outcome = RecruitOutcome.Partial;
            else if (roll >= Math.Max(critT, chance)) outcome = RecruitOutcome.Crit;
            else outcome = RecruitOutcome.Fail;
            Apply(s, npc, outcome);
            HeroSystem.GainSkill(s, "speech", 1.5f);
            return outcome;
        }

        public static void Apply(GameSession s, NpcState npc, RecruitOutcome outcome)
        {
            var rb = s.Balance.recruit;
            switch (outcome)
            {
                case RecruitOutcome.Full: Recruit(s, npc, rb.fullRelation); break;
                case RecruitOutcome.Partial: Recruit(s, npc, rb.partialRelation); break;
                case RecruitOutcome.Fail:
                    npc.Attempts++;
                    npc.CooldownUntilMonths = s.State.Months + rb.failCooldownMonths;
                    s.State.Hero.Energy = Math.Max(0, s.State.Hero.Energy - 5);
                    break;
                case RecruitOutcome.Crit:
                    npc.Attempts++;
                    npc.Hostile = true;
                    npc.CooldownUntilMonths = s.State.Months + rb.failCooldownMonths * 6;
                    s.State.Hero.Reputation -= 5;
                    var u = s.FindUnit(npc.UnitId);
                    if (u != null) { u.Team = Teams.Wild; u.Ai = AiState.Chase; u.TargetId = s.State.Hero.UnitId; u.Provoked = true; }
                    break;
            }
        }

        public static string Observe(GameSession s, NpcState npc)
        {
            var def = Def(s, npc);
            string[] traits = { "need", "distrust", "character" };
            foreach (var t in traits)
            {
                if (npc.KnownTraits.Contains(t)) continue;
                npc.KnownTraits.Add(t);
                int idx = Array.IndexOf(traits, t);
                if (def != null && idx < def.observeHints.Count && !npc.IsDescendant) return def.observeHints[idx];
                return t switch
                {
                    "need" => $"Похоже, {npc.Name} нуждается: {s.Content.ItemName(npc.Need ?? "berries")}.",
                    "distrust" => npc.Distrust >= 2 ? $"{npc.Name} держится настороженно." : $"{npc.Name} открыт к разговору.",
                    _ => $"Характер: {CharacterName(npc.Character)}."
                };
            }
            return $"Вы уже хорошо понимаете, что за человек {npc.Name}.";
        }

        public static string CharacterName(string c) => c switch
        {
            "naive" => "доверчивый", "wary" => "осторожный", "clever" => "хитрый", "timid" => "робкий",
            "proud" => "гордый", "kind" => "добрый", "greedy" => "жадный", _ => "обычный"
        };

        public static int NamedAlive(GameSession s)
        {
            int n = 0; foreach (var c in s.State.Companions) if (c.Alive) n++; return n;
        }

        public static void Recruit(GameSession s, NpcState npc, float relation)
        {
            npc.Recruited = true;
            var unit = s.FindUnit(npc.UnitId);
            if (unit != null) s.RemoveUnit(unit);
            npc.UnitId = 0;
            var def = Def(s, npc);
            HeroSystem.GainXp(s, 25);
            if (def?.memory != null) HeroSystem.AddMemory(s, def.memory);

            if (NamedAlive(s) >= s.Balance.recruit.namedCompanionLimit)
            {
                var cap = s.Capital(s.PlayerNation);
                if (cap != null) cap.Population += 3;
                s.Message($"{npc.Name} с семьёй присоединяется к общине.");
                s.Emit(new GameEvent { Type = GameEventType.Recruited, Text = npc.Name });
                return;
            }
            var c = new CompanionState
            {
                Id = s.State.NewId("comp"), Name = npc.Name, RecruitDefId = npc.RecruitDefId, NpcId = npc.Id,
                Need = npc.Need, Character = npc.Character, Distrust = npc.Distrust, Relation = relation + npc.InheritedRelation * 0.5f,
                FaceId = npc.FaceId, Generation = npc.Generation, Bio = def?.bio,
                ParentId = npc.AncestorCompanionId
            };
            float startAge = def?.startAge ?? 22;
            if (npc.IsDescendant) startAge = 20;
            c.BirthMonths = s.State.Months - startAge * 12;
            c.LifespanYears = (def?.lifespan ?? 60) + s.Rng.Range(-5, 6);
            if (def != null) { c.Combat = def.combat; c.Gathering = def.gathering; c.Speech = def.speech; c.Mind = def.mind; }
            s.State.Companions.Add(c);
            int party = s.State.Hero.PartyCompanionUnits.Count;
            CompanionSystem.SetRole(s, c, party < s.Balance.companions.maxParty ? CompanionRoles.Party : CompanionRoles.Camp, null);
            s.Message($"{c.Name} присоединяется к вам ({(relation > 0 ? "с охотой" : "неохотно")}).");
            s.Emit(new GameEvent { Type = GameEventType.Recruited, Text = c.Name });
            Chronicle.Add(s, "recruit", new Dictionary<string, string> { ["name"] = c.Name });
            var cap2 = s.Capital(s.PlayerNation);
            if (cap2 != null) cap2.Population += 1;
        }
    }
}
