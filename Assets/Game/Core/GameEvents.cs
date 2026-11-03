namespace Immortal.Core
{
    public enum GameEventType
    {
        Hit, Death, Pickup, Message, AbilityCast, DialogueStarted, DialogueEnded, ChronicleOpened,
        EraChanged, StageChanged, BuildingBuilt, CityFounded, CityCaptured, Ending, Rejuvenated, Gathered,
        WorldChanged, TimeJumped, HeroDefeated, Discovered, Recruited, CompanionDied, TechResearched,
        BattleStarted, ZoneEntered, ZoneExited, Autosave, LevelUp, Crafted, OpenScreen,
        /// <summary>Юнит наносит удар (конец замаха) — для анимаций и звука.</summary>
        Strike,
        ObjectiveCompleted, WindupStarted
    }

    /// <summary>Одноразовое событие. Presentation считывает очередь после тика.</summary>
    public sealed class GameEvent
    {
        public GameEventType Type;
        public int UnitId;
        public Vec2 Pos;
        public float Value;
        public string Id;
        public string Text;
        public bool Crit;

        public static GameEvent Msg(string text) => new GameEvent { Type = GameEventType.Message, Text = text };
    }
}
