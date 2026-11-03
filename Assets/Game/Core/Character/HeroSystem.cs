using System;
using System.Collections.Generic;
using Immortal.Core.Content;
using Immortal.Core.Narrative;
using Immortal.Core.Civilization;
using Immortal.Core.World;

namespace Immortal.Core.Character
{
    /// <summary>Атрибуты героя, возраст и жизненная сила, опыт, навыки, снаряжение, омоложение.</summary>
    public static class HeroSystem
    {
        public static float Youth(HeroState h) => MathUtil.Clamp01((float)(h.VitalityYears / Math.Max(1, h.MaxVitalityYears)));

        public static float EffBody(GameSession s)
        {
            var h = s.State.Hero; var b = s.Balance.hero;
            float y = Math.Min(1f, Youth(h) * 1.25f);
            return h.Body * (b.youthBodyMin + (1 - b.youthBodyMin) * y);
        }

        public static float EffMind(GameSession s) => s.State.Hero.Mind + s.Balance.hero.ageMindBonus * (1 - Youth(s.State.Hero));
        public static float EffSpeech(GameSession s) => Skill(s.State.Hero, "speech") + s.Balance.hero.ageSpeechBonus * (1 - Youth(s.State.Hero));
        public static float Skill(HeroState h, string id) => h.Skills.TryGetValue(id, out var v) ? v : 0;
        public static double ApparentAge(HeroState h) => 18 + (1 - Youth(h)) * 72;

        public static float MaxHp(GameSession s) => s.Balance.hero.hpBase + s.Balance.hero.hpPerBody * EffBody(s) + Affix(s, "hp");
        public static float MaxEnergy(GameSession s) => s.Balance.hero.energyBase + s.Balance.hero.energyPerWill * s.State.Hero.Will + Affix(s, "energy");

        public static float BuffValue(HeroState h, string stat)
        {
            float v = 0;
            foreach (var b in h.Buffs) if (b.Stat == stat) v += b.Value;
            return v;
        }

        public static ItemInstance Item(HeroState h, string id)
        {
            if (id == null) return null;
            foreach (var i in h.Items) if (i.Id == id) return i;
            return null;
        }

        public static IEnumerable<ItemInstance> Equipped(HeroState h)
        {
            var w = Item(h, h.WeaponId); if (w != null) yield return w;
            var a = Item(h, h.ArmorId); if (a != null) yield return a;
            var t = Item(h, h.TrinketId); if (t != null) yield return t;
        }

        public static float QualityMult(GameSession s, ItemInstance i) =>
            i?.Quality != null && s.Content.Qualities.TryGetValue(i.Quality, out var q) ? q.mult : 1f;

        public static float Affix(GameSession s, string stat)
        {
            float v = 0;
            foreach (var i in Equipped(s.State.Hero))
                if (i.Affixes.TryGetValue(stat, out var a)) v += a;
            return v;
        }

        public static string WeaponType(GameSession s)
        {
            var w = Item(s.State.Hero, s.State.Hero.WeaponId);
            if (w != null && s.Content.Items.TryGetValue(w.DefId, out var d) && d.weaponType != null) return d.weaponType;
            return "fists";
        }

        public static float WeaponDamage(GameSession s)
        {
            var w = Item(s.State.Hero, s.State.Hero.WeaponId);
            if (w == null || !s.Content.Items.TryGetValue(w.DefId, out var d)) return 0;
            return d.damage * QualityMult(s, w) + Affix(s, "damage");
        }

        public static float Armor(GameSession s)
        {
            float v = Affix(s, "armor");
            foreach (var i in Equipped(s.State.Hero))
                if (s.Content.Items.TryGetValue(i.DefId, out var d)) v += d.armor * QualityMult(s, i);
            return v;
        }

        /// <summary>Умение в слоте 1–4. Слот 4 — сверхспособность эпохи (только в дыму).</summary>
        public static AbilityDef AbilityInSlot(GameSession s, int slot)
        {
            if (slot == 4)
            {
                var era = s.PlayerEraDef;
                return era.superpower != null && s.Content.Abilities.TryGetValue(era.superpower, out var sp) ? sp : null;
            }
            if (!s.Content.WeaponTypes.TryGetValue(WeaponType(s), out var wt)) return null;
            int i = slot - 1;
            return i >= 0 && i < wt.skills.Count ? s.Content.Abilities[wt.skills[i]] : null;
        }

        public static AbilityDef BasicAttack(GameSession s)
        {
            return s.Content.WeaponTypes.TryGetValue(WeaponType(s), out var wt) ? s.Content.Abilities[wt.basic] : null;
        }

        public static void RefreshUnitStats(GameSession s)
        {
            var u = s.HeroUnit; if (u == null) return;
            float max = MaxHp(s);
            if (Math.Abs(max - u.MaxHp) > 0.01f)
            {
                float frac = u.MaxHp > 0 ? u.Hp / u.MaxHp : 1;
                u.MaxHp = max; u.Hp = Math.Min(max, Math.Max(1, max * frac));
            }
            u.Armor = Armor(s);
        }

        public static void TickRealtime(GameSession s, float dt)
        {
            var h = s.State.Hero; var u = s.HeroUnit;
            if (u == null) return;
            h.Energy = Math.Min(MaxEnergy(s), h.Energy + (s.Balance.hero.energyRegen + h.Will * 0.1f) * dt);
            var keys = new List<string>(h.Cooldowns.Keys);
            foreach (var k in keys) { float v = h.Cooldowns[k] - dt; if (v <= 0) h.Cooldowns.Remove(k); else h.Cooldowns[k] = v; }
            for (int i = h.Buffs.Count - 1; i >= 0; i--) { h.Buffs[i].TimeLeft -= dt; if (h.Buffs[i].TimeLeft <= 0) h.Buffs.RemoveAt(i); }
            if (h.CombatTimer > 0) h.CombatTimer -= dt;
            if (s.State.Tick % 20 == 0) RefreshUnitStats(s);
            if (u.Ai != AiState.Dead && h.CombatTimer <= 0 && u.Hp < u.MaxHp) u.Hp = Math.Min(u.MaxHp, u.Hp + u.MaxHp * 0.02f * dt);

            if (!s.State.Narrative.Flags.Contains("true_immortality"))
            {
                h.VitalityYears -= dt / (s.Balance.secondsPerMonth * 12.0);
                if (h.VitalityYears <= 0) { h.VitalityYears = 0; EndingSystem.DieOfOldAge(s); }
            }
        }

        public static void UpdateVision(GameSession s)
        {
            var u = s.HeroUnit; if (u == null) return;
            var h = s.State.Hero;
            float r = h.InSmoke ? s.Balance.vision.smokeRadius : s.Balance.vision.radius;
            if (Vision.Reveal(s.State, s.Map, u.Pos, r)) s.FogRevision++;
            foreach (var p in s.State.Pois)
            {
                if (p.Discovered) continue;
                if (Vec2.Distance(p.Pos, u.Pos) < Math.Min(16f, r))
                {
                    p.Discovered = true;
                    GainXp(s, 10);
                    s.Emit(new GameEvent { Type = GameEventType.Discovered, Id = p.Id, Pos = p.Pos, Text = p.Name });
                    s.Message($"Найдено: {p.Name}");
                }
            }
        }

        public static void GainXp(GameSession s, int amount)
        {
            var h = s.State.Hero;
            h.Xp += amount;
            int per = s.Balance.hero.xpPerPoint;
            while (h.Xp >= per)
            {
                h.Xp -= per; h.AttributePoints++;
                s.Emit(new GameEvent { Type = GameEventType.LevelUp, Text = "Получено очко атрибута" });
            }
        }

        public static void GainSkill(GameSession s, string id, float mult = 1f)
        {
            var h = s.State.Hero;
            float v = Skill(h, id);
            h.Skills[id] = Math.Min(100, v + s.Balance.hero.skillGain * mult * (1 - v / 120f));
        }

        public static void AddMemory(GameSession s, string id)
        {
            if (string.IsNullOrEmpty(id) || s.State.Hero.Memories.Contains(id)) return;
            s.State.Hero.Memories.Add(id);
        }

        public static int EssenceAvailable(GameSession s)
        {
            int n = s.State.Hero.Count("essence");
            var pn = s.PlayerNation; if (pn != null) n += (int)pn.Get("essence");
            return n;
        }

        public static bool SpendEssence(GameSession s, int amount)
        {
            if (EssenceAvailable(s) < amount) return false;
            var h = s.State.Hero;
            int fromHero = Math.Min(amount, h.Count("essence"));
            h.Add("essence", -fromHero);
            int rest = amount - fromHero;
            if (rest > 0) s.PlayerNation.Add("essence", -rest);
            return true;
        }

        /// <summary>Омоложение: ресурс → годы жизни, но часть навыка и воспоминаний теряется.</summary>
        public static bool Rejuvenate(GameSession s)
        {
            var h = s.State.Hero; var eb = s.Balance.essence;
            if (!h.RejuvenationUnlocked) { s.Message("Вы ещё не знаете, как это сделать."); return false; }
            if (!SpendEssence(s, eb.rejuvenateCost)) { s.Message($"Нужно {eb.rejuvenateCost} мистического ресурса."); return false; }
            h.VitalityYears = Math.Min(h.MaxVitalityYears, h.VitalityYears + eb.rejuvenateYears);
            h.RejuvenationCount++;
            var skills = new List<string>();
            foreach (var kv in h.Skills) if (kv.Value > 0.5f) skills.Add(kv.Key);
            if (skills.Count == 0) skills.AddRange(h.Skills.Keys);
            skills.Sort();
            string lost = skills[s.Rng.NextInt(skills.Count)];
            h.Skills[lost] = h.Skills[lost] * (1 - eb.skillLoss);
            var forgotten = new List<string>();
            for (int i = 0; i < eb.memoriesLost; i++)
            {
                var candidates = h.Memories.FindAll(m => !m.StartsWith("core_"));
                if (candidates.Count == 0) break;
                var m = candidates[s.Rng.NextInt(candidates.Count)];
                h.Memories.Remove(m);
                forgotten.Add(m);
            }
            RefreshUnitStats(s);
            string memText = forgotten.Count > 0 ? " Забыто: " + string.Join(", ", forgotten.ConvertAll(m => MemoryName(s, m))) + "." : "";
            s.Emit(new GameEvent { Type = GameEventType.Rejuvenated, Text = $"Вы молодеете. Навык «{SkillName(lost)}» ослаб.{memText}" });
            s.Message($"Вы молодеете. Навык «{SkillName(lost)}» ослаб.{memText}");
            Chronicle.Add(s, "rejuvenation", null);
            return true;
        }

        public static string MemoryName(GameSession s, string id) =>
            s.Content.ChronicleFile.templates.TryGetValue("memory:" + id, out var l) && l.Count > 0 ? l[0] : id;

        public static string SkillName(string id) => id switch
        {
            "combat" => "Бой", "gathering" => "Сбор", "speech" => "Речь", "management" => "Управление", _ => id
        };

        public static string RoleName(string role) => role switch
        {
            Roles.Chief => "Вождь", Roles.King => "Царь", Roles.Cardinal => "Серый кардинал", Roles.Myth => "Живой миф", _ => "Изгой"
        };

        public static void SetRole(GameSession s, string role)
        {
            var h = s.State.Hero;
            if (role == null || Roles.Rank(role) <= Roles.Rank(h.Role)) return;
            h.Role = role;
            s.Message($"Новая роль: {RoleName(role)}");
            Chronicle.Add(s, "role", new Dictionary<string, string> { ["role"] = RoleName(role) });
        }

        public static void OnEssencePicked(GameSession s)
        {
            if (!s.State.Narrative.Flags.Contains("first_essence"))
            {
                s.State.Narrative.Flags.Add("first_essence");
                EventSystem.Fire(s, "first_essence", new DialogueState());
            }
        }

        public static bool SpendAttribute(GameSession s, string attr)
        {
            var h = s.State.Hero;
            if (h.AttributePoints <= 0) return false;
            switch (attr)
            {
                case "body": h.Body += 1; break;
                case "mind": h.Mind += 1; break;
                case "will": h.Will += 1; break;
                default: return false;
            }
            h.AttributePoints--;
            RefreshUnitStats(s);
            return true;
        }
    }
}
