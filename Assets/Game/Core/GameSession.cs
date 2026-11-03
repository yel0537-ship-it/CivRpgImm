using System;
using System.Collections.Generic;
using Immortal.Core.Action;
using Immortal.Core.Character;
using Immortal.Core.Civilization;
using Immortal.Core.Content;
using Immortal.Core.Narrative;
using Immortal.Core.Time;
using Immortal.Core.World;

namespace Immortal.Core
{
    public interface IGameCommand
    {
        void Execute(GameSession s);
    }

    /// <summary>
    /// Корень симуляции: состояние, контент, генератор случайностей, производные кэши мира,
    /// выполнение команд и тики. Уровни детализации:
    /// 20 Гц рядом с героем, 1 Гц для загруженных регионов, раз в неделю/месяц для всего мира.
    /// </summary>
    public sealed class GameSession
    {
        public GameState State;
        public readonly ContentDatabase Content;
        public IRandom Rng;
        public WorldMap Map;
        public Pathfinder Paths;
        public FlowField Flow;
        public readonly List<GameEvent> Events = new List<GameEvent>();
        public readonly int TicksPerSecond;
        public readonly float Dt;
        /// <summary>Направление WASD на текущий тик (выставляется командой SetMoveInput).</summary>
        public Vec2 MoveInput;
        /// <summary>Во время прыжка во времени и в тестах интерактивные события не открываются.</summary>
        public bool InTimeJump;
        public bool AutoResolveDialogues;
        /// <summary>Растёт, когда открываются новые тайлы тумана (Presentation перекрашивает чанки).</summary>
        public int FogRevision;
        readonly Dictionary<int, Unit> _units = new Dictionary<int, Unit>();

        public BalanceDef Balance => Content.Balance;

        GameSession(ContentDatabase db, GameState state, IRandom rng)
        {
            Content = db;
            State = state;
            Rng = rng ?? new XorShiftRandom(state.RngState);
            TicksPerSecond = db.Balance.ticksPerSecond;
            Dt = 1f / TicksPerSecond;
            Map = new WorldMap(db, state);
            Paths = new Pathfinder(Map);
            Flow = new FlowField(Map);
            Vision.EnsureExplored(state, Map.Size);
            ReindexUnits();
            Map.RebuildBuildingBlockers(db, state);
            TerritorySystem.Recompute(this);
        }

        public static GameSession NewGame(ContentDatabase db, int seed, IRandom rng = null)
        {
            var state = new GameState { Seed = seed, RngState = (ulong)(uint)seed * 2654435761UL + 1442695040888963407UL };
            var s = new GameSession(db, state, rng);
            WorldSetup.Populate(s);
            s.SyncRng();
            return s;
        }

        public static GameSession FromState(ContentDatabase db, GameState state, IRandom rng = null)
        {
            return new GameSession(db, state, rng);
        }

        public void SyncRng() { State.RngState = Rng.State; }

        // ---------- Команды и тики ----------

        public void Execute(IGameCommand cmd)
        {
            if (cmd == null) return;
            if (State.GameOver && !(cmd is Commands.CloseChronicle)) return;
            cmd.Execute(this);
            SyncRng();
        }

        public bool TimeFrozen => State.GameOver || State.Narrative.Active != null || State.Narrative.ChronicleOpen;

        public void Tick()
        {
            if (TimeFrozen) return;
            State.Tick++;
            State.Months += 1.0 / (Balance.secondsPerMonth * TicksPerSecond);

            // Рядом с героем: полная симуляция.
            HeroSystem.TickRealtime(this, Dt);
            MovementSystem.Tick(this, Dt);
            InteractionSystem.Tick(this);
            AiSystem.Tick(this, Dt);
            CombatSystem.Tick(this, Dt);
            LootSystem.Tick(this, Dt);
            if (State.Tick % 5 == 0) HeroSystem.UpdateVision(this);
            if (State.Tick % 10 == 0) SpawnSystem.Tick(this);

            // Загруженные регионы: раз в секунду.
            if (State.Tick % TicksPerSecond == 0)
            {
                ArmySystem.TickRealtime(this, 1f);
                ZoneSystem.TickRealtime(this, 1f);
            }

            // Весь мир: раз в игровую неделю и месяц.
            while (State.Months - State.LastWeekMonths >= 0.25)
            {
                State.LastWeekMonths += 0.25;
                WorldSimulation.StepWeek(this);
            }
            while (State.Months - State.LastMonthMonths >= 1.0)
            {
                State.LastMonthMonths += 1.0;
                WorldSimulation.StepMonth(this);
                if (TimeFrozen) break;
            }
            SyncRng();
        }

        // ---------- События ----------
        public void Emit(GameEvent e) => Events.Add(e);
        public void Message(string text) => Events.Add(GameEvent.Msg(text));

        // ---------- Юниты ----------
        public void ReindexUnits()
        {
            _units.Clear();
            foreach (var u in State.Units) _units[u.Id] = u;
        }

        public Unit FindUnit(int id) => id != 0 && _units.TryGetValue(id, out var u) ? u : null;
        public Unit HeroUnit => FindUnit(State.Hero.UnitId);

        public Unit AddUnit(Unit u)
        {
            if (u.Id == 0) u.Id = State.NextId++;
            u.PrevPos = u.Pos;
            State.Units.Add(u);
            _units[u.Id] = u;
            return u;
        }

        public void RemoveUnit(Unit u)
        {
            State.Units.Remove(u);
            _units.Remove(u.Id);
        }

        public int CountUnits(Func<Unit, bool> pred)
        {
            int n = 0;
            foreach (var u in State.Units) if (pred(u)) n++;
            return n;
        }

        // ---------- Поиск сущностей ----------
        public NationState Nation(string id) { if (id == null) return null; foreach (var n in State.Nations) if (n.Id == id) return n; return null; }
        public NationState PlayerNation => Nation(State.PlayerNationId);
        public NationState NationByTeam(int team) { foreach (var n in State.Nations) if (n.Team == team) return n; return null; }
        public CityState City(string id) { if (id == null) return null; foreach (var c in State.Cities) if (c.Id == id) return c; return null; }
        public CompanionState Companion(string id) { if (id == null) return null; foreach (var c in State.Companions) if (c.Id == id) return c; return null; }
        public NpcState Npc(string id) { if (id == null) return null; foreach (var n in State.Npcs) if (n.Id == id) return n; return null; }
        public PoiState Poi(string id) { if (id == null) return null; foreach (var p in State.Pois) if (p.Id == id) return p; return null; }
        public ArmyState Army(string id) { if (id == null) return null; foreach (var a in State.Armies) if (a.Id == id) return a; return null; }

        public CityState Capital(NationState n) => n == null ? null : City(n.CapitalCityId);
        public string PlayerEra => PlayerNation?.EraId ?? "primitive";
        public EraDef PlayerEraDef => Content.Era[PlayerEra];

        public CityState NearestCity(Vec2 p, Func<CityState, bool> pred = null)
        {
            CityState best = null; float bd = float.MaxValue;
            foreach (var c in State.Cities)
            {
                if (pred != null && !pred(c)) continue;
                float d = Vec2.SqrDistance(c.Pos, p);
                if (d < bd) { bd = d; best = c; }
            }
            return best;
        }

        // ---------- Враждебность ----------
        public bool IsHostile(int a, int b)
        {
            if (a == b) return false;
            if (a == Teams.Neutral || b == Teams.Neutral) return false;
            if (a == Teams.Wild || b == Teams.Wild) return true;
            var na = TeamNationId(a); var nb = TeamNationId(b);
            if (na == null || nb == null) return false;
            return Diplomacy.AtWar(this, na, nb);
        }

        public string TeamNationId(int team)
        {
            if (team == Teams.Player) return State.PlayerNationId ?? "__hero";
            return NationByTeam(team)?.Id;
        }

        public int NationTeam(string nationId)
        {
            if (nationId == null) return Teams.Neutral;
            if (nationId == State.PlayerNationId) return Teams.Player;
            return Nation(nationId)?.Team ?? Teams.Neutral;
        }

        // ---------- Время ----------
        public int Year => WorldClock.Year(this);
        public string DateText => WorldClock.Format(this);
    }
}
