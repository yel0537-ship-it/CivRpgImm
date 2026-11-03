using System;
using System.Collections.Generic;
using Immortal.Core.Character;
using Immortal.Core.Content;
using Immortal.Core.Narrative;

namespace Immortal.Core.Civilization
{
    /// <summary>Нации, бессмертные лидеры, память о герое, регентство и ИИ соперников.</summary>
    public static class NationSystem
    {
        public static NationState Create(GameSession s, NationDef def, int index)
        {
            var n = new NationState
            {
                Id = def.player ? "n_player" : "n_" + def.id, DefId = def.id, Name = def.name, Color = def.color, IsPlayer = def.player,
                Team = def.player ? Teams.Player : Teams.NationBase + index, HomeRegion = def.region,
                Leader = new LeaderState { Name = def.leaderName, Title = def.leaderTitle, Personality = def.personality, HeirName = def.heirName },
                PriorityBranch = def.personality == "aggressor" ? "military" : def.personality == "trader" ? "economy" : def.personality == "isolationist" ? "knowledge" : "economy"
            };
            s.State.Nations.Add(n);
            foreach (var other in s.State.Nations) if (other != n) Diplomacy.Rel(s, n.Id, other.Id);
            return n;
        }

        public static void Remember(GameSession s, NationState n, string memory, float attitude)
        {
            if (n == null || n.IsPlayer || s.State.PlayerNationId == null && memory == null) return;
            if (memory != null && !n.Leader.Memories.Contains(memory)) n.Leader.Memories.Add(memory);
            var pid = s.State.PlayerNationId ?? "__hero";
            var r = Diplomacy.Rel(s, n.Id, pid);
            r.Attitude = MathUtil.Clamp(r.Attitude + attitude, -100, 100);
        }

        public static void ReassignCapital(GameSession s, NationState n)
        {
            CityState best = null;
            foreach (var c in s.State.Cities) if (c.NationId == n.Id && (best == null || c.Population > best.Population)) best = c;
            if (best == null)
            {
                n.Destroyed = true; n.CapitalCityId = null;
                Chronicle.Add(s, "nation_fell", new Dictionary<string, string> { ["nation"] = n.Name });
                return;
            }
            foreach (var c in s.State.Cities) if (c.NationId == n.Id) c.IsCapital = c == best;
            n.CapitalCityId = best.Id;
        }

        public static void OnLeaderKilled(GameSession s, string nationId, bool byPlayer)
        {
            var n = s.Nation(nationId);
            if (n == null || !n.Leader.Alive) return;
            n.Leader.Alive = false; n.Leader.UnitId = 0;
            StartRegency(s, n);
            Chronicle.Add(s, "leader_died", new Dictionary<string, string> { ["leader"] = n.Leader.Name, ["nation"] = n.Name });
            if (byPlayer)
            {
                s.State.Narrative.Flags.Add("killed_leader_" + n.DefId);
                s.State.Hero.Reputation -= 8;
                foreach (var o in s.State.Nations) if (!o.IsPlayer && o != n) Remember(s, o, "hero_killed_leader", -12);
                s.Message($"{n.Leader.Name} пал. В {n.Name} начинается регентство — окраинные города уязвимы.");
            }
        }

        public static void StartRegency(GameSession s, NationState n)
        {
            n.Leader.RegencyUntilMonths = s.State.Months + 12 * s.Rng.Range(8, 15);
            n.Influence *= 0.4f; n.InfluenceTotal *= 0.75f;
            foreach (var c in s.State.Cities)
                if (c.NationId == n.Id && !c.IsCapital) { c.AutonomyState = Autonomy.Vulnerable; c.Loyalty = Math.Min(c.Loyalty, 35); }
        }

        public static void CaptureLeader(GameSession s, NationState n)
        {
            n.Leader.Captive = true; n.Leader.Alive = false;
            var u = s.FindUnit(n.Leader.UnitId); if (u != null) s.RemoveUnit(u);
            n.Leader.UnitId = 0;
            StartRegency(s, n);
            s.State.Narrative.Flags.Add("captured_leader_" + n.DefId);
            Chronicle.Add(s, "leader_captured", new Dictionary<string, string> { ["leader"] = n.Leader.Name, ["nation"] = n.Name });
        }

        public static void StepMonth(GameSession s, NationState n)
        {
            if (n.Destroyed) return;
            // Конец регентства: появляется наследник, окно закрывается.
            if (!n.Leader.Alive && n.Leader.RegencyUntilMonths > 0 && s.State.Months >= n.Leader.RegencyUntilMonths)
            {
                var old = n.Leader;
                n.Leader = new LeaderState
                {
                    Name = old.HeirName ?? ("Наследник " + old.Name), Title = old.Title, Personality = old.Personality, Immortal = false,
                    HeirName = "Наследник " + (old.HeirName ?? old.Name), Memories = new List<string>(old.Memories)
                };
                n.Leader.Memories.Add("parent_fell");
                foreach (var c in s.State.Cities) if (c.NationId == n.Id && c.AutonomyState == Autonomy.Vulnerable) { c.AutonomyState = Autonomy.Loyal; c.Loyalty = 55; }
                Chronicle.Add(s, "heir", new Dictionary<string, string> { ["leader"] = n.Leader.Name, ["nation"] = n.Name });
            }
            // Склады не бездонны: запасы ограничены размером народа.
            float storeCap = 300 + CitySystem.NationPopulation(s, n) * 15;
            foreach (var k in new List<string>(n.Treasury.Keys))
                if (k != "essence" && n.Treasury[k] > storeCap) n.Treasury[k] = storeCap;
            if (!n.IsPlayer) Ai(s, n);
            if (s.Content.EraOrder(n.EraId) >= 1) n.Leader.SuspectsTruth += 0.01f;
        }

        static void Ai(GameSession s, NationState n)
        {
            var cap = s.Capital(n);
            if (cap == null) { ReassignCapital(s, n); return; }
            string pers = n.Leader.Personality ?? "trader";

            // Специализация столицы и приоритет науки по характеру.
            if (s.Rng.Chance(0.05f)) cap.Specialization = pers == "aggressor" ? "military" : pers == "isolationist" ? "science" : "economy";

            // Армии.
            float pool = n.MilitaryPool;
            int armies = 0; foreach (var a in s.State.Armies) if (a.NationId == n.Id && a.Kind == "army") armies++;
            if (pool >= 18 && armies < 3)
            {
                float str = Math.Min(pool, 40 + CitySystem.NationPopulation(s, n) * 0.3f);
                var army = ArmySystem.Create(s, n, cap, str, "army");
                n.MilitaryPool -= str;
                var target = PickWarTarget(s, n);
                if (target != null) ArmySystem.Order(s, army, target);
            }

            // Войны и мир.
            foreach (var o in s.State.Nations)
            {
                if (o == n || o.Destroyed) continue;
                var r = Diplomacy.Rel(s, n.Id, o.Id);
                float myStr = Diplomacy.Strength(s, n), theirStr = Diplomacy.Strength(s, o);
                if (!r.War)
                {
                    float warWish = (pers == "aggressor" ? 0.02f : pers == "trader" ? 0.004f : 0.002f) + Math.Max(0, -r.Attitude) / 1500f;
                    if (r.Agreements.Contains("non_aggression")) warWish *= 0.2f;
                    if (r.Agreements.Contains("defensive_alliance")) warWish = 0;
                    if (s.State.Months - r.LastWarMonths < 240) warWish *= 0.1f;
                    // Крошечную общину изгоя не трогают, пока она не стала городом-государством или не задела их сама.
                    if (o.IsPlayer && s.Content.EraOrder(o.EraId) < 1 && r.Attitude > -40) warWish = 0;
                    if (myStr > theirStr * 1.1f && r.Attitude < 10 && s.Rng.Chance(warWish)) Diplomacy.DeclareWar(s, n.Id, o.Id);
                }
                else
                {
                    bool losing = myStr < theirStr * 0.6f;
                    float peaceWish = (losing ? 0.15f : 0.02f) + (pers == "trader" ? 0.03f : 0);
                    if (s.State.Months - r.LastWarMonths > 24 && s.Rng.Chance(peaceWish))
                    {
                        if (o.IsPlayer) EventSystem.Fire(s, "envoy_peace", new DialogueState { NationId = n.Id });
                        else Diplomacy.MakePeace(s, n.Id, o.Id);
                    }
                }
                // Посольства к игроку.
                if (o.IsPlayer && !r.War && s.Rng.Chance(pers == "trader" ? 0.04f : 0.015f) && r.Attitude > -10)
                {
                    string ag = pers == "trader" ? "trade" : pers == "isolationist" ? "non_aggression" : "open_borders";
                    if (!r.Agreements.Contains(ag)) EventSystem.Fire(s, "envoy_" + ag, new DialogueState { NationId = n.Id });
                }
            }

            // Покушения на героя.
            var pr = s.State.PlayerNationId != null ? Diplomacy.Rel(s, n.Id, s.State.PlayerNationId) : null;
            if (pr != null && pr.War && n.Leader.Alive && s.Rng.Chance(s.Balance.war.assassinationChance * (pers == "aggressor" ? 1.5f : 0.5f)))
                EventSystem.Fire(s, "assassins", new DialogueState { NationId = n.Id });

            // Расширение.
            int cities = CitySystem.CityCount(s, n);
            if (cap.Population > 28 + cities * 10 && cities + ArmySystem.SettlersInFlight(s, n) < 5 && s.Rng.Chance(0.06f))
                ArmySystem.SendSettlers(s, n, cap, null, false);

            // Склонять уязвимые вольные города.
            foreach (var c in s.State.Cities)
                if (c.NationId == null && Vec2.Distance(c.Pos, cap.Pos) < 160 && s.Rng.Chance(0.01f))
                    CitySystem.JoinNation(s, c, n, 45);
        }

        public static CityState PickWarTarget(GameSession s, NationState n)
        {
            var cap = s.Capital(n);
            CityState best = null; float bd = float.MaxValue;
            foreach (var c in s.State.Cities)
            {
                if (c.NationId == n.Id) continue;
                bool hostile = c.NationId == null ? false : Diplomacy.AtWar(s, n.Id, c.NationId);
                if (!hostile) continue;
                float d = Vec2.Distance(c.Pos, cap.Pos) + c.Garrison * 2;
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }
    }

    /// <summary>Соглашения, войны, единая мировая репутация.</summary>
    public static class Diplomacy
    {
        public static readonly string[] Agreements = { "non_aggression", "trade", "open_borders", "defensive_alliance" };

        public static string AgreementName(string a) => a switch
        {
            "non_aggression" => "Ненападение", "trade" => "Торговое соглашение", "open_borders" => "Открытые границы",
            "defensive_alliance" => "Оборонительный союз", "peace" => "Мир", _ => a
        };

        public static RelationState Rel(GameSession s, string a, string b)
        {
            if (string.CompareOrdinal(a, b) > 0) { var t = a; a = b; b = t; }
            foreach (var r in s.State.Relations) if (r.A == a && r.B == b) return r;
            var nr = new RelationState { A = a, B = b };
            s.State.Relations.Add(nr);
            return nr;
        }

        public static bool AtWar(GameSession s, string a, string b)
        {
            if (a == null || b == null || a == b) return false;
            if (string.CompareOrdinal(a, b) > 0) { var t = a; a = b; b = t; }
            foreach (var r in s.State.Relations) if (r.A == a && r.B == b) return r.War;
            return false;
        }

        public static float Strength(GameSession s, NationState n)
        {
            float v = n.MilitaryPool;
            foreach (var c in s.State.Cities) if (c.NationId == n.Id) v += c.Garrison;
            foreach (var a in s.State.Armies) if (a.NationId == n.Id && a.Kind == "army") v += a.Strength;
            return v;
        }

        public static void DeclareWar(GameSession s, string a, string b)
        {
            var r = Rel(s, a, b);
            if (r.War) return;
            bool breach = r.Agreements.Contains("non_aggression");
            r.Agreements.Clear();
            r.War = true; r.LastWarMonths = s.State.Months; r.Attitude = Math.Min(r.Attitude, -30);
            var na = s.Nation(a); var nb = s.Nation(b);
            if (breach && na != null && na.IsPlayer) s.State.Hero.Reputation -= 15;
            Chronicle.Add(s, "war", new Dictionary<string, string> { ["a"] = na?.Name ?? a, ["b"] = nb?.Name ?? b });
            if ((na != null && na.IsPlayer) || (nb != null && nb.IsPlayer))
                s.Message($"Война: {na?.Name} против {nb?.Name}!");
            // Оборонительные союзники вступают в войну.
            foreach (var c in s.State.Nations)
            {
                if (c.Id == a || c.Id == b) continue;
                if (Rel(s, c.Id, b).Agreements.Contains("defensive_alliance") && !AtWar(s, c.Id, a)) DeclareWar(s, c.Id, a);
            }
        }

        public static void MakePeace(GameSession s, string a, string b)
        {
            var r = Rel(s, a, b);
            if (!r.War) return;
            r.War = false; r.LastWarMonths = s.State.Months; r.Attitude = Math.Max(r.Attitude, -10);
            foreach (var army in s.State.Armies)
                if ((army.NationId == a || army.NationId == b) && army.Kind == "army") ArmySystem.ReturnHome(s, army);
            Chronicle.Add(s, "peace", new Dictionary<string, string> { ["a"] = s.Nation(a)?.Name ?? a, ["b"] = s.Nation(b)?.Name ?? b });
        }

        /// <summary>Шанс принятия предложения. Личная встреча лидеров сильнее обычных переговоров.</summary>
        public static float AcceptChance(GameSession s, NationState target, string agreement, bool personal)
        {
            var r = Rel(s, target.Id, s.State.PlayerNationId ?? "__hero");
            float c = 0.25f + r.Attitude / 100f + (float)s.State.Hero.Reputation * 0.004f + (personal ? 0.25f : 0f);
            c += HeroSystem.EffSpeech(s) * 0.003f;
            string p = target.Leader.Personality;
            if (agreement == "trade" && p == "trader") c += 0.2f;
            if (agreement == "open_borders" && p == "isolationist") c -= 0.25f;
            if (agreement == "non_aggression" && p == "aggressor") c -= 0.15f;
            if (agreement == "defensive_alliance") c -= 0.2f;
            if (agreement == "peace")
            {
                var pn = s.PlayerNation;
                float ratio = pn != null ? Strength(s, pn) / Math.Max(1, Strength(s, target)) : 0.5f;
                c = 0.2f + (ratio - 1) * 0.3f + (personal ? 0.2f : 0) + r.Attitude / 200f;
            }
            if (target.Leader.Memories.Contains("hero_betrayed")) c -= 0.2f;
            if (target.Leader.Memories.Contains("hero_cleared_bandits")) c += 0.05f;
            return MathUtil.Clamp(c, 0.02f, 0.95f);
        }

        public static bool Propose(GameSession s, NationState target, string agreement, bool personal)
        {
            var pid = s.State.PlayerNationId;
            if (pid == null) { s.Message("Сначала нужна своя община."); return false; }
            var r = Rel(s, target.Id, pid);
            if (agreement != "peace" && r.War) { s.Message("Сначала нужен мир."); return false; }
            if (agreement != "peace" && r.Agreements.Contains(agreement)) { s.Message("Соглашение уже действует."); return false; }
            if (!personal && s.Content.EraOrder(s.PlayerEra) >= 2)
            {
                if (s.PlayerNation.Influence < 10) { s.Message("Не хватает влияния (10)."); return false; }
                s.PlayerNation.Influence -= 10;
            }
            float chance = AcceptChance(s, target, agreement, personal);
            bool ok = s.Rng.Chance(chance);
            if (ok)
            {
                if (agreement == "peace") MakePeace(s, target.Id, pid);
                else r.Agreements.Add(agreement);
                r.Attitude += 5;
                s.Message($"{target.Leader.Name} согласен: {AgreementName(agreement)} (шанс {chance:P0}).");
                Chronicle.Add(s, "treaty", new Dictionary<string, string> { ["a"] = s.PlayerNation.Name, ["b"] = target.Name, ["treaty"] = AgreementName(agreement) });
            }
            else
            {
                r.Attitude -= 3;
                s.Message($"{target.Leader.Name} отказывает (шанс был {chance:P0}).");
            }
            return ok;
        }

        public static void StepMonth(GameSession s)
        {
            foreach (var r in s.State.Relations)
            {
                if (r.War) r.Attitude -= 0.5f;
                if (r.Agreements.Contains("trade")) r.Attitude += 0.4f;
                if (r.Agreements.Contains("open_borders")) r.Attitude += 0.2f;
                if (r.Agreements.Contains("defensive_alliance")) r.Attitude += 0.3f;
                r.Attitude += (0 - r.Attitude) * 0.005f;
                r.Attitude = MathUtil.Clamp(r.Attitude, -100, 100);
                // Торговля приносит доход обеим сторонам.
                if (r.Agreements.Contains("trade"))
                {
                    var a = s.Nation(r.A); var b = s.Nation(r.B);
                    a?.Add("food", 2); b?.Add("food", 2);
                    if (a != null && s.Content.EraOrder(a.EraId) >= 1) { a.Influence += 0.5f; a.InfluenceTotal += 0.5f; }
                    if (b != null && s.Content.EraOrder(b.EraId) >= 1) { b.Influence += 0.5f; b.InfluenceTotal += 0.5f; }
                }
            }
            s.State.Hero.Reputation = MathUtil.Clamp(s.State.Hero.Reputation * 0.998, -100, 100);
        }
    }
}
