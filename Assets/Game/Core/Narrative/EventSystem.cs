using System;
using System.Collections.Generic;
using Immortal.Core.Content;

namespace Immortal.Core.Narrative
{
    /// <summary>Сюжетные события из events.json: триггер, условия, шанс, однократность, перезарядка.</summary>
    public static class EventSystem
    {
        public static bool Fire(GameSession s, string trigger, DialogueState ctx)
        {
            if (s.State.GameOver) return false;
            var ns = s.State.Narrative;
            foreach (var ev in s.Content.EventsFile.events)
            {
                if (ev.trigger != trigger) continue;
                if (!Eligible(s, ev, ctx)) continue;
                if (ev.chance < 1f && !s.Rng.Chance(ev.chance)) continue;
                ns.FiredEvents.Add(ev.id);
                if (ev.cooldownYears > 0) ns.EventCooldowns[ev.id] = s.State.Months + ev.cooldownYears * 12;
                var c = Copy(ctx);
                c.EventId = ev.id;
                if (s.InTimeJump) { AutoResolveSilently(s, ev, c); return true; }
                DialogueEngine.Start(s, ev.dialogue, c);
                return true;
            }
            return false;
        }

        static bool Eligible(GameSession s, EventDef ev, DialogueState ctx)
        {
            var ns = s.State.Narrative;
            if (ev.once && ns.FiredEvents.Contains(ev.id)) return false;
            if (ns.EventCooldowns.TryGetValue(ev.id, out var until) && s.State.Months < until) return false;
            return Conditions.All(s, ev.conds, ctx);
        }

        /// <summary>Во время прыжка во времени события не открываются, а разрешаются первым вариантом.</summary>
        static void AutoResolveSilently(GameSession s, EventDef ev, DialogueState ctx)
        {
            var ns = s.State.Narrative;
            var saved = ns.Active;
            ns.Active = null;
            bool auto = s.AutoResolveDialogues;
            s.AutoResolveDialogues = true;
            DialogueEngine.Start(s, ev.dialogue, ctx);
            s.AutoResolveDialogues = auto;
            ns.Active = saved;
        }

        public static void Monthly(GameSession s)
        {
            if (s.State.Narrative.Active != null || s.InTimeJump) return;
            Fire(s, "monthly", RandomContext(s));
        }

        /// <summary>Контекст для фоновых событий: случайный живой спутник, свой город и соперник — чтобы события говорили о людях.</summary>
        public static DialogueState RandomContext(GameSession s)
        {
            var ctx = new DialogueState();
            var comps = s.State.Companions.FindAll(c => c.Alive && c.Role != CompanionRoles.Rebel);
            if (comps.Count > 0) ctx.CompanionId = comps[s.Rng.NextInt(comps.Count)].Id;
            var cities = s.State.Cities.FindAll(c => c.NationId != null && c.NationId == s.State.PlayerNationId);
            if (cities.Count > 0) ctx.CityId = cities[s.Rng.NextInt(cities.Count)].Id;
            var rivals = s.State.Nations.FindAll(n => !n.IsPlayer && !n.Destroyed);
            if (rivals.Count > 0) ctx.NationId = rivals[s.Rng.NextInt(rivals.Count)].Id;
            return ctx;
        }

        static DialogueState Copy(DialogueState c) => c == null ? new DialogueState() : new DialogueState
        {
            NpcId = c.NpcId, CompanionId = c.CompanionId, NationId = c.NationId, CityId = c.CityId, PoiId = c.PoiId
        };
    }

    /// <summary>Хроника мира: шаблоны из chronicle.json с подстановкой.</summary>
    public static class Chronicle
    {
        public static void Add(GameSession s, string category, Dictionary<string, string> vars)
        {
            var tpl = s.Content.ChronicleFile.templates;
            if (!tpl.TryGetValue(category, out var list) || list.Count == 0) return;
            string text = list[s.Rng.NextInt(list.Count)];
            text = text.Replace("{year}", s.Year.ToString()).Replace("{hero}", s.State.Hero.Name);
            if (vars != null) foreach (var kv in vars) text = text.Replace("{" + kv.Key + "}", kv.Value);
            AddText(s, text, category);
        }

        public static void AddText(GameSession s, string text, string category, bool glitch = false)
        {
            s.State.Chronicle.Add(new ChronicleEntry { Months = s.State.Months, Text = text, Category = category, Glitch = glitch });
            if (s.State.Chronicle.Count > 600) s.State.Chronicle.RemoveAt(0);
        }

        public static List<ChronicleEntry> Since(GameSession s, int from)
        {
            var list = new List<ChronicleEntry>();
            for (int i = Math.Max(0, from); i < s.State.Chronicle.Count; i++) list.Add(s.State.Chronicle[i]);
            return list;
        }
    }

    /// <summary>Намёки на симуляцию: повтор лиц и реплик, сбои зон, невозможные записи хроники.</summary>
    public static class SimulationHints
    {
        public static void Register(GameSession s, string id, string message)
        {
            var ns = s.State.Narrative;
            if (!ns.HintIds.Add(id)) return;
            ns.HintsSeen++;
            if (message != null) s.Message(message);
        }

        public static void ZoneGlitch(GameSession s, PoiState zone)
        {
            var h = s.Content.Hints.zoneGlitches;
            if (h.Count == 0 || s.State.Narrative.HintIds.Contains("zone_" + zone.Id)) return;
            if (!s.Rng.Chance(0.5f)) return;
            Register(s, "zone_" + zone.Id, h[s.Rng.NextInt(h.Count)]);
        }

        /// <summary>NPC повторяет чужую реплику — дословно.</summary>
        public static string EchoLine(GameSession s)
        {
            var recent = s.State.Narrative.RecentLines;
            if (recent.Count > 4 && s.Rng.Chance(0.5f))
            {
                Register(s, "echo_" + s.State.Narrative.HintIds.Count, null);
                return "«" + recent[s.Rng.NextInt(recent.Count - 2)] + "»";
            }
            var list = s.Content.Hints.repeatedLines;
            return list.Count > 0 ? list[s.Rng.NextInt(list.Count)] : "";
        }

        public static string LeaderSuspicion(GameSession s, NationState n)
        {
            var list = s.Content.Hints.leaderSuspicions;
            if (n == null || list.Count == 0 || n.Leader.SuspectsTruth < 0.3f) return "";
            Register(s, "leader_" + n.Id, null);
            return list[(int)(n.Leader.SuspectsTruth * 10) % list.Count];
        }

        /// <summary>В прыжке хроника иногда пишет то, чего не могло быть.</summary>
        public static void MaybeChronicleGlitch(GameSession s)
        {
            var list = s.Content.Hints.chronicleGlitches;
            if (list.Count == 0 || !s.Rng.Chance(0.6f)) return;
            string t = list[s.Rng.NextInt(list.Count)];
            string comp = "спутник";
            foreach (var c in s.State.Companions) if (!c.Alive) { comp = c.Name; break; }
            t = t.Replace("{hero}", s.State.Hero.Name).Replace("{year}", (s.Year - s.Rng.Range(3, 30)).ToString()).Replace("{companion}", comp)
                 .Replace("{capital}", s.Capital(s.PlayerNation)?.Name ?? "лагерь");
            Chronicle.AddText(s, t, "glitch", true);
            Register(s, "chronicle_" + s.State.Chronicle.Count, null);
        }
    }
}
