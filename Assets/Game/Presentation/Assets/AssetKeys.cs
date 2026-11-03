using System.Collections.Generic;
using Immortal.Core.Content;

namespace Immortal.Presentation
{
    /// <summary>Один слот ассета: категория, id и подсказка, где он используется.</summary>
    public struct AssetSlot
    {
        public string Category, Id, Note;
        public AssetSlot(string c, string id, string note) { Category = c; Id = id; Note = note; }
        public string Key => Category + ":" + Id;
    }

    /// <summary>
    /// Полный список слотов, которые игра умеет заполнять своими ассетами. Строится из контента,
    /// поэтому новый враг или постройка в JSON сразу появляются в окне «Immortal → Покрытие ассетов».
    /// </summary>
    public static class AssetKeys
    {
        public static readonly string[] FxKeys = { "hit", "death", "explosion", "levelup", "rejuvenate", "smoke_zone" };

        public static List<AssetSlot> Visuals(ContentDatabase db)
        {
            var list = new List<AssetSlot>
            {
                new AssetSlot("unit", "hero", "герой (рост ~1.8 м, кость оружия hand_r)"),
                new AssetSlot("unit", "companion", "спутник по умолчанию"),
                new AssetSlot("unit", "npc", "житель / человек для вербовки"),
                new AssetSlot("unit", "leader", "бессмертный правитель"),
                new AssetSlot("unit", "horse", "конь героя"),
            };
            foreach (var r in db.RecruitsFile.recruits) list.Add(new AssetSlot("unit", r.id, $"спутник {r.name} (иначе companion)"));
            foreach (var n in db.NationDefs) if (!n.player) list.Add(new AssetSlot("unit", "leader_" + n.id, $"правитель {n.leaderName} (иначе leader)"));
            foreach (var e in db.EnemyList) list.Add(new AssetSlot("unit", e.id, e.name));
            foreach (var b in db.BuildingList) list.Add(new AssetSlot("building", b.id, b.name));
            list.Add(new AssetSlot("building", "wall_stone", "сегмент каменной стены"));
            list.Add(new AssetSlot("building", "wall_wood", "сегмент частокола"));
            foreach (var n in db.TerrainFile.nodes) list.Add(new AssetSlot("node", n.id, n.name + " (инстансинг: только MeshFilter+MeshRenderer)"));
            foreach (var p in db.PoiDefs) if (p.kind != "zone") list.Add(new AssetSlot("poi", p.id, p.name));
            foreach (var i in db.ItemsFile.items)
            {
                if (!i.resource) list.Add(new AssetSlot("item", i.id, i.name + " (в руке героя)"));
                list.Add(new AssetSlot("loot", i.id, i.name + " на земле"));
            }
            foreach (var a in db.AbilityList) if (a.type == "projectile") list.Add(new AssetSlot("projectile", a.id, a.name));
            list.Add(new AssetSlot("projectile", "enemy_shot", "выстрел врага"));
            foreach (var a in db.AbilityList)
            {
                if (a.type == "arc" || a.type == "buff") list.Add(new AssetSlot("fx", "slash_" + a.id, a.name + " — взмах"));
                if (a.radius > 0 && (a.type == "projectile" || a.type == "area")) list.Add(new AssetSlot("fx", "explosion_" + a.id, a.name + " — взрыв"));
            }
            foreach (var f in FxKeys) list.Add(new AssetSlot(f == "smoke_zone" ? "zone" : "fx", f, "эффект"));
            return list;
        }

        public static List<AssetSlot> Textures(ContentDatabase db)
        {
            var list = new List<AssetSlot>();
            foreach (var t in db.TerrainFile.terrains) list.Add(new AssetSlot("terrain", t.id, t.name));
            foreach (var k in new[] { "hero", "companion", "npc", "descendant", "leader", "narrator" }) list.Add(new AssetSlot("portrait", k, "портрет по умолчанию"));
            foreach (var r in db.RecruitsFile.recruits) list.Add(new AssetSlot("portrait", r.id, "портрет " + r.name));
            foreach (var n in db.NationDefs) if (!n.player) list.Add(new AssetSlot("portrait", "leader_" + n.id, "портрет " + n.leaderName));
            foreach (var a in db.AbilityList) list.Add(new AssetSlot("icon_ability", a.id, a.name));
            list.Add(new AssetSlot("icon_ability", "dash", "рывок"));
            foreach (var i in db.ItemsFile.items) if (!i.resource) list.Add(new AssetSlot("icon_item", i.id, i.name));
            foreach (var b in db.BuildingList) list.Add(new AssetSlot("icon_building", b.id, b.name));
            return list;
        }

        public static string TexturePath(AssetSlot s) => s.Category switch
        {
            "terrain" => "Textures/Terrain/" + s.Id,
            "portrait" => "Portraits/" + s.Id,
            "icon_ability" => "Icons/Abilities/" + s.Id,
            "icon_item" => "Icons/Items/" + s.Id,
            "icon_building" => "Icons/Buildings/" + s.Id,
            _ => "Textures/" + s.Id
        };

        public static List<AssetSlot> Audio(ContentDatabase db)
        {
            var list = new List<AssetSlot>();
            foreach (var k in new[] { "hit", "hit_hero", "death", "strike", "windup", "swing", "shoot", "cast", "dash", "explosion", "pickup", "essence", "gather",
                                      "footstep", "hoof", "build", "ui_click", "ui_open", "dialogue", "levelup", "objective", "recruit", "sad", "zone_enter", "era", "timejump" })
                list.Add(new AssetSlot("sfx", k, "Resources/Audio/Sfx/" + k + " (+ варианты _1.._4)"));
            foreach (var a in db.AbilityList) list.Add(new AssetSlot("sfx", "ability_" + a.id, a.name + " (иначе swing/shoot/cast)"));
            foreach (var a in db.AbilityList) if (a.radius > 0) list.Add(new AssetSlot("sfx", "explosion_" + a.id, a.name + " — взрыв (иначе explosion)"));
            foreach (var m in new[] { "menu", "world", "combat", "smoke", "chronicle" }) list.Add(new AssetSlot("music", m, "Resources/Audio/Music/" + m));
            foreach (var r in db.Regions) list.Add(new AssetSlot("music", "region_" + r.id, "музыка региона " + r.name + " (иначе era_*, world)"));
            foreach (var e in db.Eras) if (e.implemented) list.Add(new AssetSlot("music", "era_" + e.id, "музыка эпохи " + e.name));
            list.Add(new AssetSlot("ambient", "smoke", "фон в дыму"));
            list.Add(new AssetSlot("ambient", "wind", "фон по умолчанию"));
            foreach (var r in db.Regions) list.Add(new AssetSlot("ambient", "region_" + r.id, "фон региона " + r.name));
            return list;
        }
    }
}
