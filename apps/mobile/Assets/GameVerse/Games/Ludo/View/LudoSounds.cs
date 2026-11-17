#nullable enable
using UnityEngine;

namespace GameVerse.Games.Ludo.View
{
    /// <summary>
    /// Ludo's sound effects, synthesised when the game loads: original, a few kilobytes, nothing to license (ADR-0011).
    /// A dice rattle (a burst of clicks), a soft tick per hop, a whoosh for a capture, a chime for a token reaching home and
    /// a short fanfare for a win. Muted with the shared sound setting.
    /// </summary>
    public sealed class LudoSounds : System.IDisposable
    {
        private const int Rate = 22050;
        private const string PrefKey = "gv.sound";

        private readonly GameObject host;
        private readonly AudioSource source;
        private readonly AudioClip rattle;
        private readonly AudioClip hop;
        private readonly AudioClip capture;
        private readonly AudioClip home;
        private readonly AudioClip win;

        public LudoSounds()
        {
            host = new GameObject("LudoSounds");
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            var random = new System.Random(7); // fixed: the same sounds every time
            rattle = Clip("rattle", 0.42f, (t, i) => Click(t, random));
            hop = Clip("hop", 0.07f, (t, _) => Tone(t, 880, 0.07f) * 0.35f);
            capture = Clip("capture", 0.4f, (t, _) => ((float)(random.NextDouble() * 2) - 1) * Envelope(t, 0.4f) * 0.4f * (1 - (t / 0.4f)) + (Tone(t, 220 + (600 * (1 - (t / 0.4f))), 0.4f) * 0.25f));
            home = Clip("home", 0.5f, (t, _) => (Tone(t, 660, 0.5f) + Tone(t, 990, 0.5f)) * 0.22f);
            win = Clip("win", 0.9f, (t, _) => Tone(t, t < 0.2f ? 523 : t < 0.4f ? 659 : t < 0.6f ? 784 : 1047, 0.9f) * 0.3f);
        }

        public static bool Enabled
        {
            get => PlayerPrefs.GetInt(PrefKey, 1) == 1;
            set => PlayerPrefs.SetInt(PrefKey, value ? 1 : 0);
        }

        public void Rattle() => Play(rattle);

        public void Hop(int step) => Play(hop, 1 + (step * 0.04f));

        public void Capture() => Play(capture);

        public void Home() => Play(home);

        public void Win() => Play(win);

        public void Dispose()
        {
            Object.Destroy(host);
            Object.Destroy(rattle);
            Object.Destroy(hop);
            Object.Destroy(capture);
            Object.Destroy(home);
            Object.Destroy(win);
        }

        private void Play(AudioClip clip, float pitch = 1)
        {
            if (Enabled)
            {
                source.pitch = pitch;
                source.PlayOneShot(clip);
            }
        }

        private static AudioClip Clip(string name, float seconds, System.Func<float, int, float> sample)
        {
            var data = new float[(int)(Rate * seconds)];
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = Mathf.Clamp(sample((float)i / Rate, i), -1, 1);
            }

            var clip = AudioClip.Create("ludo-" + name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Tone(float t, float hz, float length) => Mathf.Sin(2 * Mathf.PI * hz * t) * Envelope(t, length);

        /// <summary>A quick attack and an exponential release, so nothing clicks.</summary>
        private static float Envelope(float t, float length) => Mathf.Min(1, t * 400) * Mathf.Exp(-5 * t / length);

        /// <summary>Dice clatter: short noise bursts at irregular moments, getting further apart as the die settles.</summary>
        private static float Click(float t, System.Random random)
        {
            float[] hits = { 0.0f, 0.05f, 0.09f, 0.15f, 0.22f, 0.31f, 0.38f };
            foreach (var hit in hits)
            {
                var since = t - hit;
                if (since >= 0 && since < 0.018f)
                {
                    return ((float)(random.NextDouble() * 2) - 1) * (1 - (since / 0.018f)) * 0.5f;
                }
            }

            return 0;
        }
    }
}
