using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Immortal.Presentation
{
    /// <summary>Описание визуала для одного ключа (unit:wolf, building:hut, node:tree, ...).</summary>
    public sealed class VisualEntry
    {
        /// <summary>Путь к префабу внутри любой папки Resources (без расширения).</summary>
        public string prefab;
        public float scale = 1f;
        public float yOffset;
        public float rotationY;
        /// <summary>Перекрасить модель в цвет нации (через _BaseColor/_Color).</summary>
        public bool tintByNation;
        /// <summary>Имя кости/узла для оружия (по умолчанию ищутся hand_r, Hand_R, RightHand, mixamorig:RightHand).</summary>
        public string handBone;
        /// <summary>Высота полоски здоровья над моделью; 0 — вычислить по габаритам.</summary>
        public float height;
        /// <summary>Длительность эффекта (для fx:*), если у префаба нет ParticleSystem.</summary>
        public float life = 2f;
        [JsonIgnore] public GameObject Prefab;
    }

    public sealed class VisualsFile
    {
        public Dictionary<string, string> folders = new Dictionary<string, string>();
        public Dictionary<string, VisualEntry> entries = new Dictionary<string, VisualEntry>();
    }

    /// <summary>
    /// Каталог визуала. Модель подхватывается без правки кода двумя способами:
    /// 1) по соглашению имён: префаб Resources/Models/Units/wolf.prefab для юнита wolf и т.п.;
    /// 2) явной записью в Resources/Presentation/visuals.json (путь, масштаб, смещение, кость оружия).
    /// Если ничего нет — ViewFactory строит примитив, и игра выглядит как сейчас.
    /// </summary>
    public static class VisualCatalog
    {
        static VisualsFile _file;
        static readonly Dictionary<string, VisualEntry> Cache = new Dictionary<string, VisualEntry>();
        static readonly VisualEntry None = new VisualEntry();

        static readonly Dictionary<string, string> DefaultFolders = new Dictionary<string, string>
        {
            ["unit"] = "Models/Units", ["building"] = "Models/Buildings", ["node"] = "Models/Nodes", ["poi"] = "Models/Pois",
            ["projectile"] = "Models/Projectiles", ["loot"] = "Models/Loot", ["item"] = "Models/Items", ["fx"] = "Fx", ["zone"] = "Fx"
        };

        static void Load()
        {
            if (_file != null) return;
            var ta = Resources.Load<TextAsset>("Presentation/visuals");
            try { _file = ta != null ? JsonConvert.DeserializeObject<VisualsFile>(ta.text) : new VisualsFile(); }
            catch (System.Exception e) { Debug.LogWarning("visuals.json: " + e.Message); _file = new VisualsFile(); }
            if (_file.folders == null) _file.folders = new Dictionary<string, string>();
            if (_file.entries == null) _file.entries = new Dictionary<string, VisualEntry>();
        }

        /// <summary>Первый найденный визуал по списку id (например, rec_ayna → companion). null — используйте примитив.</summary>
        public static VisualEntry Resolve(string category, params string[] ids)
        {
            Load();
            foreach (var id in ids)
            {
                if (string.IsNullOrEmpty(id)) continue;
                string key = category + ":" + id;
                if (!Cache.TryGetValue(key, out var e))
                {
                    e = None;
                    if (_file.entries.TryGetValue(key, out var explicitEntry) && !string.IsNullOrEmpty(explicitEntry.prefab))
                    {
                        explicitEntry.Prefab = Resources.Load<GameObject>(explicitEntry.prefab);
                        if (explicitEntry.Prefab != null) e = explicitEntry;
                        else Debug.LogWarning($"visuals.json: префаб {explicitEntry.prefab} для {key} не найден");
                    }
                    else
                    {
                        string folder = _file.folders.TryGetValue(category, out var f) ? f : DefaultFolders.TryGetValue(category, out var d) ? d : "Models";
                        var prefab = Resources.Load<GameObject>(folder + "/" + id);
                        if (prefab != null) e = new VisualEntry { prefab = folder + "/" + id, Prefab = prefab };
                    }
                    Cache[key] = e;
                }
                if (e != None) return e;
            }
            return null;
        }

        /// <summary>Сбросить кэш (после импорта новых ассетов в редакторе).</summary>
        public static void Reload() { _file = null; Cache.Clear(); }
    }
}
