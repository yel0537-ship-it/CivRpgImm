using System.Collections.Generic;
using System.IO;
using Immortal.Core.Content;
using Immortal.Presentation;
using UnityEditor;
using UnityEngine;

namespace Immortal.EditorTools
{
    /// <summary>
    /// Окно «Immortal → Покрытие ассетов»: какие модели, текстуры и звуки уже свои, а какие пока заглушки.
    /// Кнопка «Создать папки» готовит структуру Resources под соглашение имён.
    /// </summary>
    public sealed class AssetCoverageWindow : EditorWindow
    {
        Vector2 _scroll;
        string _filter = "";
        bool _onlyMissing;
        List<(AssetSlot slot, bool found, string path)> _rows;

        [MenuItem("Immortal/Покрытие ассетов")]
        static void Open() => GetWindow<AssetCoverageWindow>("Ассеты Immortal").Refresh();

        void Refresh()
        {
            VisualCatalog.Reload();
            var db = ContentDatabase.Load(n => File.Exists($"Assets/Resources/Data/{n}.json") ? File.ReadAllText($"Assets/Resources/Data/{n}.json") : null);
            _rows = new List<(AssetSlot, bool, string)>();
            foreach (var s in AssetKeys.Visuals(db))
            {
                var e = VisualCatalog.Resolve(s.Category, s.Id);
                _rows.Add((s, e != null, e?.prefab ?? DefaultFolder(s.Category) + "/" + s.Id));
            }
            foreach (var s in AssetKeys.Textures(db))
            {
                string p = AssetKeys.TexturePath(s);
                _rows.Add((s, Resources.Load<Texture2D>(p) != null, p + (s.Category == "terrain" ? " (иначе процедурная)" : "")));
            }
            foreach (var s in AssetKeys.Audio(db))
            {
                string folder = s.Category == "sfx" ? "Audio/Sfx/" : s.Category == "music" ? "Audio/Music/" : "Audio/Ambient/";
                bool found = Resources.Load<AudioClip>(folder + s.Id) != null || Resources.Load<AudioClip>(folder + s.Id + "_1") != null;
                _rows.Add((s, found, folder + s.Id));
            }
        }

        static string DefaultFolder(string category) => category switch
        {
            "unit" => "Models/Units", "building" => "Models/Buildings", "node" => "Models/Nodes", "poi" => "Models/Pois",
            "projectile" => "Models/Projectiles", "loot" => "Models/Loot", "item" => "Models/Items", _ => "Fx"
        };

        void OnGUI()
        {
            if (_rows == null) Refresh();
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Обновить", GUILayout.Width(100))) Refresh();
            if (GUILayout.Button("Создать папки", GUILayout.Width(120))) CreateFolders();
            _onlyMissing = GUILayout.Toggle(_onlyMissing, "только заглушки", GUILayout.Width(130));
            _filter = EditorGUILayout.TextField(_filter);
            EditorGUILayout.EndHorizontal();
            int found = 0; foreach (var r in _rows) if (r.found) found++;
            EditorGUILayout.HelpBox($"Своих ассетов: {found} из {_rows.Count}. Пути — относительно любой папки Resources, без расширения. Подробности: docs/ASSETS.md", MessageType.Info);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var (slot, ok, path) in _rows)
            {
                if (_onlyMissing && ok) continue;
                if (!string.IsNullOrEmpty(_filter) && !slot.Key.Contains(_filter) && !(slot.Note ?? "").Contains(_filter)) continue;
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label(ok ? "✓" : "·", GUILayout.Width(16));
                GUILayout.Label(slot.Key, EditorStyles.boldLabel, GUILayout.Width(240));
                GUILayout.Label(path, GUILayout.Width(320));
                GUILayout.Label(slot.Note ?? "");
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        static void CreateFolders()
        {
            foreach (var d in new[] { "Models/Units", "Models/Buildings", "Models/Nodes", "Models/Pois", "Models/Projectiles", "Models/Loot", "Models/Items", "Fx",
                                      "Textures/Terrain", "Audio/Sfx", "Audio/Music", "Audio/Ambient" })
                Directory.CreateDirectory("Assets/Resources/" + d);
            AssetDatabase.Refresh();
        }
    }
}
