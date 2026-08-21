using System;
using System.Collections.Generic;
using System.IO;
using System.Media;
using System.Text;
using System.Threading.Tasks;

namespace GymClock
{
    /// <summary>A single tone plus an optional silent gap after it.</summary>
    public struct Tone
    {
        public readonly int Frequency;
        public readonly int Milliseconds;
        public readonly int GapMilliseconds;

        public Tone(int frequency, int milliseconds)
            : this(frequency, milliseconds, 0)
        {
        }

        public Tone(int frequency, int milliseconds, int gapMilliseconds)
        {
            Frequency = frequency;
            Milliseconds = milliseconds;
            GapMilliseconds = gapMilliseconds;
        }
    }

    /// <summary>
    /// Generates PCM WAV data in memory and plays it through SoundPlayer.
    /// No sound files to ship, no NuGet packages, and it is loud enough to carry
    /// across a gym when the laptop is plugged into the projector or a speaker.
    /// </summary>
    public static class Beeper
    {
        private const int SampleRate = 44100;

        private static readonly object PlayLock = new object();
        private static readonly Dictionary<string, byte[]> Cache = new Dictionary<string, byte[]>();

        public static bool Enabled = true;
        public static double Volume = 0.85;

        // Cue vocabulary used by the clock.
        public static void CueWorkStart() { Play(new Tone(1000, 160, 60), new Tone(1000, 320)); }
        public static void CueRestStart() { Play(new Tone(520, 380)); }
        public static void CueMoveStart() { Play(new Tone(650, 140, 70), new Tone(650, 140)); }
        public static void CuePrepStart() { Play(new Tone(700, 200)); }
        public static void CueCountdown() { Play(new Tone(880, 110)); }
        public static void CuePause() { Play(new Tone(400, 150)); }
        public static void CueFinish() { Play(new Tone(700, 170, 40), new Tone(900, 170, 40), new Tone(1200, 520)); }

        public static void Play(params Tone[] tones)
        {
            if (!Enabled || tones == null || tones.Length == 0) return;

            Tone[] copy = (Tone[])tones.Clone();
            double volume = Volume;

            Task.Run(() =>
            {
                try
                {
                    // Serialised so cues queue up rather than talking over each other.
                    lock (PlayLock)
                    {
                        foreach (Tone tone in copy)
                        {
                            byte[] wav = GetWav(tone, volume);
                            using (MemoryStream stream = new MemoryStream(wav))
                            using (SoundPlayer player = new SoundPlayer(stream))
                            {
                                player.PlaySync();
                            }
                        }
                    }
                }
                catch
                {
                    // No audio device, or audio in use - the visual clock still works.
                }
            });
        }

        private static byte[] GetWav(Tone tone, double volume)
        {
            string key = tone.Frequency + "|" + tone.Milliseconds + "|" + tone.GapMilliseconds + "|" + volume.ToString("0.00");
            lock (Cache)
            {
                byte[] cached;
                if (Cache.TryGetValue(key, out cached)) return cached;

                byte[] built = BuildWav(tone, volume);
                if (Cache.Count < 64) Cache[key] = built;
                return built;
            }
        }

        private static byte[] BuildWav(Tone tone, double volume)
        {
            int toneSamples = Math.Max(1, SampleRate * Math.Max(1, tone.Milliseconds) / 1000);
            int gapSamples = SampleRate * Math.Max(0, tone.GapMilliseconds) / 1000;
            int totalSamples = toneSamples + gapSamples;
            int dataBytes = totalSamples * 2;

            byte[] wav = new byte[44 + dataBytes];

            WriteAscii(wav, 0, "RIFF");
            WriteInt32(wav, 4, 36 + dataBytes);
            WriteAscii(wav, 8, "WAVE");
            WriteAscii(wav, 12, "fmt ");
            WriteInt32(wav, 16, 16);                 // fmt chunk size
            WriteInt16(wav, 20, 1);                  // PCM
            WriteInt16(wav, 22, 1);                  // mono
            WriteInt32(wav, 24, SampleRate);
            WriteInt32(wav, 28, SampleRate * 2);     // byte rate
            WriteInt16(wav, 32, 2);                  // block align
            WriteInt16(wav, 34, 16);                 // bits per sample
            WriteAscii(wav, 36, "data");
            WriteInt32(wav, 40, dataBytes);

            // 4 ms fade in/out stops the click you get from a hard-edged square start.
            int fade = Math.Max(1, SampleRate / 250);
            for (int i = 0; i < toneSamples; i++)
            {
                double envelope = 1.0;
                if (i < fade) envelope = (double)i / fade;
                else if (i > toneSamples - fade) envelope = (double)(toneSamples - i) / fade;

                double sample = Math.Sin(2.0 * Math.PI * tone.Frequency * i / SampleRate) * envelope * volume;
                short value = (short)(sample * 32000);

                int offset = 44 + (i * 2);
                wav[offset] = (byte)(value & 0xFF);
                wav[offset + 1] = (byte)((value >> 8) & 0xFF);
            }

            return wav;
        }

        private static void WriteAscii(byte[] buffer, int offset, string text)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(text);
            Array.Copy(bytes, 0, buffer, offset, bytes.Length);
        }

        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteInt16(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }
    }
}
