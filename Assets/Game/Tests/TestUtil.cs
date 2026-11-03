using System;
using System.IO;
using Immortal.Core;
using Immortal.Core.Commands;
using Immortal.Core.Content;
using Immortal.Core.Narrative;

namespace Immortal.Tests
{
    /// <summary>Общие помощники тестов: загрузка контента, новая партия, прокрутка тиков.</summary>
    public static class TestUtil
    {
        static ContentDatabase _db;

        public static string DataDir()
        {
#if UNITY_5_3_OR_NEWER
            return Path.Combine(UnityEngine.Application.dataPath, "Resources", "Data");
#else
            string dir = AppContext.BaseDirectory;
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string probe = Path.Combine(dir, "Assets", "Resources", "Data");
                if (Directory.Exists(probe)) return probe;
                dir = Path.GetDirectoryName(dir);
            }
            string env = Environment.GetEnvironmentVariable("IMMORTAL_DATA");
            if (!string.IsNullOrEmpty(env)) return env;
            throw new DirectoryNotFoundException("Не найдена папка Assets/Resources/Data");
#endif
        }

        public static ContentDatabase Content()
        {
            if (_db == null)
            {
                string dir = DataDir();
                _db = ContentDatabase.Load(n => File.ReadAllText(Path.Combine(dir, n + ".json")));
            }
            return _db;
        }

        /// <summary>Новая партия с закрытым вступительным диалогом.</summary>
        public static GameSession NewGame(int seed = 1234, IRandom rng = null)
        {
            var s = GameSession.NewGame(Content(), seed, rng);
            CloseAll(s);
            return s;
        }

        public static void CloseAll(GameSession s)
        {
            for (int i = 0; i < 20; i++)
            {
                if (s.State.Narrative.ChronicleOpen) s.Execute(new CloseChronicle());
                if (s.State.Narrative.Active != null) DialogueEngine.AutoResolve(s);
                if (!s.State.Narrative.ChronicleOpen && s.State.Narrative.Active == null) break;
            }
        }

        public static void Ticks(GameSession s, int n, bool closeDialogs = true)
        {
            for (int i = 0; i < n; i++)
            {
                if (closeDialogs && (s.State.Narrative.Active != null || s.State.Narrative.ChronicleOpen)) CloseAll(s);
                s.Tick();
                s.Events.Clear();
                if (s.State.GameOver) return;
            }
        }

        public static bool TickUntil(GameSession s, Func<bool> cond, int maxTicks, bool closeDialogs = true)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                if (cond()) return true;
                Ticks(s, 1, closeDialogs);
                if (s.State.GameOver) return cond();
            }
            return cond();
        }

        public static int Seconds(GameSession s, float sec) => (int)(sec * s.TicksPerSecond);
    }
}
