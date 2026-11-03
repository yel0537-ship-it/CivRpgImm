using System;
using System.Collections.Generic;
using UnityEngine;

namespace Immortal.Presentation
{
    /// <summary>
    /// Процедурные звуки-заглушки. Используются, только если в Resources нет настоящего клипа:
    /// игра звучит сразу, а замена — просто положить файл с тем же именем.
    /// </summary>
    public static class SynthClips
    {
        const int Rate = 22050;
        static readonly Dictionary<string, AudioClip> Cache = new Dictionary<string, AudioClip>();
        static readonly System.Random Rnd = new System.Random(1234);

        public static AudioClip Get(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (Cache.TryGetValue(name, out var c)) return c;
            c = Build(name);
            Cache[name] = c;
            return c;
        }

        static AudioClip Make(string name, float seconds, Func<float, float> f)
        {
            int n = Mathf.Max(1, (int)(seconds * Rate));
            var data = new float[n];
            for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate), -1f, 1f);
            var clip = AudioClip.Create("synth_" + name, n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        static float Noise() => (float)(Rnd.NextDouble() * 2 - 1);
        static float Sine(float freq, float t) => Mathf.Sin(2 * Mathf.PI * freq * t);
        static float Env(float t, float attack, float decay) => t < attack ? t / attack : Mathf.Exp(-(t - attack) / decay);

        static AudioClip Build(string name)
        {
            float lp = 0;
            switch (name)
            {
                case "hit":
                    return Make(name, 0.16f, t => { lp += (Noise() - lp) * 0.35f; return (lp * 0.8f + Sine(110 - t * 200, t) * 0.6f) * Env(t, 0.002f, 0.04f); });
                case "hit_hero":
                    return Make(name, 0.22f, t => { lp += (Noise() - lp) * 0.2f; return (lp * 0.6f + Sine(70, t) * 0.8f) * Env(t, 0.003f, 0.06f); });
                case "death":
                    return Make(name, 0.45f, t => (Sine(220 - t * 300, t) * 0.5f + Noise() * 0.1f) * Env(t, 0.01f, 0.15f));
                case "swing":
                    return Make(name, 0.2f, t => { lp += (Noise() - lp) * (0.05f + t * 1.5f); return lp * 1.4f * Mathf.Sin(Mathf.PI * t / 0.2f); });
                case "strike":
                    return Make(name, 0.14f, t => { lp += (Noise() - lp) * 0.12f; return lp * 1.2f * Mathf.Sin(Mathf.PI * t / 0.14f); });
                case "windup":
                    return Make(name, 0.12f, t => Sine(660, t) * 0.25f * Env(t, 0.005f, 0.04f));
                case "shoot":
                    return Make(name, 0.15f, t => (Sine(500 - t * 1500, t) * 0.4f + Noise() * 0.15f) * Env(t, 0.002f, 0.05f));
                case "cast":
                    return Make(name, 0.4f, t => (Sine(300 + t * 900, t) * 0.35f + Sine(600 + t * 1800, t) * 0.15f) * Env(t, 0.05f, 0.15f));
                case "explosion":
                    return Make(name, 0.7f, t => { lp += (Noise() - lp) * 0.08f; return (lp * 1.6f + Sine(55, t) * 0.7f) * Env(t, 0.005f, 0.18f); });
                case "pickup":
                    return Make(name, 0.16f, t => Sine(t < 0.07f ? 880 : 1320, t) * 0.3f * Env(t, 0.003f, 0.05f));
                case "essence":
                    return Make(name, 0.9f, t => (Sine(660, t) + Sine(990 * (1 + 0.01f * Sine(6, t)), t) * 0.6f + Sine(1320, t) * 0.3f) * 0.18f * Env(t, 0.05f, 0.3f));
                case "gather":
                    return Make(name, 0.09f, t => { lp += (Noise() - lp) * 0.3f; return (lp * 0.7f + Sine(180, t) * 0.5f) * Env(t, 0.001f, 0.025f); });
                case "footstep":
                    return Make(name, 0.06f, t => { lp += (Noise() - lp) * 0.15f; return lp * 0.5f * Env(t, 0.002f, 0.015f); });
                case "hoof":
                    return Make(name, 0.08f, t => (Sine(140, t) * 0.4f + Noise() * 0.1f) * Env(t, 0.001f, 0.02f));
                case "dash":
                    return Make(name, 0.25f, t => { lp += (Noise() - lp) * (0.02f + t); return lp * 1.5f * Mathf.Sin(Mathf.PI * t / 0.25f); });
                case "ui_click":
                    return Make(name, 0.035f, t => Sine(1800, t) * 0.2f * Env(t, 0.001f, 0.008f));
                case "ui_open":
                    return Make(name, 0.14f, t => Sine(t < 0.06f ? 520 : 780, t) * 0.18f * Env(t, 0.005f, 0.05f));
                case "levelup":
                case "objective":
                    return Make(name, 0.6f, t => Sine(t < 0.15f ? 523 : t < 0.3f ? 659 : 784, t) * 0.25f * Env(t % 0.15f, 0.005f, 0.12f));
                case "recruit":
                    return Make(name, 0.7f, t => (Sine(392, t) + Sine(494, t) + Sine(587, t)) * 0.1f * Env(t, 0.03f, 0.25f));
                case "sad":
                    return Make(name, 1.2f, t => (Sine(330, t) + Sine(392, t) + Sine(494, t) * 0.5f) * 0.1f * Env(t, 0.1f, 0.5f));
                case "build":
                    return Make(name, 0.5f, t => { float k = t % 0.16f; lp += (Noise() - lp) * 0.3f; return (lp * 0.5f + Sine(220, t) * 0.4f) * Env(k, 0.001f, 0.03f); });
                case "zone_enter":
                    return Make(name, 1.4f, t => (Sine(65, t) + Sine(69, t) + Sine(131 * (1 + 0.02f * Sine(3, t)), t) * 0.5f) * 0.15f * Mathf.Sin(Mathf.PI * t / 1.4f));
                case "era":
                case "timejump":
                    return Make(name, 2.2f, t => (Sine(220, t) + Sine(277, t) + Sine(330, t) + Sine(440, t) * 0.5f) * 0.09f * Env(t, 0.4f, 0.8f));
                case "dialogue":
                    return Make(name, 0.2f, t => Sine(440, t) * 0.12f * Env(t, 0.01f, 0.08f));
                // ---- Музыкальные подложки (зацикливаются) ----
                case "music_world": return Pad(name, new[] { new[] { 220f, 261.6f, 329.6f }, new[] { 174.6f, 220f, 261.6f }, new[] { 261.6f, 329.6f, 392f }, new[] { 196f, 246.9f, 293.7f } }, 4f, 0.07f);
                case "music_menu": return Pad(name, new[] { new[] { 146.8f, 174.6f, 220f }, new[] { 130.8f, 164.8f, 196f } }, 6f, 0.07f);
                case "music_chronicle": return Pad(name, new[] { new[] { 196f, 246.9f, 293.7f }, new[] { 220f, 261.6f, 329.6f } }, 5f, 0.06f);
                case "music_smoke":
                    return Make(name, 12f, t => (Sine(55, t) + Sine(58.3f, t) + Sine(110 * (1 + 0.01f * Sine(0.3f, t)), t) * 0.4f) * 0.08f * (0.7f + 0.3f * Sine(0.25f, t)));
                case "music_combat":
                    return Make(name, 8f, t =>
                    {
                        float beat = t % 0.5f;
                        lp += (Noise() - lp) * 0.2f;
                        float drum = (Sine(60, beat) * 0.7f + lp * 0.2f) * Env(beat, 0.002f, 0.08f);
                        float bass = Sine(t % 4f < 2f ? 110f : 98f, t) * 0.12f;
                        return (drum * 0.5f + bass) * 0.8f;
                    });
                case "ambient_smoke":
                    return Make(name, 10f, t => { lp += (Noise() - lp) * 0.01f; return lp * 0.6f * (0.6f + 0.4f * Sine(0.2f, t)); });
                case "ambient_wind":
                    return Make(name, 10f, t => { lp += (Noise() - lp) * 0.02f; return lp * 0.35f * (0.5f + 0.5f * Sine(0.13f, t)); });
                default:
                    return null;
            }
        }

        static AudioClip Pad(string name, float[][] chords, float chordSeconds, float volume)
        {
            float total = chords.Length * chordSeconds;
            return Make(name, total, t =>
            {
                int ci = Mathf.Min(chords.Length - 1, (int)(t / chordSeconds));
                float local = t - ci * chordSeconds;
                float env = Mathf.Clamp01(local / 1.2f) * Mathf.Clamp01((chordSeconds - local) / 1.2f);
                float v = 0;
                foreach (var f in chords[ci]) v += Sine(f, t) + Sine(f * 2.001f, t) * 0.2f;
                return v * volume * env;
            });
        }
    }
}
