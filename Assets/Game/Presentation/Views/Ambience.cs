using System.Collections.Generic;
using Immortal.Core;
using Immortal.Core.World;
using UnityEngine;

namespace Immortal.Presentation
{
    /// <summary>
    /// Чисто визуальная жизнь мира, не влияющая на симуляцию: жители бродят между постройками
    /// (их число растёт с населением, в голод — меньше и медленнее), погода и частицы по регионам.
    /// Префабы-замены: unit:citizen, fx:weather_&lt;регион&gt;, fx:weather_smoke.
    /// </summary>
    public sealed class Ambience
    {
        readonly Transform _root;
        readonly WorldView _world;
        GameSession _s;

        sealed class Citizen { public GameObject Go; public Vector3 Target; public float Speed, Wait; public IUnitAnimator Anim; public Vector3 Last; }
        readonly Dictionary<string, List<Citizen>> _citizens = new Dictionary<string, List<Citizen>>();
        readonly System.Random _rnd = new System.Random(5);

        GameObject _weatherGo;
        ParticleSystem _weather;
        string _weatherKey;

        public Ambience(Transform parent, WorldView world)
        {
            _root = new GameObject("Ambience").transform;
            _root.SetParent(parent, false);
            _world = world;
        }

        public void Bind(GameSession s)
        {
            _s = s;
            foreach (var l in _citizens.Values) foreach (var c in l) Object.Destroy(c.Go);
            _citizens.Clear();
            _weatherKey = null;
            if (_weatherGo != null) Object.Destroy(_weatherGo);
        }

        /// <summary>Куда показывать реплики (назначает GameRoot).</summary>
        public System.Action<Transform, string> Say;
        /// <summary>Как найти вид спутника по id юнита (для реплик в отряде).</summary>
        public System.Func<int, Transform> UnitAnchor;
        float _barkTimer = 4f, _companionTimer = 25f;

        public void Update(Vec2 hero, Vector3 heroWorld, float dt)
        {
            if (_s == null) return;
            UpdateCitizens(hero, dt);
            UpdateWeather(hero, heroWorld);
            UpdateBarks(heroWorld, dt);
        }

        // ---------- Реплики: государство слышно через людей ----------
        void UpdateBarks(Vector3 heroWorld, float dt)
        {
            if (Say == null) return;
            _barkTimer -= dt; _companionTimer -= dt;
            if (_barkTimer <= 0)
            {
                _barkTimer = 5f + (float)_rnd.NextDouble() * 5f;
                foreach (var kv in _citizens)
                {
                    var city = _s.City(kv.Key);
                    foreach (var z in kv.Value)
                    {
                        if (Vector3.Distance(z.Go.transform.position, heroWorld) > 11f) continue;
                        Say(z.Go.transform, Line(CitizenKey(city), null));
                        return;
                    }
                }
            }
            if (_companionTimer <= 0)
            {
                _companionTimer = 30f + (float)_rnd.NextDouble() * 30f;
                var party = _s.State.Hero.PartyCompanionUnits;
                if (party.Count == 0 || _s.State.Hero.CombatTimer > 0) return;
                var u = _s.FindUnit(party[_rnd.Next(party.Count)]);
                var c = u != null ? _s.Companion(u.CompanionId) : null;
                if (c == null) return;
                string key = _s.State.Hero.InSmoke ? "bark_smoke" : c.Relation < -15 ? "bark_companion_sour" : "bark_companion";
                Say(UnitAnchor?.Invoke(u.Id), Line(key, c.Name));
            }
        }

        string CitizenKey(CityState city)
        {
            var ns = _s.State.Narrative;
            var keys = new List<string>();
            if (city != null && city.Hungry) { keys.Add("bark_hungry"); keys.Add("bark_hungry"); }
            foreach (var r in _s.State.Relations) if (r.War && (r.A == _s.State.PlayerNationId || r.B == _s.State.PlayerNationId)) { keys.Add("bark_war"); break; }
            if (ns.Taboo == "strict") keys.Add("bark_taboo_strict");
            if (ns.Flags.Contains("taboo_broken")) keys.Add("bark_taboo_broken");
            if (ns.Flags.Contains("inequality_known")) keys.Add("bark_inequality");
            if (ns.Flags.Contains("legend_of_return")) keys.Add("bark_legend");
            if (ns.Flags.Contains("worshipped")) keys.Add("bark_worshipped");
            if (_s.Content.EraOrder(_s.PlayerEra) == 1) keys.Add("bark_city_states");
            if (_s.Content.EraOrder(_s.PlayerEra) >= 2) keys.Add("bark_ancient");
            foreach (var n in _s.State.Nations) if (!n.IsPlayer && !n.Leader.Alive) { keys.Add("bark_regency"); break; }
            if (keys.Count == 0 || _rnd.NextDouble() < 0.3) return "bark_default";
            return keys[_rnd.Next(keys.Count)];
        }

        string Line(string key, string companion)
        {
            if (!_s.Content.ChronicleFile.templates.TryGetValue(key, out var list) || list.Count == 0) return null;
            string rival = "соседи";
            foreach (var n in _s.State.Nations) if (!n.IsPlayer) { rival = n.Name; break; }
            return list[_rnd.Next(list.Count)].Replace("{hero}", _s.State.Hero.Name).Replace("{companion}", companion ?? "")
                .Replace("{rival}", rival).Replace("{capital}", _s.Capital(_s.PlayerNation)?.Name ?? "");
        }

        // ---------- Жители ----------
        void UpdateCitizens(Vec2 hero, float dt)
        {
            var seen = new HashSet<string>();
            foreach (var c in _s.State.Cities)
            {
                if (Vec2.Distance(c.Pos, hero) > 75f || !Vision.IsExplored(_s.State, _s.Map.TileAt(c.Pos))) continue;
                if (_s.State.Hero.InSmoke) continue;
                seen.Add(c.Id);
                if (!_citizens.TryGetValue(c.Id, out var list)) { list = new List<Citizen>(); _citizens[c.Id] = list; }
                int want = Mathf.Clamp((int)(c.Population / 6f), 1, 14);
                if (c.Hungry) want = Mathf.Max(1, want / 2);
                while (list.Count < want) list.Add(Spawn(c));
                while (list.Count > want) { Object.Destroy(list[list.Count - 1].Go); list.RemoveAt(list.Count - 1); }
                foreach (var z in list) Walk(c, z, dt, c.Hungry ? 0.5f : 1f);
            }
            var drop = new List<string>();
            foreach (var kv in _citizens) if (!seen.Contains(kv.Key)) drop.Add(kv.Key);
            foreach (var k in drop) { foreach (var z in _citizens[k]) Object.Destroy(z.Go); _citizens.Remove(k); }
        }

        Citizen Spawn(CityState c)
        {
            var n = _s.Nation(c.NationId);
            var tint = Color.Lerp(new Color(0.75f, 0.7f, 0.62f), n != null ? Look.Hex(n.Color) : Color.gray, 0.25f);
            if (c.Hungry) tint *= 0.6f;
            GameObject go;
            IUnitAnimator anim;
            var ve = VisualCatalog.Resolve("unit", "citizen", "npc");
            if (ve != null)
            {
                go = Object.Instantiate(ve.Prefab, _root);
                go.transform.localScale *= ve.scale;
                var a = go.GetComponentInChildren<Animator>();
                if (a != null) { var br = go.AddComponent<AnimatorBridge>(); br.Init(a); anim = br; }
                else { var pa = go.AddComponent<ProceduralAnimator>(); pa.Init(go.transform.childCount > 0 ? go.transform.GetChild(0) : go.transform, false); anim = pa; }
            }
            else
            {
                go = new GameObject("citizen");
                go.transform.SetParent(_root, false);
                var body = ViewFactory.BuildHumanoid(go.transform, tint, 0.85f, new Unit { Kind = UnitKind.Npc }, out _);
                var pa = go.AddComponent<ProceduralAnimator>(); pa.Init(body, false); anim = pa;
            }
            var start = _world.Ground(c.Pos + new Vec2((float)_rnd.NextDouble() * 6 - 3, (float)_rnd.NextDouble() * 6 - 3));
            go.transform.position = start;
            return new Citizen { Go = go, Target = start, Speed = 1.2f + (float)_rnd.NextDouble() * 0.8f, Anim = anim, Last = start };
        }

        void Walk(CityState c, Citizen z, float dt, float speedMul)
        {
            var p = z.Go.transform.position;
            if (z.Wait > 0) z.Wait -= dt;
            else if (Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(z.Target.x, 0, z.Target.z)) < 0.4f)
            {
                z.Wait = (float)_rnd.NextDouble() * 4f;
                Vec2 dest = c.Pos;
                if (c.Buildings.Count > 0) dest = c.Buildings[_rnd.Next(c.Buildings.Count)].Pos;
                dest += new Vec2((float)_rnd.NextDouble() * 4 - 2, (float)_rnd.NextDouble() * 4 - 2);
                if (_s.Map.Walkable(dest)) z.Target = _world.Ground(dest);
            }
            else
            {
                var dir = new Vector3(z.Target.x - p.x, 0, z.Target.z - p.z).normalized;
                var np = p + dir * z.Speed * speedMul * dt;
                np.y = _world.GroundHeight(Look.ToCore(np));
                z.Go.transform.position = np;
                if (dir.sqrMagnitude > 0.01f) z.Go.transform.rotation = Quaternion.Slerp(z.Go.transform.rotation, Quaternion.LookRotation(dir), dt * 6f);
            }
            var cur = z.Go.transform.position;
            float speed = dt > 0 ? new Vector2(cur.x - z.Last.x, cur.z - z.Last.z).magnitude / dt : 0;
            z.Last = cur;
            z.Anim?.Tick(new UnitAnimState { Speed = speed }, dt);
        }

        // ---------- Погода ----------
        void UpdateWeather(Vec2 hero, Vector3 heroWorld)
        {
            string key = _s.State.Hero.InSmoke ? "smoke" : _s.Map.RegionAt(hero).id;
            if (key != _weatherKey)
            {
                _weatherKey = key;
                if (_weatherGo != null) Object.Destroy(_weatherGo);
                _weatherGo = null; _weather = null;
                var ve = VisualCatalog.Resolve("fx", "weather_" + key);
                if (ve != null) { _weatherGo = Object.Instantiate(ve.Prefab, _root); }
                else BuildWeather(key);
            }
            if (_weatherGo != null) _weatherGo.transform.position = heroWorld + Vector3.up * (key == "r_marsh" || key == "smoke" ? 1.5f : 12f);
        }

        void BuildWeather(string key)
        {
            // Параметры: цвет, размер, скорость падения, ветер, частота, время жизни.
            Color col; float size, fall, wind, rate, life;
            switch (key)
            {
                case "r_tundra": col = new Color(1, 1, 1, 0.8f); size = 0.12f; fall = 3f; wind = 0.8f; rate = 260; life = 5f; break;
                case "r_dunes": col = new Color(0.9f, 0.72f, 0.45f, 0.35f); size = 0.08f; fall = 0.3f; wind = 5f; rate = 160; life = 4f; break;
                case "r_marsh": col = new Color(0.75f, 0.8f, 0.78f, 0.12f); size = 3.5f; fall = 0f; wind = 0.4f; rate = 10; life = 9f; break;
                case "r_forest": col = new Color(0.55f, 0.6f, 0.25f, 0.8f); size = 0.18f; fall = 1.2f; wind = 0.6f; rate = 14; life = 8f; break;
                case "r_steppe": col = new Color(1f, 0.95f, 0.75f, 0.5f); size = 0.06f; fall = 0.2f; wind = 2.5f; rate = 30; life = 5f; break;
                case "smoke": col = new Color(0.7f, 0.55f, 1f, 0.6f); size = 0.15f; fall = -0.3f; wind = 0.2f; rate = 70; life = 4f; break;
                default: return;
            }
            _weatherGo = new GameObject("Weather " + key);
            _weatherGo.transform.SetParent(_root, false);
            _weather = _weatherGo.AddComponent<ParticleSystem>();
            _weather.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _weather.main;
            main.duration = 5f; main.loop = true;
            main.startLifetime = life;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.6f, size * 1.4f);
            main.startColor = col;
            main.maxParticles = 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0f;
            var em = _weather.emission; em.rateOverTime = rate;
            var sh = _weather.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(70, key == "r_marsh" ? 2 : 1, 70);
            var vel = _weather.velocityOverLifetime;
            vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(wind * 0.6f, wind * 1.2f);
            vel.y = new ParticleSystem.MinMaxCurve(-fall * 1.2f, -fall * 0.8f);
            vel.z = new ParticleSystem.MinMaxCurve(-wind * 0.3f, wind * 0.3f);
            var col2 = _weather.colorOverLifetime; col2.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                         new[] { new GradientAlphaKey(0, 0), new GradientAlphaKey(1, 0.2f), new GradientAlphaKey(1, 0.8f), new GradientAlphaKey(0, 1) });
            col2.color = grad;
            var r = _weatherGo.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Look.Transparent;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            _weather.Play();
        }
    }
}
