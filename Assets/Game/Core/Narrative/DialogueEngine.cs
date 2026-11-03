using System;
using System.Collections.Generic;
using System.Text;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using Immortal.Core.Content;
using Immortal.Core.Time;

namespace Immortal.Core.Narrative
{
    public sealed class OptionView
    {
        public int Index; public string Text; public bool Enabled; public string Hint;
    }

    /// <summary>
    /// Один движок для вербовки, событий и переговоров: JSON-граф узлов, вариантов, условий и эффектов.
    /// </summary>
    public static class DialogueEngine
    {
        public static void Start(GameSession s, string dialogueId, DialogueState ctx)
        {
            if (!s.Content.Dialogues.TryGetValue(dialogueId, out var d)) { s.Message($"[нет диалога {dialogueId}]"); return; }
            ctx = ctx ?? new DialogueState();
            ctx.DialogueId = dialogueId;
            ctx.NodeId = d.start;
            var ns = s.State.Narrative;
            if (ns.Active != null) { ns.Queue.Add(ctx); return; }
            ns.Active = ctx;
            s.Emit(new GameEvent { Type = GameEventType.DialogueStarted, Id = dialogueId });
            EnterNode(s, ctx.NodeId);
            if (s.AutoResolveDialogues) AutoResolve(s);
        }

        public static DialogueDef Current(GameSession s) =>
            s.State.Narrative.Active != null && s.Content.Dialogues.TryGetValue(s.State.Narrative.Active.DialogueId, out var d) ? d : null;

        public static NodeDefN CurrentNode(GameSession s)
        {
            var d = Current(s); if (d == null) return null;
            var id = s.State.Narrative.Active.NodeId;
            foreach (var n in d.nodes) if (n.id == id) return n;
            return null;
        }

        public static string Speaker(GameSession s)
        {
            var n = CurrentNode(s);
            return n?.speaker != null ? Templates.Fill(s, n.speaker, s.State.Narrative.Active) : "";
        }

        public static string Text(GameSession s)
        {
            var n = CurrentNode(s);
            return n != null ? Templates.Fill(s, n.text, s.State.Narrative.Active) : "";
        }

        public static List<OptionView> Options(GameSession s)
        {
            var list = new List<OptionView>();
            var node = CurrentNode(s); var ctx = s.State.Narrative.Active;
            if (node == null) return list;
            for (int i = 0; i < node.options.Count; i++)
            {
                var o = node.options[i];
                if (!Conditions.All(s, o.conds, ctx)) continue;
                var v = new OptionView { Index = i, Text = Templates.Fill(s, o.text, ctx), Enabled = true };
                if (o.check != null && o.check.kind == "recruit")
                {
                    var npc = s.Npc(ctx.NpcId);
                    if (npc != null)
                    {
                        v.Enabled = RecruitmentSystem.CanUse(s, npc, o.check.approach, out var why);
                        float ch = RecruitmentSystem.Chance(s, npc, o.check.approach);
                        v.Hint = v.Enabled ? $"шанс {ch * 100:0}%" : why;
                    }
                }
                else if (o.check != null)
                {
                    v.Hint = $"шанс {CheckChance(s, o.check, ctx) * 100:0}%";
                }
                list.Add(v);
            }
            return list;
        }

        public static void Choose(GameSession s, int optionIndex)
        {
            var node = CurrentNode(s); var ctx = s.State.Narrative.Active;
            if (node == null || optionIndex < 0 || optionIndex >= node.options.Count) return;
            var o = node.options[optionIndex];
            if (!Conditions.All(s, o.conds, ctx)) return;
            if (o.check != null && o.check.kind == "recruit")
            {
                var npc = s.Npc(ctx.NpcId);
                string why = "Нельзя.";
                if (npc == null || !RecruitmentSystem.CanUse(s, npc, o.check.approach, out why)) { s.Message(why ?? "Нельзя."); return; }
            }
            Effects.Apply(s, o.effects, ctx);
            if (s.State.Narrative.Active != ctx) return; // эффект закрыл диалог
            string next = o.next;
            if (o.check != null)
            {
                var outcome = ResolveCheck(s, o.check, ctx);
                ctx.LastOutcome = outcome.ToString();
                next = outcome switch
                {
                    RecruitOutcome.Full => o.check.full,
                    RecruitOutcome.Partial => o.check.partial ?? o.check.full,
                    RecruitOutcome.Fail => o.check.fail,
                    _ => o.check.crit ?? o.check.fail
                };
            }
            if (next == null || next == "end") { End(s); return; }
            ctx.NodeId = next;
            EnterNode(s, next);
        }

        static void EnterNode(GameSession s, string nodeId)
        {
            var node = CurrentNode(s);
            if (node == null) { End(s); return; }
            var ctx = s.State.Narrative.Active;
            Effects.Apply(s, node.effects, ctx);
            Templates.Remember(s, Templates.Fill(s, node.text, ctx));
        }

        public static void End(GameSession s)
        {
            var ns = s.State.Narrative;
            if (ns.Active == null) return;
            ns.Active = null;
            s.Emit(new GameEvent { Type = GameEventType.DialogueEnded });
            if (ns.Queue.Count > 0)
            {
                var next = ns.Queue[0];
                ns.Queue.RemoveAt(0);
                Start(s, next.DialogueId, next);
            }
        }

        /// <summary>Для ботов и тестов: выбирает первый доступный вариант до конца диалога.</summary>
        public static void AutoResolve(GameSession s, int maxSteps = 30)
        {
            for (int i = 0; i < maxSteps && s.State.Narrative.Active != null; i++)
            {
                var opts = Options(s);
                var pick = opts.Find(o => o.Enabled);
                if (pick == null) { End(s); return; }
                Choose(s, pick.Index);
            }
            if (s.State.Narrative.Active != null) End(s);
        }

        public static float CheckChance(GameSession s, CheckDef c, DialogueState ctx)
        {
            var h = s.State.Hero;
            float value = 0;
            if (c.attr != null) value = c.attr == "body" ? HeroSystem.EffBody(s) : c.attr == "mind" ? HeroSystem.EffMind(s) : h.Will;
            if (c.skill != null) value = c.skill == "speech" ? HeroSystem.EffSpeech(s) / 8f : HeroSystem.Skill(h, c.skill) / 8f;
            float ch = 0.35f + value * 0.06f - c.difficulty * 0.5f + (float)h.Reputation * 0.002f;
            if (c.kind == "leader" && ctx.NationId != null)
            {
                var n = s.Nation(ctx.NationId);
                if (n != null) ch += Diplomacy.Rel(s, n.Id, s.State.PlayerNationId ?? "__hero").Attitude / 200f;
            }
            return MathUtil.Clamp(ch, 0.05f, 0.95f);
        }

        static RecruitOutcome ResolveCheck(GameSession s, CheckDef c, DialogueState ctx)
        {
            if (c.kind == "recruit")
            {
                var npc = s.Npc(ctx.NpcId);
                var r = RecruitmentSystem.Resolve(s, npc, c.approach, out float chance);
                ctx.LastChance = chance;
                return r;
            }
            float ch = CheckChance(s, c, ctx);
            ctx.LastChance = ch;
            float roll = s.Rng.NextFloat();
            if (roll < ch - 0.15f) return RecruitOutcome.Full;
            if (roll < ch) return RecruitOutcome.Partial;
            if (roll > 0.95f) return RecruitOutcome.Crit;
            return RecruitOutcome.Fail;
        }
    }

    /// <summary>Подстановка имён предка, потомка, города и т.п.</summary>
    public static class Templates
    {
        public static string Fill(GameSession s, string t, DialogueState ctx)
        {
            if (string.IsNullOrEmpty(t) || t.IndexOf('{') < 0) return t ?? "";
            var sb = new StringBuilder(t.Length + 32);
            int i = 0;
            while (i < t.Length)
            {
                char ch = t[i];
                if (ch == '{')
                {
                    int j = t.IndexOf('}', i);
                    if (j > i) { sb.Append(Value(s, t.Substring(i + 1, j - i - 1), ctx)); i = j + 1; continue; }
                }
                sb.Append(ch); i++;
            }
            return sb.ToString();
        }

        static string Value(GameSession s, string key, DialogueState ctx)
        {
            var npc = s.Npc(ctx?.NpcId);
            var comp = s.Companion(ctx?.CompanionId);
            var nation = s.Nation(ctx?.NationId);
            var city = s.City(ctx?.CityId) ?? s.Capital(s.PlayerNation);
            var def = npc != null ? RecruitmentSystem.Def(s, npc) : null;
            switch (key)
            {
                case "hero": return s.State.Hero.Name;
                case "npc": return npc?.Name ?? comp?.Name ?? "незнакомец";
                case "companion": return comp?.Name ?? "спутник";
                case "city": return city?.Name ?? "ваш лагерь";
                case "capital": return s.Capital(s.PlayerNation)?.Name ?? "ваш лагерь";
                case "nation": return nation?.Name ?? s.PlayerNation?.Name ?? "община";
                case "leader": return nation?.Leader.Name ?? "правитель";
                case "leader_title": return nation?.Leader.Title ?? "правитель";
                case "ancestor": return npc?.AncestorName ?? s.Companion(comp?.ParentId)?.Name ?? comp?.Name ?? "предок";
                case "descendant": return npc?.Name ?? "потомок";
                case "year": return s.Year.ToString();
                case "date": return s.DateText;
                case "need": return s.Content.ItemName(npc?.Need ?? "berries");
                case "greeting": return def?.greeting ?? $"{npc?.Name} смотрит на вас настороженно.";
                case "bio": return comp?.Bio ?? def?.bio ?? "";
                case "quest": return def?.quest?.text ?? "Мне ничего не нужно.";
                case "quest_done": return def?.quest?.doneText ?? "Спасибо.";
                case "chance": return $"{(ctx?.LastChance ?? 0) * 100:0}%";
                case "era": return s.PlayerEraDef.name;
                case "age": return $"{HeroSystem.ApparentAge(s.State.Hero):0}";
                case "rival": foreach (var n in s.State.Nations) if (!n.IsPlayer) return n.Name; return "соседи";
                case "rival_leader": foreach (var n in s.State.Nations) if (!n.IsPlayer) return n.Leader.Name; return "сосед";
                case "echo": return SimulationHints.EchoLine(s);
                case "suspicion": return SimulationHints.LeaderSuspicion(s, nation);
                case "years_dead": return comp != null && !comp.Alive ? $"{(s.State.Months - comp.DeathMonths) / 12:0}" : "0";
                case "relation": return comp != null ? RelationWord(comp.Relation) : npc != null ? RelationWord(npc.InheritedRelation) : "";
                case "memory_ruins": return s.State.Hero.Memories.Contains("ruins_carvings") ? "Вы помните те же знаки в руинах." : "Знаки ничего вам не говорят.";
                case "observe": return npc != null ? RecruitmentSystem.Observe(s, npc) : "";
                case "outcome": return ctx?.LastOutcome ?? "";
                case "lineage_line": return npc != null && npc.InheritedRelation < -10 ? $"Мой предок {npc.AncestorName} умер с обидой на тебя." : $"Мне рассказывали, что {npc?.AncestorName} шёл за тобой до конца.";
                default: return "{" + key + "}";
            }
        }

        public static string RelationWord(float r) => r > 50 ? "предан вам" : r > 15 ? "дружелюбен" : r > -15 ? "сдержан" : r > -50 ? "обижен" : "враждебен";

        public static void Remember(GameSession s, string line)
        {
            var list = s.State.Narrative.RecentLines;
            if (string.IsNullOrEmpty(line) || line.Length > 160) return;
            list.Add(line);
            if (list.Count > 30) list.RemoveAt(0);
        }
    }

    public static class Conditions
    {
        public static bool All(GameSession s, List<CondDef> conds, DialogueState ctx)
        {
            if (conds == null) return true;
            foreach (var c in conds) if (!Pass(s, c, ctx)) return false;
            return true;
        }

        public static bool Pass(GameSession s, CondDef c, DialogueState ctx)
        {
            var st = s.State; var h = st.Hero; var ns = st.Narrative;
            if (c.flag != null && !ns.Flags.Contains(c.flag)) return false;
            if (c.notFlag != null && ns.Flags.Contains(c.notFlag)) return false;
            if (c.era != null && s.PlayerEra != c.era) return false;
            if (c.minEra != null && s.Content.EraOrder(s.PlayerEra) < s.Content.EraOrder(c.minEra)) return false;
            if (c.item != null && h.Count(c.item) + (int)(s.PlayerNation?.Get(c.item) ?? 0) < c.itemMin) return false;
            if (c.attr != null)
            {
                float v = c.attr == "body" ? HeroSystem.EffBody(s) : c.attr == "mind" ? HeroSystem.EffMind(s) : h.Will;
                if (v < c.attrMin) return false;
            }
            if (c.skill != null && HeroSystem.Skill(h, c.skill) < c.skillMin) return false;
            if (c.energy > 0 && h.Energy < c.energy) return false;
            if (c.memory != null && !h.Memories.Contains(c.memory)) return false;
            if (c.notMemory != null && h.Memories.Contains(c.notMemory)) return false;
            if (c.taboo != null && ns.Taboo != c.taboo) return false;
            if (c.notTaboo == true && ns.Taboo != "none") return false;
            if (c.tabooActive.HasValue && (ns.Taboo != "none") != c.tabooActive.Value) return false;
            if (c.role != null && h.Role != c.role) return false;
            if (c.minRole != null && Roles.Rank(h.Role) < Roles.Rank(c.minRole)) return false;
            if (c.hints >= 0 && ns.HintsSeen < c.hints) return false;
            if (c.reputationMin > -999 && h.Reputation < c.reputationMin) return false;
            if (c.rejuvenation.HasValue && h.RejuvenationUnlocked != c.rejuvenation.Value) return false;
            if (c.hasNation.HasValue && (st.PlayerNationId != null) != c.hasNation.Value) return false;
            if (c.inSmoke.HasValue && h.InSmoke != c.inSmoke.Value) return false;
            var npc = s.Npc(ctx?.NpcId);
            if (c.hasNeedItem.HasValue && (npc == null || (RecruitmentSystem.GiftItem(s, npc) != null) != c.hasNeedItem.Value)) return false;
            if (c.questDone.HasValue && (npc == null || RecruitmentSystem.QuestDone(s, npc) != c.questDone.Value)) return false;
            if (c.questStarted.HasValue && (npc == null || npc.QuestStarted != c.questStarted.Value)) return false;
            if (c.notKnownAllTraits != null && npc != null && npc.KnownTraits.Count >= 3) return false;
            if (c.relationMin > -999)
            {
                var comp = s.Companion(ctx?.CompanionId);
                float rel = comp != null ? comp.Relation : npc != null ? npc.InheritedRelation : 0;
                if (rel < c.relationMin) return false;
            }
            var nation = s.Nation(ctx?.NationId);
            if (c.atWar.HasValue)
            {
                bool war = nation != null && st.PlayerNationId != null && Diplomacy.AtWar(s, nation.Id, st.PlayerNationId);
                if (war != c.atWar.Value) return false;
            }
            if (c.leaderAlive.HasValue && (nation == null || nation.Leader.Alive != c.leaderAlive.Value)) return false;
            if (c.cityStageMin != null)
            {
                var cap = s.Capital(s.PlayerNation);
                if (cap == null || cap.StageIndex < s.Content.StageIndex[c.cityStageMin]) return false;
            }
            if (c.chance >= 0 && !s.Rng.Chance(c.chance)) return false;
            return true;
        }
    }

    public static class Effects
    {
        public static void Apply(GameSession s, List<EffectDef> effects, DialogueState ctx)
        {
            if (effects == null) return;
            foreach (var e in effects) Apply(s, e, ctx);
        }

        public static void Apply(GameSession s, EffectDef e, DialogueState ctx)
        {
            var st = s.State; var h = st.Hero; var ns = st.Narrative;
            var npc = s.Npc(ctx?.NpcId);
            var comp = s.Companion(ctx?.CompanionId);
            var nation = s.Nation(ctx?.NationId ?? e.attitudeNation);
            var city = s.City(ctx?.CityId);

            if (e.setFlag != null) ns.Flags.Add(e.setFlag);
            if (e.clearFlag != null) ns.Flags.Remove(e.clearFlag);
            if (e.item != null && e.amount != 0)
            {
                if (e.amount < 0)
                {
                    int need = -e.amount, fromHero = Math.Min(need, h.Count(e.item));
                    h.Add(e.item, -fromHero);
                    if (need - fromHero > 0) s.PlayerNation?.Add(e.item, -(need - fromHero));
                }
                else { h.Add(e.item, e.amount); if (e.item == "essence") HeroSystem.OnEssencePicked(s); }
            }
            if (e.energy != 0) h.Energy = Math.Max(0, h.Energy + e.energy);
            if (e.vitality != 0) h.VitalityYears = Math.Min(h.MaxVitalityYears, Math.Max(0.1, h.VitalityYears + e.vitality));
            if (e.rep != 0) h.Reputation += e.rep;
            if (e.xp != 0) HeroSystem.GainXp(s, (int)e.xp);
            if (e.memory != null) HeroSystem.AddMemory(s, e.memory);
            if (e.forgetMemory != null) h.Memories.Remove(e.forgetMemory);
            if (e.taboo != null) { ns.Taboo = e.taboo; Chronicle.Add(s, "taboo_" + e.taboo, null); }
            if (e.science != 0 && s.PlayerNation != null) TechSystem.AddScience(s, s.PlayerNation, e.science);
            if (e.influence != 0 && s.PlayerNation != null) s.PlayerNation.Influence += e.influence;
            if (e.rejuvenationUnlock) { h.RejuvenationUnlocked = true; }
            if (e.observe && npc != null) { var txt = RecruitmentSystem.Observe(s, npc); s.Message(txt); }
            if (e.questStart && npc != null) npc.QuestStarted = true;
            if (e.questComplete && npc != null) npc.QuestDone = true;
            if (e.npcCooldown != 0 && npc != null) npc.CooldownUntilMonths = st.Months + e.npcCooldown;
            if (e.npcHostile && npc != null) RecruitmentSystem.Apply(s, npc, RecruitOutcome.Crit);
            if (e.recruit != null && npc != null && !npc.Recruited)
                RecruitmentSystem.Recruit(s, npc, e.recruit == "full" ? s.Balance.recruit.fullRelation : s.Balance.recruit.partialRelation);
            if (e.relation != 0 && comp != null) comp.Relation = MathUtil.Clamp(comp.Relation + e.relation, -100, 100);
            if (e.relation != 0 && comp == null && npc != null) npc.InheritedRelation += e.relation;
            if (e.companionRole != null && comp != null)
                CompanionSystem.SetRole(s, comp, e.companionRole, e.companionTarget ?? (e.companionRole == CompanionRoles.Governor ? ctx?.CityId : null));
            if (e.companionLifespan != 0 && comp != null) comp.LifespanYears += e.companionLifespan;
            if (e.killCompanion && comp != null) CompanionSystem.Die(s, comp, "по приказу правителя");
            if (e.removeCompanion && comp != null && comp.Role == CompanionRoles.Party) CompanionSystem.SetRole(s, comp, CompanionRoles.Camp, null);
            if (e.loyalty != 0 && city != null) city.Loyalty = MathUtil.Clamp(city.Loyalty + e.loyalty, 0, 100);
            if (e.allLoyalty != 0 || (e.loyalty != 0 && city == null))
                foreach (var c in st.Cities) if (c.NationId == st.PlayerNationId) c.Loyalty = MathUtil.Clamp(c.Loyalty + (e.allLoyalty != 0 ? e.allLoyalty : e.loyalty), 0, 100);
            if (e.attitude != 0 && nation != null) NationSystem.Remember(s, nation, null, e.attitude);
            if (e.treaty != null && nation != null && st.PlayerNationId != null)
            {
                var r = Diplomacy.Rel(s, nation.Id, st.PlayerNationId);
                if (e.treaty == "peace") Diplomacy.MakePeace(s, nation.Id, st.PlayerNationId);
                else if (!r.Agreements.Contains(e.treaty)) { r.Agreements.Add(e.treaty); Chronicle.Add(s, "treaty", new Dictionary<string, string> { ["a"] = s.PlayerNation.Name, ["b"] = nation.Name, ["treaty"] = Diplomacy.AgreementName(e.treaty) }); }
            }
            if (e.startWar && nation != null && st.PlayerNationId != null) Diplomacy.DeclareWar(s, st.PlayerNationId, nation.Id);
            if (e.makePeace && nation != null && st.PlayerNationId != null) Diplomacy.MakePeace(s, nation.Id, st.PlayerNationId);
            if (e.killLeader && nation != null)
            {
                var u = s.FindUnit(nation.Leader.UnitId);
                if (u != null) Action.CombatSystem.Kill(s, u, s.HeroUnit);
                else NationSystem.OnLeaderKilled(s, nation.Id, true);
            }
            if (e.captureLeader && nation != null) NationSystem.CaptureLeader(s, nation);
            if (e.cityIndependent && city != null) CitySystem.Secede(s, city);
            if (e.cityReturn && city != null && s.PlayerNation != null) CitySystem.JoinNation(s, city, s.PlayerNation, 55);
            if (e.spawn != null && s.HeroUnit != null && !s.InTimeJump)
                for (int i = 0; i < Math.Max(1, e.spawnCount); i++)
                {
                    var pos = s.HeroUnit.Pos + Vec2.FromAngle(i * 2.1f + s.Rng.NextFloat()) * s.Rng.Range(9f, 13f);
                    var u = Action.SpawnSystem.CreateEnemy(s, e.spawn, pos, Teams.Wild);
                    u.TargetId = s.HeroUnit.Id; u.Ai = AiState.Chase; u.Home = s.HeroUnit.Pos;
                }
            if (e.hint != null) SimulationHints.Register(s, e.hint, null);
            if (e.chronicle != null) Chronicle.AddText(s, Templates.Fill(s, e.chronicle, ctx), e.chronicleCategory ?? "event");
            if (e.message != null) s.Message(Templates.Fill(s, e.message, ctx));
            if (e.offerJump) { ns.JumpOptions = new List<int>(s.Balance.time.jumpOptions); ns.JumpReason = "choice"; }
            if (e.ending != null) EndingSystem.Trigger(s, e.ending);
            if (e.startDialogue != null) { DialogueEngine.End(s); DialogueEngine.Start(s, e.startDialogue, new DialogueState { NpcId = ctx?.NpcId, CompanionId = ctx?.CompanionId, NationId = ctx?.NationId, CityId = ctx?.CityId }); return; }
            if (e.timeJump > 0 && !s.InTimeJump) { DialogueEngine.End(s); TimeJumpSystem.Jump(s, e.timeJump, ns.JumpReason ?? "choice"); return; }
            if (e.endDialogue) DialogueEngine.End(s);
        }
    }
}
