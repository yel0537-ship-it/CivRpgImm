using System.Collections.Generic;

namespace Immortal.Core.Content
{
    // ---------- balance.json ----------
    public sealed class BalanceDef
    {
        public int ticksPerSecond = 20;
        public float secondsPerMonth = 60;
        public int startYear = 1;
        public int worldTiles = 384;
        public float tileSize = 2f;
        public int chunkTiles = 32;
        public int territoryCell = 8;
        public HeroBalance hero = new HeroBalance();
        public VisionBalance vision = new VisionBalance();
        public ActivationBalance activation = new ActivationBalance();
        public RecruitBalance recruit = new RecruitBalance();
        public CityBalance city = new CityBalance();
        public EssenceBalance essence = new EssenceBalance();
        public TimeBalance time = new TimeBalance();
        public EndingBalance endings = new EndingBalance();
        public WarBalance war = new WarBalance();
        public CompanionBalance companions = new CompanionBalance();
    }

    public sealed class HeroBalance
    {
        public float hpBase = 80, hpPerBody = 10, energyBase = 30, energyPerWill = 5, energyRegen = 3f;
        public float speed = 6.5f, mountedSpeedMult = 1.8f, radius = 0.5f;
        public float startVitalityYears = 70, maxVitalityYears = 90, startAge = 22;
        public float dashDistance = 6f, dashCooldown = 2.5f, dashEnergy = 8f;
        public float interactRange = 2.2f, gatherTime = 1.2f;
        public int xpPerPoint = 100;
        public float skillGain = 0.4f;
        public float youthBodyMin = 0.6f, ageMindBonus = 3f, ageSpeechBonus = 25f;
        public float defeatResourceLoss = 0.5f;
    }

    public sealed class VisionBalance { public float radius = 24f, smokeRadius = 7f; }
    public sealed class ActivationBalance { public float poiRadius = 55f, despawnRadius = 80f, npcRadius = 60f; public int maxActiveEnemies = 40, maxAnimals = 10; public float animalSpawnInterval = 4f; }

    public sealed class RecruitBalance
    {
        public float fullMargin = 0.15f, critBase = 0.93f, critPressure = 0.08f;
        public float giftBonus = 0.25f, needMatchBonus = 0.15f, helpBonus = 0.4f, repFactor = 0.003f, speechFactor = 0.004f;
        public float distrustPenalty = 0.07f;
        public float trickEnergy = 10, helpEnergy = 5, pressureEnergy = 15;
        public int giftAmount = 5;
        public float failCooldownMonths = 2, partialRelation = -10, fullRelation = 30;
        public int namedCompanionLimit = 12;
    }

    public sealed class CityBalance
    {
        public float foodPerPop = 1f, gatherPerPop = 0.8f, woodPerPop = 0.12f, stonePerPop = 0.08f;
        public float sciencePerPop = 0.03f, influencePerPop = 0.015f;
        public float growthRate = 0.02f, selfGrowthPop = 8f, starvationRate = 0.03f;
        public float baseHousing = 4f, loyaltyDistancePenalty = 0.03f;
        public float autonomyVulnerable = 40f, autonomyEntrenched = 25f, autonomyLegitimate = 12f;
        public float emigrationOvercrowd = 1.3f;
        public float settlerCostPop = 6f;
    }

    public sealed class EssenceBalance
    {
        public int rejuvenateCost = 3; public float rejuvenateYears = 15f; public float skillLoss = 0.15f; public int memoriesLost = 1;
        public float firstEssenceYears = 8f;
        public float sciencePerEssence = 40f;
        public float zoneGrowthPerYear = 0.6f, zoneShrinkPerKill = 0.25f, zoneMinRadius = 6f, zoneMaxRadius = 42f;
        public float poolRegenPerYear = 2f;
    }

    public sealed class TimeBalance
    {
        public List<int> jumpOptions = new List<int> { 25, 50, 100 };
        public int sleepYearsMin = 20, sleepYearsMax = 40;
        public float jumpVitalityFactor = 0.3f;
        public float fieldConversionPerPop = 0.02f;
    }

    public sealed class EndingBalance
    {
        public float influenceVictory = 4000f; public float influenceRatio = 2f;
        public int awakeningEssence = 25; public int awakeningHints = 3;
    }

    public sealed class WarBalance
    {
        public float armySpeed = 3f, battleRadius = 45f, expandRadius = 50f, soldiersPerUnit = 5f; public int maxSoldiersPerSide = 12;
        public float siegeRate = 0.06f, attritionRate = 0.12f, recruitCostFood = 2f;
        public float assassinationChance = 0.08f;
    }

    public sealed class CompanionBalance
    {
        public int maxParty = 3; public float errandMonths = 3; public float essenceErrandRisk = 0.12f;
        public float rebellionRelation = -30f; public float rebellionLoyalty = 35f;
    }

    // ---------- terrain.json ----------
    public sealed class TerrainFile
    {
        public List<TerrainDef> terrains = new List<TerrainDef>();
        public List<NodeDef> nodes = new List<NodeDef>();
    }

    public sealed class TerrainDef
    {
        public string id, name, color;
        public bool walkable = true;
        public float moveCost = 1f, height = 0f;
        public bool fertile;
        public List<NodeSpawn> nodes = new List<NodeSpawn>();
    }

    public sealed class NodeSpawn { public string node; public float chance; }

    public sealed class NodeDef
    {
        public string id, name, color, shape;
        public Dictionary<string, int> yields = new Dictionary<string, int>();
        public float regrowMonths = 12;
        public bool blocks;
        public string skill = "gathering";
    }

    // ---------- regions.json ----------
    public sealed class RegionsFile { public List<RegionDef> regions = new List<RegionDef>(); }

    public sealed class RegionDef
    {
        public string id, name, description;
        public string primary, secondary;
        public float secondaryChance = 0.3f, waterChance = 0.05f, mountainChance = 0.05f;
        public float danger = 1f;
        public List<string> fauna = new List<string>();
        public Dictionary<string, int> pois = new Dictionary<string, int>();
        public float cx, cy; // центр в долях мира 0..1
    }

    // ---------- points_of_interest.json ----------
    public sealed class PoiFile { public List<PoiDef> pois = new List<PoiDef>(); }

    public sealed class PoiDef
    {
        public string id, name, kind, description, color;
        public List<EnemyCount> enemies = new List<EnemyCount>();
        public Dictionary<string, int> rewards = new Dictionary<string, int>();
        public float science, xp, radius = 6f, respawnYears = -1, essencePool;
        public string memory, dialogue, lootTable;
        public bool capturable;
    }

    public sealed class EnemyCount { public string id; public int count; }

    // ---------- eras.json ----------
    public sealed class ErasFile { public List<EraDef> eras = new List<EraDef>(); }

    public sealed class EraDef
    {
        public string id, name, superpower, superpowerName, itemQuality, role, chapterTitle, chapterText, tint;
        public int order;
        public bool implemented;
        public List<string> smokeEntities = new List<string>();
        public string requiresStage;
        public int requiresTechs;
        public List<string> rangedWeapons = new List<string>();
    }

    // ---------- tech.json ----------
    public sealed class TechFile { public List<TechDef> techs = new List<TechDef>(); public List<BranchDef> branches = new List<BranchDef>(); }
    public sealed class BranchDef { public string id, name; }

    public sealed class TechDef
    {
        public string id, name, branch, era, description;
        public float cost;
        public List<string> prereqs = new List<string>();
        public List<string> unlockBuildings = new List<string>();
        public List<string> unlockRecipes = new List<string>();
        public Dictionary<string, float> bonus = new Dictionary<string, float>();
        public int requiresEssence;
        public string requiresBuilding;
        public bool randomDiscovery;
    }

    // ---------- items.json ----------
    public sealed class ItemsFile
    {
        public List<ItemDef> items = new List<ItemDef>();
        public List<RecipeDef> recipes = new List<RecipeDef>();
        public List<WeaponTypeDef> weaponTypes = new List<WeaponTypeDef>();
        public List<QualityDef> qualities = new List<QualityDef>();
        public List<AffixDef> affixes = new List<AffixDef>();
        public List<LootTableDef> lootTables = new List<LootTableDef>();
    }

    public sealed class ItemDef
    {
        public string id, name, slot, weaponType, era, color, description;
        public float damage, armor, value;
        public bool resource;
    }

    public sealed class RecipeDef
    {
        public string id, result, fieldQuality, workshopQuality, era, tech;
        public Dictionary<string, int> inputs = new Dictionary<string, int>();
    }

    public sealed class WeaponTypeDef { public string id, name, basic; public List<string> skills = new List<string>(); }
    public sealed class QualityDef { public string id, name; public int order; public float mult = 1f; public int affixes; }
    public sealed class AffixDef { public string id, name, stat; public float min, max; }
    public sealed class LootTableDef { public string id; public List<LootEntry> entries = new List<LootEntry>(); public float equipChance; public List<string> equipPool = new List<string>(); }
    public sealed class LootEntry { public string item; public int min = 1, max = 1; public float chance = 1f; }

    // ---------- recruits.json ----------
    public sealed class RecruitsFile { public List<RecruitDef> recruits = new List<RecruitDef>(); public List<string> genericNames = new List<string>(); public List<string> descendantSuffixes = new List<string>(); }

    public sealed class RecruitDef
    {
        public string id, name, region, bio, greeting, need, character, role;
        public int distrust;
        public float baseChance = 0.35f;
        public float lifespan = 60, startAge = 25;
        public float combat = 10, gathering = 10, speech = 10, mind = 4;
        public List<string> observeHints = new List<string>();
        public QuestDef quest;
        public string dialogue = "dlg_recruit";
        public string memory;
    }

    public sealed class QuestDef { public string type, item, poiKind, text, doneText; public int amount; }

    // ---------- enemies.json ----------
    public sealed class EnemiesFile { public List<EnemyDef> enemies = new List<EnemyDef>(); }

    public sealed class EnemyDef
    {
        public string id, name, kind = "enemy", behavior = "melee", color, lootTable, projectile;
        public float hp = 30, damage = 6, armor, speed = 4f, radius = 0.5f, attackRange = 1.5f, aggroRange = 10f;
        public float windup = 0.6f, recover = 0.7f, scale = 1f;
        public string ability;
        public float abilityCooldown = 7f;
        public int xp = 10;
        public float essence;
        public bool boss;
    }

    // ---------- abilities.json ----------
    public sealed class AbilitiesFile { public List<AbilityDef> abilities = new List<AbilityDef>(); }

    public sealed class AbilityDef
    {
        public string id, name, type, scaling = "body", buffStat, color, description;
        public float damage, range = 2f, arc = 90f, radius = 2f, speed = 18f, cooldown = 1f, energy, duration, buffValue, windup;
        public bool requiresSmoke, mystic;
        public int count = 1;
    }

    // ---------- buildings.json ----------
    public sealed class BuildingsFile { public List<BuildingDef> buildings = new List<BuildingDef>(); }

    public sealed class BuildingDef
    {
        public string id, name, era, minStage, color, description, service, tech, specialization;
        public Dictionary<string, int> cost = new Dictionary<string, int>();
        public float food, housing, health, science, influence, defense, loyalty, production;
        public float size = 2f, height = 2f;
        public int max = 99;
        public bool megastructure, interior;
    }

    // ---------- settlement_stages.json ----------
    public sealed class StagesFile { public List<StageDef> stages = new List<StageDef>(); }

    public sealed class StageDef
    {
        public string id, name, unlocksEra, role;
        public float minPop;
        public List<string> requiredBuildings = new List<string>();
        public Dictionary<string, int> cost = new Dictionary<string, int>();
        public float radius = 8f, influenceRadius = 24f, maxPop = 100f;
        public int slots = 8;
        public bool walls;
    }

    // ---------- nations.json ----------
    public sealed class NationsFile { public List<NationDef> nations = new List<NationDef>(); }

    public sealed class NationDef
    {
        public string id, name, color, leaderName, leaderTitle, personality, region, heirName;
        public bool player;
        public float startPop = 12;
        public List<string> cityNames = new List<string>();
        public List<string> leaderLines = new List<string>();
    }

    // ---------- events.json ----------
    public sealed class EventsFile
    {
        public List<DialogueDef> dialogues = new List<DialogueDef>();
        public List<EventDef> events = new List<EventDef>();
    }

    public sealed class DialogueDef
    {
        public string id, title;
        public string start = "start";
        public List<NodeDefN> nodes = new List<NodeDefN>();
    }

    public sealed class NodeDefN
    {
        public string id, speaker, text;
        public List<EffectDef> effects = new List<EffectDef>();
        public List<OptionDef> options = new List<OptionDef>();
    }

    public sealed class OptionDef
    {
        public string text, next;
        public List<CondDef> conds = new List<CondDef>();
        public List<EffectDef> effects = new List<EffectDef>();
        public CheckDef check;
    }

    public sealed class CheckDef
    {
        public string kind;     // recruit, attr, skill, leader
        public string approach; // gift, trick, help, pressure
        public string attr, skill;
        public float difficulty = 0.5f;
        public string full, partial, fail, crit;
    }

    public sealed class CondDef
    {
        public string flag, notFlag, era, minEra, item, attr, skill, memory, notMemory, taboo, minRole, role;
        public int itemMin = 1;
        public float attrMin, skillMin, energy, relationMin = -999, hints = -1, reputationMin = -999;
        public bool? tabooActive;
        public bool? hasWeapon, hasHorse, governor;
        public int companionsMin = -1, techsMin = -1;
        public float capitalPopMin = -1;
        public bool? hasNeedItem, questDone, questStarted, rejuvenation, hasNation, atWar, leaderAlive, notTaboo, inSmoke;
        public string cityStageMin, notKnownAllTraits;
        public float chance = -1;
    }

    public sealed class EffectDef
    {
        public string companionRole, companionTarget;
        public string setFlag, clearFlag, item, recruit, chronicle, chronicleCategory, spawn, memory, forgetMemory, taboo, treaty, hint, ending, message, attitudeNation, startDialogue;
        public int amount, spawnCount = 1;
        public float energy, vitality, rep, relation, loyalty, attitude, science, influence, companionLifespan, npcCooldown, xp;
        public int timeJump;
        public bool observe, npcHostile, rejuvenationUnlock, endDialogue, questStart, questComplete, killLeader, captureLeader, cityIndependent, cityReturn, startWar, makePeace, killCompanion, offerJump, removeCompanion;
        public float allLoyalty;
    }

    public sealed class EventDef
    {
        public string id, trigger, dialogue;
        public float chance = 1f;
        public bool once = true;
        public float cooldownYears;
        public List<CondDef> conds = new List<CondDef>();
    }

    // ---------- objectives.json ----------
    public sealed class ObjectivesFile { public List<ObjectiveDef> objectives = new List<ObjectiveDef>(); }

    /// <summary>Цель-подсказка: показывается, пока не выполнена. done — ИЛИ из наборов условий (внутри набора — И).</summary>
    public sealed class ObjectiveDef
    {
        public string id, title, text;
        public List<CondDef> show = new List<CondDef>();
        public List<List<CondDef>> done = new List<List<CondDef>>();
        public int xp;
    }

    // ---------- chronicle.json ----------
    public sealed class ChronicleFile { public Dictionary<string, List<string>> templates = new Dictionary<string, List<string>>(); }

    // ---------- simulation_hints.json ----------
    public sealed class HintsFile
    {
        public List<string> repeatedLines = new List<string>();
        public List<string> chronicleGlitches = new List<string>();
        public List<string> zoneGlitches = new List<string>();
        public List<string> faceLines = new List<string>();
        public List<string> leaderSuspicions = new List<string>();
    }
}
