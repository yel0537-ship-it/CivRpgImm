using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace Immortal.Presentation
{
    public sealed class SoundDef
    {
        /// <summary>Пути к клипам в Resources. Пусто — ищутся Audio/Sfx/&lt;ключ&gt; и варианты &lt;ключ&gt;_1.._4.</summary>
        public List<string> clips = new List<string>();
        public float volume = 1f;
        public float pitchMin = 0.94f, pitchMax = 1.06f;
        /// <summary>Имя процедурной заглушки (SynthClips), если клипов нет.</summary>
        public string synth;
        public bool spatial = true;
        public float cooldown = 0.04f;
        /// <summary>Запасной ключ: ability:club_smash → swing.</summary>
        public string fallback;
    }

    public sealed class AudioFile
    {
        public Dictionary<string, SoundDef> sfx = new Dictionary<string, SoundDef>();
        public Dictionary<string, string> music = new Dictionary<string, string>();
        public Dictionary<string, string> ambient = new Dictionary<string, string>();
        public float musicFade = 2.5f;
        public float musicVolume = 0.6f;
    }

    /// <summary>Громкость и прочие настройки игрока (PlayerPrefs).</summary>
    public static class GameSettings
    {
        public static float Master { get => PlayerPrefs.GetFloat("imm.master", 0.8f); set => PlayerPrefs.SetFloat("imm.master", Mathf.Clamp01(value)); }
        public static float Music { get => PlayerPrefs.GetFloat("imm.music", 0.6f); set => PlayerPrefs.SetFloat("imm.music", Mathf.Clamp01(value)); }
        public static float Sfx { get => PlayerPrefs.GetFloat("imm.sfx", 0.8f); set => PlayerPrefs.SetFloat("imm.sfx", Mathf.Clamp01(value)); }
        public static bool DamageNumbers { get => PlayerPrefs.GetInt("imm.dmg", 1) == 1; set => PlayerPrefs.SetInt("imm.dmg", value ? 1 : 0); }
        public static bool CameraShake { get => PlayerPrefs.GetInt("imm.shake", 1) == 1; set => PlayerPrefs.SetInt("imm.shake", value ? 1 : 0); }
        public static bool SynthAudio { get => PlayerPrefs.GetInt("imm.synth", 1) == 1; set => PlayerPrefs.SetInt("imm.synth", value ? 1 : 0); }
    }

    /// <summary>
    /// Звук: одноразовые эффекты (пул 3D-источников), музыка с перекрёстным затуханием, фоновые петли.
    /// Каталог — Resources/Presentation/audio.json; клипы — Resources/Audio/Sfx|Music|Ambient/&lt;ключ&gt;.
    /// Нет клипа — играет процедурная заглушка (отключается в настройках).
    /// </summary>
    public sealed class AudioService : MonoBehaviour
    {
        AudioFile _file;
        readonly List<AudioSource> _pool = new List<AudioSource>();
        AudioSource _musicA, _musicB, _ambient;
        string _musicKey, _ambientKey;
        float _musicFade = 1f;
        Transform _listener;
        readonly Dictionary<string, AudioClip[]> _clipCache = new Dictionary<string, AudioClip[]>();
        readonly Dictionary<string, float> _lastPlayed = new Dictionary<string, float>();
        int _next;

        public static AudioService Create(Transform parent)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            var a = go.AddComponent<AudioService>();
            a.Init();
            return a;
        }

        void Init()
        {
            var ta = Resources.Load<TextAsset>("Presentation/audio");
            try { _file = ta != null ? JsonConvert.DeserializeObject<AudioFile>(ta.text) : new AudioFile(); }
            catch (System.Exception e) { Debug.LogWarning("audio.json: " + e.Message); _file = new AudioFile(); }
            _file.sfx ??= new Dictionary<string, SoundDef>();
            _file.music ??= new Dictionary<string, string>();
            _file.ambient ??= new Dictionary<string, string>();

            // Слушатель — у героя, а не у далёкой камеры, чтобы 3D-звук был внятным.
            foreach (var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) l.enabled = false;
            _listener = new GameObject("Listener").transform;
            _listener.SetParent(transform, false);
            _listener.gameObject.AddComponent<AudioListener>();

            for (int i = 0; i < 20; i++)
            {
                var src = new GameObject("sfx" + i).AddComponent<AudioSource>();
                src.transform.SetParent(transform, false);
                src.playOnAwake = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 8f; src.maxDistance = 70f;
                _pool.Add(src);
            }
            _musicA = MakeLoop("musicA"); _musicB = MakeLoop("musicB"); _ambient = MakeLoop("ambient");
        }

        AudioSource MakeLoop(string n)
        {
            var src = new GameObject(n).AddComponent<AudioSource>();
            src.transform.SetParent(transform, false);
            src.loop = true; src.playOnAwake = false; src.spatialBlend = 0f; src.volume = 0f;
            return src;
        }

        public void SetListener(Vector3 pos) { if (_listener != null) _listener.position = pos + Vector3.up * 2f; }

        // ---------- Эффекты ----------
        public void Play(string key, Vector3? at = null, float volume = 1f, string fallbackKey = null)
        {
            if (string.IsNullOrEmpty(key)) return;
            var (def, clip) = Resolve(key, 0);
            if (clip == null && fallbackKey != null) (def, clip) = Resolve(fallbackKey, 0);
            if (clip == null) return;
            float now = Time.unscaledTime;
            if (_lastPlayed.TryGetValue(key, out var last) && now - last < (def?.cooldown ?? 0.04f)) return;
            _lastPlayed[key] = now;
            var src = _pool[_next]; _next = (_next + 1) % _pool.Count;
            src.clip = clip;
            bool spatial = at.HasValue && (def == null || def.spatial);
            src.spatialBlend = spatial ? 1f : 0f;
            if (at.HasValue) src.transform.position = at.Value;
            src.pitch = Random.Range(def?.pitchMin ?? 0.95f, def?.pitchMax ?? 1.05f);
            src.volume = (def?.volume ?? 1f) * volume * GameSettings.Sfx * GameSettings.Master;
            src.Play();
        }

        (SoundDef, AudioClip) Resolve(string key, int depth)
        {
            _file.sfx.TryGetValue(key, out var def);
            var clips = Clips(key, def, "Audio/Sfx/");
            if (clips.Length > 0) return (def, clips[Random.Range(0, clips.Length)]);
            if (GameSettings.SynthAudio)
            {
                var synth = SynthClips.Get(def?.synth ?? key);
                if (synth != null) return (def, synth);
            }
            string fb = def?.fallback ?? (key.Contains(":") ? key.Substring(0, key.IndexOf(':')) : null);
            if (fb != null && depth < 3) { var (d2, c2) = Resolve(fb, depth + 1); return (def ?? d2, c2); }
            return (def, null);
        }

        AudioClip[] Clips(string key, SoundDef def, string folder)
        {
            if (_clipCache.TryGetValue(key, out var arr)) return arr;
            var list = new List<AudioClip>();
            if (def != null && def.clips != null)
                foreach (var p in def.clips) { var c = Resources.Load<AudioClip>(p); if (c != null) list.Add(c); }
            if (list.Count == 0)
            {
                string baseName = folder + key.Replace(':', '_');
                var c0 = Resources.Load<AudioClip>(baseName); if (c0 != null) list.Add(c0);
                for (int i = 1; i <= 4; i++) { var ci = Resources.Load<AudioClip>(baseName + "_" + i); if (ci != null) list.Add(ci); }
            }
            arr = list.ToArray();
            _clipCache[key] = arr;
            return arr;
        }

        // ---------- Музыка и фон ----------
        /// <summary>Ключи пробуются по порядку: region:r_forest → era:city_states → world.</summary>
        public void SetMusic(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (string.IsNullOrEmpty(k)) continue;
                var clip = LoopClip(k, _file.music, "Audio/Music/", "music_");
                if (clip == null) continue;
                if (k == _musicKey) return;
                _musicKey = k;
                var t = _musicA; _musicA = _musicB; _musicB = t; // A — новая, B — уходящая
                _musicA.clip = clip; _musicA.volume = 0; _musicA.Play();
                _musicFade = 0f;
                return;
            }
        }

        public void SetAmbient(params string[] keys)
        {
            foreach (var k in keys)
            {
                if (string.IsNullOrEmpty(k)) continue;
                var clip = LoopClip(k, _file.ambient, "Audio/Ambient/", "ambient_");
                if (clip == null) continue;
                if (k == _ambientKey) return;
                _ambientKey = k; _ambient.clip = clip; _ambient.Play();
                return;
            }
            _ambientKey = null; _ambient.Stop();
        }

        AudioClip LoopClip(string key, Dictionary<string, string> map, string folder, string synthPrefix)
        {
            string path = map.TryGetValue(key, out var p) ? p : folder + key.Replace(':', '_');
            var cacheKey = "loop|" + path;
            if (!_clipCache.TryGetValue(cacheKey, out var arr))
            {
                var c = Resources.Load<AudioClip>(path);
                arr = c != null ? new[] { c } : new AudioClip[0];
                _clipCache[cacheKey] = arr;
            }
            if (arr.Length > 0) return arr[0];
            return GameSettings.SynthAudio ? SynthClips.Get(synthPrefix + key) : null;
        }

        void Update()
        {
            float fade = Mathf.Max(0.1f, _file.musicFade);
            _musicFade = Mathf.Min(1f, _musicFade + Time.unscaledDeltaTime / fade);
            float mv = GameSettings.Music * GameSettings.Master * _file.musicVolume;
            _musicA.volume = mv * _musicFade;
            _musicB.volume = mv * (1f - _musicFade);
            if (_musicFade >= 1f && _musicB.isPlaying) _musicB.Stop();
            _ambient.volume = Mathf.MoveTowards(_ambient.volume, GameSettings.Sfx * GameSettings.Master * 0.5f, Time.unscaledDeltaTime);
        }
    }
}
