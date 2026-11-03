using System.IO;
using Immortal.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Immortal.EditorTools
{
    /// <summary>
    /// Готовит проект: сцена Boot с GameRoot, сцена в Build Settings, PanelSettings для UI Toolkit,
    /// URP-ассет (если конвейер не назначен), обработка ввода «Both». Запускается автоматически один раз
    /// и вручную через меню Immortal → Bootstrap Project.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectBootstrap
    {
        public const string ScenePath = "Assets/Scenes/Boot.unity";

        static ProjectBootstrap()
        {
            EditorApplication.delayCall += () =>
            {
                if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (!File.Exists(ScenePath)) Run();
            };
        }

        /// <summary>
        /// Создаёт Assets/Scenes/Boot.unity с камерой и GameRoot.
        /// Если открыта безымянная сцена (свежий проект), Unity не даёт добавить сцену к ней,
        /// поэтому Boot создаётся вместо неё (с предложением сохранить изменения, если они есть).
        /// Если открыта сохранённая сцена — Boot создаётся рядом и закрывается, работа пользователя не трогается.
        /// </summary>
        static bool CreateBootScene()
        {
            var active = SceneManager.GetActiveScene();
            bool untitled = string.IsNullOrEmpty(active.path);
            if (untitled && active.isDirty && !Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                Debug.LogWarning("Immortal: создание сцены Boot отменено. Запустите Immortal → Bootstrap Project позже.");
                return false;
            }

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, untitled ? NewSceneMode.Single : NewSceneMode.Additive);
            var cam = new GameObject("Main Camera") { tag = "MainCamera" };
            cam.AddComponent<Camera>();
            var root = new GameObject("GameRoot");
            root.AddComponent<GameRoot>();
            if (!untitled)
            {
                SceneManager.MoveGameObjectToScene(cam, scene);
                SceneManager.MoveGameObjectToScene(root, scene);
            }
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                Debug.LogError("Immortal: не удалось сохранить " + ScenePath);
                return false;
            }
            if (!untitled) EditorSceneManager.CloseScene(scene, true);
            return true;
        }

        [MenuItem("Immortal/Bootstrap Project")]
        public static void Run()
        {
            Directory.CreateDirectory("Assets/Scenes");
            Directory.CreateDirectory("Assets/Resources/UI");
            Directory.CreateDirectory("Assets/Settings");

            // 1. Сцена Boot.
            if (!File.Exists(ScenePath) && !CreateBootScene()) return;
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            // 2. PanelSettings (тема по умолчанию, если в проекте есть).
            const string psPath = "Assets/Resources/UI/GamePanelSettings.asset";
            if (AssetDatabase.LoadAssetAtPath<PanelSettings>(psPath) == null)
            {
                var ps = ScriptableObject.CreateInstance<PanelSettings>();
                ps.scaleMode = PanelScaleMode.ScaleWithScreenSize;
                ps.referenceResolution = new Vector2Int(1920, 1080);
                ps.match = 0.5f;
                foreach (var guid in AssetDatabase.FindAssets("t:ThemeStyleSheet"))
                {
                    ps.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(AssetDatabase.GUIDToAssetPath(guid));
                    if (ps.themeStyleSheet != null) break;
                }
                AssetDatabase.CreateAsset(ps, psPath);
            }

            // 3. URP-конвейер, если проект открыт без шаблона Universal 3D.
#if IMMORTAL_URP
            if (GraphicsSettings.defaultRenderPipeline == null)
            {
                try
                {
                    var data = ScriptableObject.CreateInstance<UnityEngine.Rendering.Universal.UniversalRendererData>();
                    AssetDatabase.CreateAsset(data, "Assets/Settings/Immortal_Renderer.asset");
                    var rp = UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset.Create(data);
                    AssetDatabase.CreateAsset(rp, "Assets/Settings/Immortal_URP.asset");
                    GraphicsSettings.defaultRenderPipeline = rp;
                    QualitySettings.renderPipeline = rp;
                }
                catch (System.Exception e) { Debug.LogWarning("URP не настроен автоматически: " + e.Message + ". Игра работает и во встроенном конвейере."); }
            }
#endif

            // 4. Обработка ввода: оба варианта (код поддерживает и Input System, и старый Input Manager).
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
            if (settings.Length > 0)
            {
                var so = new SerializedObject(settings[0]);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue != 2) { prop.intValue = 2; so.ApplyModifiedProperties(); Debug.Log("Обработка ввода переключена на «Both». Перезапустите редактор, если Unity попросит."); }
            }
            PlayerSettings.productName = "Immortal";
            PlayerSettings.companyName = "Immortal Team";
            PlayerSettings.runInBackground = true;
            AssetDatabase.SaveAssets();
            Debug.Log("Immortal: проект подготовлен. Откройте Assets/Scenes/Boot.unity и нажмите Play.");
        }
    }

    /// <summary>Сборки: Standalone для игры и для сценариев снимков. Вызываются из tools/*.sh через -executeMethod.</summary>
    public static class BuildScript
    {
        [MenuItem("Immortal/Build Standalone")]
        public static void BuildStandalone()
        {
            ProjectBootstrap.Run();
            var target = EditorUserBuildSettings.activeBuildTarget;
            string ext = target == BuildTarget.StandaloneWindows64 ? ".exe" : target == BuildTarget.StandaloneOSX ? ".app" : ".x86_64";
            string path = System.Environment.GetEnvironmentVariable("IMMORTAL_BUILD_PATH") ?? "Build/Immortal" + ext;
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ProjectBootstrap.ScenePath }, locationPathName = path, target = target, options = BuildOptions.None
            });
            Debug.Log($"Сборка: {report.summary.result}, {report.summary.totalSize / (1024 * 1024)} МБ → {path}");
            if (Application.isBatchMode) EditorApplication.Exit(report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
        }

        /// <summary>Проверка компиляции и контента без сборки (для tools/check).</summary>
        public static void ValidateContent()
        {
            var db = Core.Content.ContentDatabase.Load(n => File.ReadAllText($"Assets/Resources/Data/{n}.json"));
            var errors = Core.Content.ContentValidator.Validate(db);
            foreach (var e in errors) Debug.LogError("[Контент] " + e);
            Debug.Log($"Валидация контента: ошибок {errors.Count}");
            if (Application.isBatchMode) EditorApplication.Exit(errors.Count == 0 ? 0 : 1);
        }
    }
}
