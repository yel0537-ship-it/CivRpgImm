using System;
using System.Collections.Generic;
using Immortal.Core.Action;
using Immortal.Core.Civilization;
using Immortal.Core.Narrative;
using Immortal.Core.World;

namespace Immortal.Core.Character
{
    /// <summary>Спутники: отряд, поручения, роли в государстве, отношения, старение, смерть и потомки.</summary>
    public static class CompanionSystem
    {
        public static bool SetRole(GameSession s, CompanionState c, string role, string targetId)
        {
            if (!c.Alive) return false;
            var h = s.State.Hero;
            if (role == CompanionRoles.Party && c.Role != CompanionRoles.Party && h.PartyCompanionUnits.Count >= s.Balance.companions.maxParty)
            { s.Message($"С вами уже {s.Balance.companions.maxParty} спутника."); return false; }

            // Снимаем со старой роли.
            if (c.Role == CompanionRoles.Governor) { var old = s.City(c.AssignmentId); if (old != null && old.GovernorId == c.Id) old.GovernorId = null; }
            if (c.Role == CompanionRoles.General) { var a = s.Army(c.AssignmentId); if (a != null && a.GeneralId == c.Id) a.GeneralId = null; }
            if (c.Role == CompanionRoles.Party) DespawnParty(s, c);
            var npcUnit = s.FindUnit(c.UnitId);
            if (npcUnit != null && npcUnit.Kind == UnitKind.Npc) { s.RemoveUnit(npcUnit); c.UnitId = 0; }

            c.Role = role; c.AssignmentId = targetId;
            switch (role)
            {
                case CompanionRoles.Party: SpawnParty(s, c); break;
                case CompanionRoles.Governor:
                    var city = s.City(targetId);
                    if (city == null) { c.Role = CompanionRoles.Camp; return false; }
                    if (city.GovernorId != null && city.GovernorId != c.Id) { var prev = s.Companion(city.GovernorId); if (prev != null) { prev.Role = CompanionRoles.Camp; prev.AssignmentId = null; } }
                    city.GovernorId = c.Id;
                    s.Message($"{c.Name} — наместник города {city.Name}.");
                    break;
                case CompanionRoles.General:
                    var army = s.Army(targetId);
                    if (army != null) army.GeneralId = c.Id;
                    break;
                case CompanionRoles.Errand:
                    c.ErrandKind = targetId ?? "resources";
                    c.ErrandReturnMonths = s.State.Months + s.Balance.companions.errandMonths;
                    s.Message($"{c.Name} отправляется: {ErrandName(c.ErrandKind)}. Вернётся через {s.Balance.companions.errandMonths:0} мес.");
                    break;
            }
            return true;
        }

        public static string ErrandName(string kind) => kind switch
        {
            "intel" => "разведка", "essence" => "поход в дым за ресурсом", _ => "сбор ресурсов"
        };

        public static string RoleName(string role) => role switch
        {
            CompanionRoles.Party => "в отряде", CompanionRoles.Camp => "в лагере", CompanionRoles.Errand => "на поручении",
            CompanionRoles.Governor => "наместник", CompanionRoles.General => "полководец", CompanionRoles.Scholar => "учёный",
            CompanionRoles.Envoy => "посол", CompanionRoles.Rebel => "мятежник", _ => role
        };

        public static void SpawnParty(GameSession s, CompanionState c)
        {
            var hero = s.HeroUnit;
            if (hero == null || c.UnitId != 0 && s.FindUnit(c.UnitId) != null) return;
            float hp = 50 + c.Combat * 1.2f;
            var u = s.AddUnit(new Unit
            {
                Kind = UnitKind.Companion, CompanionId = c.Id, Name = c.Name, Team = Teams.Player,
                Pos = s.Map.NearestWalkable(hero.Pos + new Vec2(1.5f, -1.5f)), Hp = hp, MaxHp = hp, Damage = 5 + c.Combat * 0.18f,
                Speed = 6.2f, AttackRange = 1.5f, AggroRange = 11f, Windup = 0.45f, Recover = 0.6f, Ai = AiState.Follow, Behavior = "melee",
                Armor = 1
            });
            c.UnitId = u.Id;
            s.State.Hero.PartyCompanionUnits.Add(u.Id);
        }

        public static void DespawnParty(GameSession s, CompanionState c)
        {
            var u = s.FindUnit(c.UnitId);
            if (u != null) s.RemoveUnit(u);
            s.State.Hero.PartyCompanionUnits.Remove(c.UnitId);
            c.UnitId = 0;
        }

        /// <summary>Восстановление отряда после загрузки или прыжка во времени.</summary>
        public static void RespawnParty(GameSession s)
        {
            s.State.Hero.PartyCompanionUnits.RemoveAll(id => s.FindUnit(id) == null);
            foreach (var c in s.State.Companions)
                if (c.Alive && c.Role == CompanionRoles.Party && (c.UnitId == 0 || s.FindUnit(c.UnitId) == null)) { c.UnitId = 0; SpawnParty(s, c); }
        }

        public static void OnDowned(GameSession s, Unit u)
        {
            var c = s.Companion(u.CompanionId);
            if (c == null) return;
            s.State.Hero.PartyCompanionUnits.Remove(u.Id);
            c.UnitId = 0;
            if (s.Rng.Chance(0.15f)) { Die(s, c, "в бою"); return; }
            c.Role = CompanionRoles.Camp;
            c.Relation -= 3;
            s.Message($"{c.Name} тяжело ранен и возвращается в лагерь.");
        }

        public static void StepMonth(GameSession s)
        {
            var list = s.State.Companions;
            for (int i = 0; i < list.Count; i++)
            {
                var c = list[i];
                if (!c.Alive) continue;
                // Отношения дрейфуют.
                if (c.Role == CompanionRoles.Party) c.Relation += 0.4f;
                else if (c.Role == CompanionRoles.Camp) c.Relation -= 0.05f;
                else if (c.Role == CompanionRoles.Governor || c.Role == CompanionRoles.Scholar) c.Relation += 0.1f;
                if (c.Character == "greedy" && c.Role == CompanionRoles.Governor) c.Relation -= 0.15f;
                c.Relation = MathUtil.Clamp(c.Relation, -100, 100);

                if (c.Role == CompanionRoles.Errand && s.State.Months >= c.ErrandReturnMonths) ReturnFromErrand(s, c);

                double age = c.AgeYears(s.State.Months);
                if (!c.AskedForEssence && age >= c.LifespanYears - 1.5 && s.State.Hero.RejuvenationUnlocked && !s.InTimeJump)
                {
                    c.AskedForEssence = true;
                    EventSystem.Fire(s, "companion_dying", new DialogueState { CompanionId = c.Id });
                }
                if (age >= c.LifespanYears) Die(s, c, "от старости");
            }
        }

        static void ReturnFromErrand(GameSession s, CompanionState c)
        {
            var pn = s.PlayerNation; var h = s.State.Hero;
            void Give(string r, int n) { if (pn != null) pn.Add(r, n); else h.Add(r, n); }
            string report;
            switch (c.ErrandKind)
            {
                case "intel":
                    PoiState target = null;
                    foreach (var p in s.State.Pois) if (!p.Discovered) { target = p; if (s.Rng.Chance(0.3f)) break; }
                    if (target != null)
                    {
                        target.Discovered = true;
                        Vision.Reveal(s.State, s.Map, target.Pos, 30f);
                        s.FogRevision++;
                        report = $"разведал место «{target.Name}»";
                    }
                    else report = "не нашёл ничего нового";
                    foreach (var n in s.State.Nations) if (!n.IsPlayer) HeroSystem.AddMemory(s, "intel_" + n.DefId);
                    break;
                case "essence":
                    if (s.Rng.Chance(s.Balance.companions.essenceErrandRisk))
                    {
                        c.Role = CompanionRoles.Camp;
                        Die(s, c, "в дыму");
                        return;
                    }
                    int e = s.Rng.Range(2, 5);
                    Give("essence", e);
                    report = $"принёс {e} мистического ресурса";
                    HeroSystem.OnEssencePicked(s);
                    break;
                default:
                    float k = 1f + c.Gathering / 40f;
                    int w = (int)(s.Rng.Range(12, 25) * k), st = (int)(s.Rng.Range(8, 16) * k), f = (int)(s.Rng.Range(8, 16) * k);
                    Give("wood", w); Give("stone", st); Give("berries", f);
                    report = $"принёс {w} дерева, {st} камня, {f} ягод";
                    break;
            }
            c.Role = CompanionRoles.Camp; c.ErrandKind = null;
            c.Relation += 1;
            s.Message($"{c.Name} вернулся с поручения: {report}.");
        }

        public static void Die(GameSession s, CompanionState c, string cause)
        {
            if (!c.Alive) return;
            if (c.Role == CompanionRoles.Party) DespawnParty(s, c);
            if (c.Role == CompanionRoles.Governor) { var city = s.City(c.AssignmentId); if (city != null && city.GovernorId == c.Id) city.GovernorId = null; }
            var npcU = s.FindUnit(c.UnitId); if (npcU != null) s.RemoveUnit(npcU);
            c.UnitId = 0;
            c.Alive = false; c.DeathMonths = s.State.Months; c.Role = "dead";
            s.Emit(new GameEvent { Type = GameEventType.CompanionDied, Text = c.Name });
            s.Message($"{c.Name} умирает {cause}.");
            Chronicle.Add(s, "companion_died", new Dictionary<string, string> { ["name"] = c.Name, ["cause"] = cause });

            // Могила в мире.
            var home = s.Capital(s.PlayerNation)?.Pos ?? s.HeroUnit?.Pos ?? new Vec2(100, 100);
            var gpos = s.Map.NearestWalkable(home + Vec2.FromAngle(MathUtil.Hash01(c.Id.Length, s.State.Companions.IndexOf(c), 5) * 6.28f) * s.Rng.Range(12f, 18f));
            s.State.Pois.Add(new PoiState
            {
                Id = s.State.NewId("grave"), DefId = "grave", Name = $"Могила: {c.Name}", Pos = gpos, Radius = 2, Discovered = true,
                Data = c.Id, Cleared = true, RegionId = s.Content.Regions[s.Map.RegionIndexAt(gpos)].id
            });

            // Потомок: наследует часть отношения (любовь или обиду).
            if (c.AgeYears(s.State.Months) > 25 || s.Rng.Chance(0.5f)) CreateDescendant(s, c, home);
        }

        public static NpcState CreateDescendant(GameSession s, CompanionState c, Vec2 near)
        {
            var names = s.Content.RecruitsFile.genericNames;
            string baseName = names.Count > 0 ? names[(s.State.Narrative.GeneratedNameCounter++ * 7 + c.Generation) % names.Count] : "Потомок";
            var suffixes = s.Content.RecruitsFile.descendantSuffixes;
            string suffix = suffixes.Count > 0 ? suffixes[s.Rng.NextInt(suffixes.Count)] : "из рода";
            var pos = s.Map.NearestWalkable(near + Vec2.FromAngle(s.Rng.Range(0, 6.28f)) * s.Rng.Range(5f, 9f));
            bool sameFace = s.Rng.Chance(0.35f);
            string[] needs = { "meat", "hide", "berries", "stone" };
            string[] chars = { "naive", "wary", "proud", "kind", "timid", "clever" };
            var npc = new NpcState
            {
                Id = s.State.NewId("npc"), RecruitDefId = c.RecruitDefId, Name = $"{baseName} ({suffix}: {ShortName(c.Name)})",
                Pos = pos, Home = pos, IsDescendant = true, AncestorCompanionId = c.Id, AncestorName = ShortName(c.Name),
                Generation = c.Generation + 1, FaceId = sameFace ? c.FaceId : s.Rng.Range(100, 999),
                InheritedRelation = c.Relation * 0.6f, Need = needs[s.Rng.NextInt(needs.Length)],
                Character = s.Rng.Chance(0.5f) ? c.Character : chars[s.Rng.NextInt(chars.Length)], Distrust = c.Relation < 0 ? 3 : 1
            };
            s.State.Npcs.Add(npc);
            if (sameFace) SimulationHints.Register(s, "face_" + npc.Id, null);
            return npc;
        }

        public static string ShortName(string name)
        {
            int i = name.IndexOf(" (");
            return i > 0 ? name.Substring(0, i) : name;
        }

        public static float GovernorLoyaltyBonus(GameSession s, CityState city)
        {
            var g = s.Companion(city.GovernorId);
            if (g == null || !g.Alive) return -5f;
            float b = g.Relation * 0.12f + g.Speech * 0.08f;
            if (g.Character == "kind") b += 4;
            if (g.Character == "greedy") b -= 4;
            return b;
        }
    }
}
