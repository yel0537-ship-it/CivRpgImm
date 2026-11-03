using System;
using Immortal.Core.Action;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using Immortal.Core.Narrative;
using Immortal.Core.Time;

namespace Immortal.Core.Commands
{
    // ---------- Герой в мире ----------

    public sealed class SetMoveInput : IGameCommand
    {
        public Vec2 Dir; public SetMoveInput(Vec2 d) { Dir = d; }
        public void Execute(GameSession s) => s.MoveInput = Dir;
    }

    public sealed class MoveTo : IGameCommand
    {
        public Vec2 Target; public MoveTo(Vec2 t) { Target = t; }
        public void Execute(GameSession s)
        {
            var hero = s.HeroUnit; if (hero == null || hero.Ai == AiState.Dead) return;
            s.State.Hero.Pending = null; hero.TargetId = 0;
            var t = s.Map.Clamp(Target);
            if (s.Map.LineWalkable(hero.Pos, t)) { hero.Path = new System.Collections.Generic.List<Vec2> { t }; hero.PathIndex = 0; }
            else MovementSystem.SetPath(s, hero, t);
        }
    }

    public sealed class AttackUnit : IGameCommand
    {
        public int UnitId; public AttackUnit(int id) { UnitId = id; }
        public void Execute(GameSession s)
        {
            var hero = s.HeroUnit; var t = s.FindUnit(UnitId);
            if (hero == null || t == null || t == hero) return;
            s.State.Hero.Pending = null;
            hero.TargetId = UnitId;
        }
    }

    public sealed class Interact : IGameCommand
    {
        public string Kind, Id; public int UnitId, Tile;
        public static Interact Unit(int id) => new Interact { Kind = "unit", UnitId = id };
        public static Interact Poi(string id) => new Interact { Kind = "poi", Id = id };
        public static Interact Building(string cityId, int slot) => new Interact { Kind = "building", Id = cityId, Tile = slot };
        public static Interact Gather(int tile) => new Interact { Kind = "gather", Tile = tile };
        public void Execute(GameSession s)
        {
            var hero = s.HeroUnit; if (hero == null) return;
            switch (Kind)
            {
                case "unit":
                    var u = s.FindUnit(UnitId); if (u == null) return;
                    InteractionSystem.Begin(s, new PendingAction { Kind = "unit", UnitId = UnitId, Pos = u.Pos });
                    break;
                case "poi":
                    var p = s.Poi(Id); if (p == null) return;
                    InteractionSystem.Begin(s, new PendingAction { Kind = "poi", Id = Id, Pos = p.Pos });
                    break;
                case "building":
                    var c = s.City(Id); if (c == null) return;
                    var b = c.Buildings.Find(x => x.Slot == Tile); if (b == null) return;
                    InteractionSystem.Begin(s, new PendingAction { Kind = "building", Id = Id, Tile = Tile, Pos = b.Pos });
                    break;
                case "gather":
                    if (s.Map.NodeAt(Tile) == null) return;
                    LootSystem.StartGather(s, Tile);
                    var pos = s.Map.TileCenter(Tile);
                    if (Vec2.Distance(hero.Pos, pos) > s.Balance.hero.interactRange) MovementSystem.SetPath(s, hero, pos);
                    hero.TargetId = 0;
                    break;
            }
        }
    }

    public sealed class UseAbility : IGameCommand
    {
        public int Slot; public Vec2 Target;
        public UseAbility(int slot, Vec2 target) { Slot = slot; Target = target; }
        public void Execute(GameSession s)
        {
            var hero = s.HeroUnit; if (hero == null || hero.Ai == AiState.Dead) return;
            var a = HeroSystem.AbilityInSlot(s, Slot);
            if (a == null) { s.Message(Slot == 4 ? "В эту эпоху у дыма нет силы — только ресурс." : "Слот пуст: нужно другое оружие."); return; }
            CombatSystem.UseAbility(s, hero, a, Target);
        }
    }

    public sealed class Dash : IGameCommand
    {
        public Vec2 Dir; public Dash(Vec2 d) { Dir = d; }
        public void Execute(GameSession s)
        {
            var hero = s.HeroUnit; var h = s.State.Hero; var hb = s.Balance.hero;
            if (hero == null || hero.Ai == AiState.Dead || h.Cooldowns.ContainsKey("dash")) return;
            if (h.Energy < hb.dashEnergy) { s.Message("Не хватает энергии."); return; }
            h.Energy -= hb.dashEnergy; h.Cooldowns["dash"] = hb.dashCooldown;
            var d = Dir.SqrLength > 0.01f ? Dir.Normalized : hero.Facing;
            var dash = new Content.AbilityDef { id = "dash", name = "Рывок", type = "dash", range = hb.dashDistance };
            CombatSystem.UseAbility(s, hero, dash, hero.Pos + d * hb.dashDistance);
        }
    }

    public sealed class ToggleMount : IGameCommand
    {
        public void Execute(GameSession s)
        {
            var h = s.State.Hero;
            if (!h.HasHorse) { s.Message("У вас нет коня. Ищите табуны в степи."); return; }
            h.Mounted = !h.Mounted;
            s.Message(h.Mounted ? "Вы в седле." : "Вы спешились.");
        }
    }

    public sealed class Craft : IGameCommand
    {
        public string RecipeId; public bool Workshop;
        public Craft(string id, bool workshop) { RecipeId = id; Workshop = workshop; }
        public void Execute(GameSession s) => LootSystem.Craft(s, RecipeId, Workshop);
    }

    public sealed class Equip : IGameCommand
    {
        public string ItemId; public Equip(string id) { ItemId = id; }
        public void Execute(GameSession s) => LootSystem.Equip(s, ItemId);
    }

    public sealed class SpendAttribute : IGameCommand
    {
        public string Attr; public SpendAttribute(string a) { Attr = a; }
        public void Execute(GameSession s) => HeroSystem.SpendAttribute(s, Attr);
    }

    public sealed class Rejuvenate : IGameCommand
    {
        public void Execute(GameSession s) => HeroSystem.Rejuvenate(s);
    }

    // ---------- Диалоги, хроника, время ----------

    public sealed class ChooseOption : IGameCommand
    {
        public int Index; public ChooseOption(int i) { Index = i; }
        public void Execute(GameSession s) => DialogueEngine.Choose(s, Index);
    }

    public sealed class CloseChronicle : IGameCommand
    {
        public void Execute(GameSession s) { s.State.Narrative.ChronicleOpen = false; }
    }

    public sealed class TimeJump : IGameCommand
    {
        public int Years; public TimeJump(int y) { Years = y; }
        public void Execute(GameSession s)
        {
            var ns = s.State.Narrative;
            bool offered = ns.JumpOptions.Contains(Years);
            bool retire = Roles.Rank(s.State.Hero.Role) >= 1 && s.State.Hero.CombatTimer <= 0;
            if (!offered && !retire) { s.Message("Удалиться от дел можно, став вождём, и не в бою."); return; }
            TimeJumpSystem.Jump(s, Years, offered ? ns.JumpReason ?? "era" : "retire");
        }
    }

    public sealed class DeclineJump : IGameCommand
    {
        public void Execute(GameSession s) { s.State.Narrative.JumpOptions.Clear(); }
    }

    // ---------- Правитель ----------

    public sealed class FoundCamp : IGameCommand
    {
        public void Execute(GameSession s)
        {
            if (s.State.PlayerNationId != null) { s.Message("Лагерь уже основан."); return; }
            var hero = s.HeroUnit; if (hero == null) return;
            var owner = TerritorySystem.OwnerAt(s, hero.Pos);
            if (owner != null) { s.Message($"Это земли {owner.Name}. Найдите свободное место."); return; }
            if (ZoneSystem.ZoneAt(s, hero.Pos) != null) { s.Message("В дыму нельзя жить."); return; }
            var def = s.Content.NationDefs.Find(n => n.player);
            var nation = NationSystem.Create(s, def, s.State.Nations.Count);
            s.State.PlayerNationId = nation.Id;
            foreach (var r in s.State.Relations) { }
            var city = CitySystem.Found(s, nation, hero.Pos + hero.Facing * 4f, def.cityNames.Count > 0 ? def.cityNames[0] : "Стоянка", 0, true);
            int named = 0; foreach (var c in s.State.Companions) if (c.Alive) named++;
            city.Population = 1 + named;
            city.FoodStock = 30;
            InteractionSystem.Deposit(s, city);
            s.Message($"Основан лагерь «{city.Name}». Сдавайте добычу у костра, стройте шалаши (Tab).");
            Chronicle.Add(s, "camp", new System.Collections.Generic.Dictionary<string, string> { ["city"] = city.Name });
            EventSystem.Fire(s, "camp_founded", new DialogueState { CityId = city.Id });
        }
    }

    public sealed class Build : IGameCommand
    {
        public string CityId, BuildingId;
        public Build(string c, string b) { CityId = c; BuildingId = b; }
        public void Execute(GameSession s)
        {
            var c = s.City(CityId);
            if (c == null || c.NationId != s.State.PlayerNationId) return;
            if (!CitySystem.CanBuild(s, c, BuildingId, out var reason)) { s.Message($"Нельзя построить: {reason}."); return; }
            CitySystem.PlaceBuilding(s, c, BuildingId);
            HeroSystem.GainSkill(s, "management", 2f);
            s.Message($"Построено: {s.Content.Buildings[BuildingId].name}");
        }
    }

    public sealed class QueueBuild : IGameCommand
    {
        public string CityId, BuildingId;
        public QueueBuild(string c, string b) { CityId = c; BuildingId = b; }
        public void Execute(GameSession s)
        {
            var c = s.City(CityId);
            if (c == null || c.NationId != s.State.PlayerNationId) return;
            c.BuildQueue.Add(BuildingId);
        }
    }

    public sealed class SetSpecialization : IGameCommand
    {
        public string CityId, Spec;
        public SetSpecialization(string c, string spec) { CityId = c; Spec = spec; }
        public void Execute(GameSession s)
        {
            var c = s.City(CityId); if (c == null || c.NationId != s.State.PlayerNationId) return;
            c.Specialization = Spec;
        }
    }

    public sealed class AssignCompanion : IGameCommand
    {
        public string CompanionId, Role, TargetId;
        public AssignCompanion(string c, string role, string target = null) { CompanionId = c; Role = role; TargetId = target; }
        public void Execute(GameSession s)
        {
            var c = s.Companion(CompanionId); if (c == null) return;
            if (Role == CompanionRoles.Governor && s.City(TargetId)?.NationId != s.State.PlayerNationId) { s.Message("Наместником можно поставить только в свой город."); return; }
            CompanionSystem.SetRole(s, c, Role, TargetId);
        }
    }

    public sealed class SetTechPriority : IGameCommand
    {
        public string Branch; public SetTechPriority(string b) { Branch = b; }
        public void Execute(GameSession s) { var n = s.PlayerNation; if (n != null) n.PriorityBranch = Branch; }
    }

    public sealed class InvestEssence : IGameCommand
    {
        public void Execute(GameSession s) => TechSystem.InvestEssence(s);
    }

    public sealed class ProposeTreaty : IGameCommand
    {
        public string NationId, Agreement; public bool Personal;
        public ProposeTreaty(string n, string a, bool personal = false) { NationId = n; Agreement = a; Personal = personal; }
        public void Execute(GameSession s) { var n = s.Nation(NationId); if (n != null) Diplomacy.Propose(s, n, Agreement, Personal); }
    }

    public sealed class DeclareWar : IGameCommand
    {
        public string NationId; public DeclareWar(string n) { NationId = n; }
        public void Execute(GameSession s)
        {
            if (s.State.PlayerNationId == null) return;
            Diplomacy.DeclareWar(s, s.State.PlayerNationId, NationId);
            NationSystem.Remember(s, s.Nation(NationId), "hero_declared_war", -20);
        }
    }

    public sealed class RecruitArmy : IGameCommand
    {
        public string CityId; public float Strength;
        public RecruitArmy(string c, float str) { CityId = c; Strength = str; }
        public void Execute(GameSession s)
        {
            var c = s.City(CityId); if (c == null || c.NationId != s.State.PlayerNationId) return;
            ArmySystem.Recruit(s, c, Strength);
        }
    }

    public sealed class OrderArmy : IGameCommand
    {
        public string ArmyId, CityId;
        public OrderArmy(string a, string c) { ArmyId = a; CityId = c; }
        public void Execute(GameSession s)
        {
            var a = s.Army(ArmyId); var c = s.City(CityId);
            if (a == null || a.NationId != s.State.PlayerNationId) return;
            if (c != null && c.NationId != null && c.NationId != s.State.PlayerNationId && !Diplomacy.AtWar(s, a.NationId, c.NationId))
            { s.Message("Сначала объявите войну."); return; }
            ArmySystem.Order(s, a, c);
        }
    }

    public sealed class SendSettlers : IGameCommand
    {
        public string CityId; public SendSettlers(string c) { CityId = c; }
        public void Execute(GameSession s)
        {
            var c = s.City(CityId); var n = s.PlayerNation;
            if (c == null || n == null || c.NationId != n.Id) return;
            if (c.StageIndex < 1) { s.Message("Нужна хотя бы деревня."); return; }
            if (n.Get("food") < 20) { s.Message("Нужно 20 еды в запасах."); return; }
            if (c.Population < s.Balance.city.settlerCostPop + 8) { s.Message("Мало людей."); return; }
            n.Add("food", -20);
            ArmySystem.SendSettlers(s, n, c, null, false);
        }
    }

    public sealed class PersuadeCity : IGameCommand
    {
        public string CityId; public PersuadeCity(string c) { CityId = c; }
        public void Execute(GameSession s)
        {
            var c = s.City(CityId); var pn = s.PlayerNation; var hero = s.HeroUnit;
            if (c == null || pn == null || c.NationId == pn.Id) return;
            bool personal = hero != null && Vec2.Distance(hero.Pos, c.Pos) < 30f;
            if (!personal)
            {
                if (pn.Influence < 30) { s.Message("Нужно 30 влияния или прийти в город лично."); return; }
                pn.Influence -= 30;
            }
            var owner = s.Nation(c.NationId);
            if (owner != null && c.AutonomyState == Autonomy.Loyal) { s.Message($"{c.Name} верен {owner.Name}."); return; }
            float ch = 0.2f + HeroSystem.EffSpeech(s) * 0.004f + (float)s.State.Hero.Reputation * 0.005f + (personal ? 0.15f : 0);
            if (c.AutonomyState == Autonomy.Vulnerable) ch += 0.2f;
            if (owner != null && !owner.Leader.Alive) ch += 0.2f;
            if (c.RebelLeaderId != null) { var g = s.Companion(c.RebelLeaderId); if (g != null) ch += g.Relation / 200f; }
            ch = MathUtil.Clamp(ch, 0.05f, 0.9f);
            if (s.Rng.Chance(ch))
            {
                CitySystem.JoinNation(s, c, pn, 50);
                s.Message($"{c.Name} присоединяется к {pn.Name}! (шанс {ch:P0})");
                Chronicle.Add(s, "city_joined", new System.Collections.Generic.Dictionary<string, string> { ["city"] = c.Name, ["nation"] = pn.Name });
                if (owner != null) NationSystem.Remember(s, owner, "hero_stole_city", -15);
                EndingSystem.CheckMilitary(s);
            }
            else { s.Message($"{c.Name} отказывается (шанс {ch:P0})."); c.Loyalty = Math.Min(100, c.Loyalty + 5); }
        }
    }

    public sealed class ActivateMirror : IGameCommand
    {
        public void Execute(GameSession s)
        {
            foreach (var c in s.State.Cities)
                if (c.NationId == s.State.PlayerNationId && c.HasBuilding("mirror_of_sky")) { EndingSystem.OnMegastructure(s, c, "mirror_of_sky"); return; }
            s.Message("Зеркало небес ещё не построено.");
        }
    }

    public sealed class DepositAll : IGameCommand
    {
        public void Execute(GameSession s)
        {
            var cap = s.Capital(s.PlayerNation); var hero = s.HeroUnit;
            if (cap == null || hero == null) return;
            if (Vec2.Distance(cap.Pos, hero.Pos) > 15f) { s.Message("Сдать добычу можно только у своего костра или склада."); return; }
            InteractionSystem.Deposit(s, cap);
        }
    }
}
