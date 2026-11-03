using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Immortal.Core.Content
{
    /// <summary>Весь контент игры. Загружается из набора JSON-строк (имя файла без расширения → текст).</summary>
    public sealed class ContentDatabase
    {
        public static readonly string[] FileNames =
        {
            "balance", "terrain", "regions", "points_of_interest", "eras", "tech", "items", "recruits",
            "enemies", "abilities", "buildings", "settlement_stages", "nations", "events", "chronicle", "simulation_hints", "objectives"
        };

        public BalanceDef Balance;
        public TerrainFile TerrainFile;
        public List<RegionDef> Regions;
        public List<PoiDef> PoiDefs;
        public List<EraDef> Eras;
        public TechFile TechFile;
        public ItemsFile ItemsFile;
        public RecruitsFile RecruitsFile;
        public List<EnemyDef> EnemyList;
        public List<AbilityDef> AbilityList;
        public List<BuildingDef> BuildingList;
        public List<StageDef> Stages;
        public List<NationDef> NationDefs;
        public EventsFile EventsFile;
        public ChronicleFile ChronicleFile;
        public HintsFile Hints;
        public List<ObjectiveDef> Objectives;

        public readonly Dictionary<string, TerrainDef> Terrain = new Dictionary<string, TerrainDef>();
        public readonly List<TerrainDef> TerrainByIndex = new List<TerrainDef>();
        public readonly Dictionary<string, int> TerrainIndex = new Dictionary<string, int>();
        public readonly Dictionary<string, NodeDef> Nodes = new Dictionary<string, NodeDef>();
        public readonly Dictionary<string, RegionDef> RegionById = new Dictionary<string, RegionDef>();
        public readonly Dictionary<string, PoiDef> Poi = new Dictionary<string, PoiDef>();
        public readonly Dictionary<string, EraDef> Era = new Dictionary<string, EraDef>();
        public readonly Dictionary<string, TechDef> Tech = new Dictionary<string, TechDef>();
        public readonly Dictionary<string, ItemDef> Items = new Dictionary<string, ItemDef>();
        public readonly Dictionary<string, RecipeDef> Recipes = new Dictionary<string, RecipeDef>();
        public readonly Dictionary<string, WeaponTypeDef> WeaponTypes = new Dictionary<string, WeaponTypeDef>();
        public readonly Dictionary<string, QualityDef> Qualities = new Dictionary<string, QualityDef>();
        public readonly Dictionary<string, LootTableDef> LootTables = new Dictionary<string, LootTableDef>();
        public readonly Dictionary<string, RecruitDef> Recruits = new Dictionary<string, RecruitDef>();
        public readonly Dictionary<string, EnemyDef> Enemies = new Dictionary<string, EnemyDef>();
        public readonly Dictionary<string, AbilityDef> Abilities = new Dictionary<string, AbilityDef>();
        public readonly Dictionary<string, BuildingDef> Buildings = new Dictionary<string, BuildingDef>();
        public readonly Dictionary<string, int> StageIndex = new Dictionary<string, int>();
        public readonly Dictionary<string, NationDef> Nations = new Dictionary<string, NationDef>();
        public readonly Dictionary<string, DialogueDef> Dialogues = new Dictionary<string, DialogueDef>();
        public readonly Dictionary<string, EventDef> Events = new Dictionary<string, EventDef>();

        public static ContentDatabase Load(Func<string, string> readFile)
        {
            var db = new ContentDatabase();
            T Read<T>(string name) where T : new()
            {
                string json = readFile(name);
                if (string.IsNullOrEmpty(json)) throw new Exception($"Нет файла контента {name}.json");
                try { return JsonConvert.DeserializeObject<T>(json) ?? new T(); }
                catch (Exception e) { throw new Exception($"Ошибка разбора {name}.json: {e.Message}", e); }
            }

            db.Balance = Read<BalanceDef>("balance");
            db.TerrainFile = Read<TerrainFile>("terrain");
            db.Regions = Read<RegionsFile>("regions").regions;
            db.PoiDefs = Read<PoiFile>("points_of_interest").pois;
            db.Eras = Read<ErasFile>("eras").eras;
            db.TechFile = Read<TechFile>("tech");
            db.ItemsFile = Read<ItemsFile>("items");
            db.RecruitsFile = Read<RecruitsFile>("recruits");
            db.EnemyList = Read<EnemiesFile>("enemies").enemies;
            db.AbilityList = Read<AbilitiesFile>("abilities").abilities;
            db.BuildingList = Read<BuildingsFile>("buildings").buildings;
            db.Stages = Read<StagesFile>("settlement_stages").stages;
            db.NationDefs = Read<NationsFile>("nations").nations;
            db.EventsFile = Read<EventsFile>("events");
            db.ChronicleFile = Read<ChronicleFile>("chronicle");
            db.Hints = Read<HintsFile>("simulation_hints");
            db.Objectives = Read<ObjectivesFile>("objectives").objectives;
            db.Index();
            return db;
        }

        void Index()
        {
            foreach (var t in TerrainFile.terrains) { TerrainIndex[t.id] = TerrainByIndex.Count; TerrainByIndex.Add(t); Terrain[t.id] = t; }
            foreach (var n in TerrainFile.nodes) Nodes[n.id] = n;
            foreach (var r in Regions) RegionById[r.id] = r;
            foreach (var p in PoiDefs) Poi[p.id] = p;
            Eras.Sort((a, b) => a.order.CompareTo(b.order));
            foreach (var e in Eras) Era[e.id] = e;
            foreach (var t in TechFile.techs) Tech[t.id] = t;
            foreach (var i in ItemsFile.items) Items[i.id] = i;
            foreach (var r in ItemsFile.recipes) Recipes[r.id] = r;
            foreach (var w in ItemsFile.weaponTypes) WeaponTypes[w.id] = w;
            foreach (var q in ItemsFile.qualities) Qualities[q.id] = q;
            foreach (var l in ItemsFile.lootTables) LootTables[l.id] = l;
            foreach (var r in RecruitsFile.recruits) Recruits[r.id] = r;
            foreach (var e in EnemyList) Enemies[e.id] = e;
            foreach (var a in AbilityList) Abilities[a.id] = a;
            foreach (var b in BuildingList) Buildings[b.id] = b;
            for (int i = 0; i < Stages.Count; i++) StageIndex[Stages[i].id] = i;
            foreach (var n in NationDefs) Nations[n.id] = n;
            foreach (var d in EventsFile.dialogues) Dialogues[d.id] = d;
            foreach (var e in EventsFile.events) Events[e.id] = e;
        }

        public int EraOrder(string eraId) => eraId != null && Era.TryGetValue(eraId, out var e) ? e.order : 0;
        public EraDef NextEra(string eraId)
        {
            int o = EraOrder(eraId);
            foreach (var e in Eras) if (e.order == o + 1) return e;
            return null;
        }

        public string ItemName(string id) => Items.TryGetValue(id, out var d) ? d.name : id;

        public string RandomTemplate(string category, IRandom rng)
        {
            if (ChronicleFile.templates.TryGetValue(category, out var list) && list.Count > 0)
                return list[rng.NextInt(list.Count)];
            return "{text}";
        }
    }
}
