using Newtonsoft.Json;

namespace Immortal.Core
{
    /// <summary>GameState целиком сериализуется в JSON (Presentation пишет в Application.persistentDataPath).</summary>
    public static class SaveLoad
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            Formatting = Formatting.None
        };

        public static string Serialize(GameSession s)
        {
            s.SyncRng();
            return JsonConvert.SerializeObject(s.State, Settings);
        }

        public static string Serialize(GameState st) => JsonConvert.SerializeObject(st, Settings);

        public static GameState Deserialize(string json) => JsonConvert.DeserializeObject<GameState>(json, Settings);

        public static GameSession Load(Content.ContentDatabase db, string json)
        {
            var st = Deserialize(json);
            var s = GameSession.FromState(db, st);
            Character.CompanionSystem.RespawnParty(s);
            return s;
        }

        /// <summary>Хеш состояния для проверок детерминизма.</summary>
        public static ulong Hash(GameSession s) => MathUtil.Fnv1a(Serialize(s));
    }
}
