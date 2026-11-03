using System.Collections.Generic;
using Newtonsoft.Json;

namespace Immortal.Core
{
    /// <summary>Полное состояние партии. Сериализуется в JSON целиком.</summary>
    public sealed class GameState
    {
        public int Version = 1;
        public int Seed;
        public ulong RngState;
        public long Tick;
        /// <summary>Мировое время в месяцах от начала партии.</summary>
        public double Months;
        public double LastWeekMonths;
        public double LastMonthMonths;
        public int NextId = 100;
        public int Chapter = 1;
        public string PlayerNationId;
        public string EndingId;
        public bool GameOver;

        public HeroState Hero = new HeroState();
        public List<Unit> Units = new List<Unit>();
        public List<Projectile> Projectiles = new List<Projectile>();
        public List<LootDrop> Loot = new List<LootDrop>();
        public List<AreaEffect> Areas = new List<AreaEffect>();
        public List<PoiState> Pois = new List<PoiState>();
        public List<NpcState> Npcs = new List<NpcState>();
        public List<CompanionState> Companions = new List<CompanionState>();
        public List<NationState> Nations = new List<NationState>();
        public List<CityState> Cities = new List<CityState>();
        public List<ArmyState> Armies = new List<ArmyState>();
        public List<RelationState> Relations = new List<RelationState>();
        public List<RoadState> Roads = new List<RoadState>();
        public WorldModState WorldMods = new WorldModState();
        public NarrativeState Narrative = new NarrativeState();
        public List<ChronicleEntry> Chronicle = new List<ChronicleEntry>();

        [JsonIgnore] public byte[] TerritoryOwner; // индекс нации+1 на клетку территории; пересчитывается
        [JsonIgnore] public int TerritoryCellsX;

        public string NewId(string prefix) => prefix + (NextId++);
    }

    public static class Roles
    {
        public const string Outcast = "outcast", Chief = "chief", King = "king", Cardinal = "cardinal", Myth = "myth";
        public static int Rank(string r) => r == Chief ? 1 : r == King ? 2 : r == Cardinal ? 3 : r == Myth ? 4 : 0;
    }

    public sealed class HeroState
    {
        public string Name = "Изгой";
        public int UnitId = 1;
        public float Body = 5, Mind = 4, Will = 4;
        public int AttributePoints;
        public int Xp;
        public Dictionary<string, float> Skills = new Dictionary<string, float>
        {
            ["combat"] = 5, ["gathering"] = 5, ["speech"] = 5, ["management"] = 0
        };
        public float Energy = 40;
        public float Influence;
        public double VitalityYears = 70;
        public double MaxVitalityYears = 90;
        public double StartAge = 22;
        public string Role = Roles.Outcast;
        public Dictionary<string, int> Inventory = new Dictionary<string, int>();
        public List<ItemInstance> Items = new List<ItemInstance>();
        public string WeaponId, ArmorId, TrinketId;
        public List<string> Memories = new List<string>();
        public List<string> KnownRecipes = new List<string>();
        public bool HasHorse, Mounted;
        public bool RejuvenationUnlocked;
        public int RejuvenationCount;
        public double Reputation;
        public int Defeats;
        public PendingAction Pending;
        public Dictionary<string, float> Cooldowns = new Dictionary<string, float>();
        public List<Buff> Buffs = new List<Buff>();
        public float GatherProgress;
        public List<int> PartyCompanionUnits = new List<int>();
        public bool InSmoke;
        public string SmokePoiId;
        public float CombatTimer; // >0 — недавно был бой

        public int Count(string item) => Inventory.TryGetValue(item, out var v) ? v : 0;
        public void Add(string item, int n)
        {
            int v = Count(item) + n;
            if (v <= 0) Inventory.Remove(item); else Inventory[item] = v;
        }
    }

    public sealed class PendingAction
    {
        public string Kind;   // unit, poi, node, building, loot
        public string Id;     // строковый id
        public int UnitId;
        public int Tile;
        public Vec2 Pos;
    }

    public sealed class Buff
    {
        public string Stat; public float Value; public float TimeLeft; public string Source;
    }

    public sealed class ItemInstance
    {
        public string Id;
        public string DefId;
        public string Quality;
        public Dictionary<string, float> Affixes = new Dictionary<string, float>();
    }

    public enum UnitKind { Hero, Companion, Enemy, Animal, Soldier, Leader, Npc, Spirit }
    public enum AiState { Idle, Wander, Chase, Windup, Recover, Flee, Follow, Dead }

    public static class Teams
    {
        public const int Player = 0, Wild = 1, Neutral = 2, NationBase = 10;
    }

    public sealed class Unit
    {
        public int Id;
        public UnitKind Kind;
        public string DefId;
        public string Name;
        public int Team;
        public Vec2 Pos, PrevPos, Vel, Facing = new Vec2(0, -1);
        public float Radius = 0.5f, Speed = 5f;
        public float Hp, MaxHp, Damage, Armor;
        public float AttackRange = 1.6f, AggroRange = 12f;
        public float Windup = 0.6f, Recover = 0.6f;
        public AiState Ai;
        public float StateTimer;
        public int TargetId;
        public Vec2 AttackDir;
        public string PoiId, CompanionId, NpcId, ArmyId, NationId;
        public Vec2 Home;
        public List<Vec2> Path;
        public int PathIndex;
        public bool HasMoveTarget;
        public Vec2 MoveTarget;
        public bool Provoked;
        public string Behavior; // melee, ranged, flee, passive
        public float DeadTimer;
        public bool InSmoke;
        public bool Boss;
    }

    public sealed class Projectile
    {
        public int Id; public int OwnerId; public int Team;
        public Vec2 Pos, Vel; public float Damage, Aoe, Radius = 0.4f, Life;
        public string AbilityId; public bool Mystic;
    }

    /// <summary>Отложенный/повторяющийся эффект на область (каменный дождь, огненный взрыв).</summary>
    public sealed class AreaEffect
    {
        public Vec2 Pos; public float Radius, Damage, Delay, Interval; public int Pulses = 1; public int Team; public int OwnerId;
        public string AbilityId; public bool Mystic;
    }

    public sealed class LootDrop
    {
        public int Id; public Vec2 Pos; public string Resource; public int Amount; public ItemInstance Item; public float Life = 120f;
    }

    public sealed class PoiState
    {
        public string Id, DefId, Name, RegionId;
        public Vec2 Pos;
        public float Radius;
        public bool Discovered, Visited, Cleared, Active;
        public string OwnerNationId;
        public int EnemiesRemaining;
        public double RespawnAtMonths = -1;
        public double LastUsedMonths;
        public float EssencePool;
        public string Data; // id спутника для могилы и т.п.
        public int Kills;
    }

    public sealed class NpcState
    {
        public string Id, RecruitDefId, Name;
        public Vec2 Pos, Home;
        public bool Recruited, Hostile, Alive = true;
        public double CooldownUntilMonths;
        public List<string> KnownTraits = new List<string>();
        public bool QuestStarted, QuestDone;
        public bool IsDescendant;
        public string AncestorCompanionId;
        public int FaceId, Generation;
        public int UnitId;
        public int Attempts;
        public string Need, Character; public int Distrust;
        public float InheritedRelation;
        public string AncestorName;
        public bool Met;
    }

    public static class CompanionRoles
    {
        public const string Party = "party", Camp = "camp", Errand = "errand", Governor = "governor",
            General = "general", Scholar = "scholar", Envoy = "envoy", Rebel = "rebel";
    }

    public sealed class CompanionState
    {
        public string Id, Name, RecruitDefId, NpcId;
        public double BirthMonths;
        public double LifespanYears;
        public string Need, Character; public int Distrust;
        public float Relation;
        public string Role = CompanionRoles.Camp;
        public string AssignmentId;
        public string ErrandKind;
        public double ErrandReturnMonths;
        public bool Alive = true;
        public double DeathMonths;
        public string ParentId;
        public int Generation, FaceId;
        public float Combat = 10, Gathering = 10, Speech = 10, Mind = 4;
        public int UnitId;
        public bool AskedForEssence;
        public string Bio;
        public double AgeYears(double nowMonths) => (nowMonths - BirthMonths) / 12.0;
    }

    public sealed class LeaderState
    {
        public string Name, Title, Personality;
        public bool Alive = true, Immortal = true, Captive;
        public double RegencyUntilMonths = -1;
        public string HeirName;
        public List<string> Memories = new List<string>();
        public int UnitId;
        public float SuspectsTruth;
    }

    public sealed class NationState
    {
        public string Id, DefId, Name, Color;
        public bool IsPlayer, Destroyed;
        public int Team;
        public string EraId = "primitive";
        public LeaderState Leader = new LeaderState();
        public string CapitalCityId, OriginalCapitalId;
        public Dictionary<string, float> Treasury = new Dictionary<string, float>();
        public Dictionary<string, float> TechProgress = new Dictionary<string, float>();
        public List<string> Researched = new List<string>();
        public string PriorityBranch = "economy";
        public float Influence, InfluenceTotal;
        public float MilitaryPool;
        public int CitiesFounded;
        public double NextAiMonths;
        public string HomeRegion;
        public float Get(string r) => Treasury.TryGetValue(r, out var v) ? v : 0f;
        public void Add(string r, float n) { Treasury[r] = Get(r) + n; }
    }

    public static class Autonomy
    {
        public const string Loyal = "loyal", Vulnerable = "vulnerable", Entrenched = "entrenched", Legitimate = "legitimate";
    }

    public sealed class BuildingState
    {
        public string DefId; public int Slot; public Vec2 Pos; public double BuiltMonths;
    }

    public sealed class CityState
    {
        public string Id, Name, NationId, OriginalNationId;
        public Vec2 Pos;
        public int StageIndex;
        public float Population = 1;
        public float FoodStock = 10;
        public float Satiety = 1, Provision = 1, Health = 1;
        public List<BuildingState> Buildings = new List<BuildingState>();
        public List<string> BuildQueue = new List<string>();
        public string Specialization = "economy";
        public string GovernorId;
        public float Loyalty = 80;
        public string AutonomyState = Autonomy.Loyal;
        public double FoundedMonths;
        public bool IsCapital;
        public float Garrison = 5;
        public string RebelLeaderId;
        public bool Hungry;
        public int Refugees;
        public bool HasBuilding(string id) { foreach (var b in Buildings) if (b.DefId == id) return true; return false; }
        public int CountBuilding(string id) { int n = 0; foreach (var b in Buildings) if (b.DefId == id) n++; return n; }
    }

    public static class ArmyStates { public const string Idle = "idle", Marching = "marching", Sieging = "sieging", Battle = "battle", Returning = "returning"; }

    public sealed class ArmyState
    {
        public string Id, NationId, HomeCityId, TargetCityId, GeneralId;
        public Vec2 Pos;
        public float Strength;
        public string State = ArmyStates.Idle;
        public List<Vec2> Path = new List<Vec2>();
        public int PathIndex;
        public bool Expanded;
        public string Kind = "army"; // army, settlers, raiders
        public Vec2 Destination;
    }

    public sealed class RelationState
    {
        public string A, B;
        public float Attitude;
        public bool War;
        public List<string> Agreements = new List<string>();
        public double LastWarMonths = -999;
    }

    public sealed class RoadState { public string FromCity, ToCity; public List<Vec2> Points = new List<Vec2>(); }

    public sealed class WorldModState
    {
        /// <summary>Переопределения типа местности поверх детерминированной базы (индекс тайла → id местности).</summary>
        public Dictionary<int, string> Terrain = new Dictionary<int, string>();
        /// <summary>Истощённые ресурсные узлы: индекс тайла → месяц восстановления (-1 — навсегда).</summary>
        public Dictionary<int, double> Depleted = new Dictionary<int, double>();
        public byte[] Explored;
        public int Revision;
    }

    public sealed class DialogueState
    {
        public string DialogueId, NodeId, EventId;
        public string NpcId, CompanionId, NationId, CityId, PoiId;
        public string LastOutcome;
        public float LastChance;
    }

    public sealed class NarrativeState
    {
        public HashSet<string> Flags = new HashSet<string>();
        public HashSet<string> FiredEvents = new HashSet<string>();
        public Dictionary<string, double> EventCooldowns = new Dictionary<string, double>();
        public DialogueState Active;
        public List<DialogueState> Queue = new List<DialogueState>();
        public string Taboo = "none"; // none, lenient, strict
        public int HintsSeen;
        public HashSet<string> HintIds = new HashSet<string>();
        public bool ChronicleOpen;
        public int ChronicleFrom;
        public List<int> JumpOptions = new List<int>();
        public string JumpReason;
        public List<string> RecentLines = new List<string>();
        public int GeneratedNameCounter;
    }

    public sealed class ChronicleEntry
    {
        public double Months; public string Text; public string Category; public bool Glitch;
    }
}
