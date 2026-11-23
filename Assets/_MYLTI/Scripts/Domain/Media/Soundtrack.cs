using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Mylti.Domain
{
    /// <summary>16-bit PCM WAV read/write (mono mixdown on read).</summary>
    public static class Wav
    {
        public static float[] ReadMono(string path, out int sampleRate)
        {
            using (var br = new BinaryReader(File.OpenRead(path)))
            {
                if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "RIFF") throw new InvalidDataException("Not a RIFF file");
                br.ReadInt32();
                if (Encoding.ASCII.GetString(br.ReadBytes(4)) != "WAVE") throw new InvalidDataException("Not a WAVE file");
                int channels = 1, bits = 16; sampleRate = 44100; int format = 1;
                while (br.BaseStream.Position + 8 <= br.BaseStream.Length)
                {
                    string id = Encoding.ASCII.GetString(br.ReadBytes(4));
                    int size = br.ReadInt32();
                    if (id == "fmt ")
                    {
                        format = br.ReadInt16(); channels = br.ReadInt16(); sampleRate = br.ReadInt32();
                        br.ReadInt32(); br.ReadInt16(); bits = br.ReadInt16();
                        if (size > 16) br.ReadBytes(size - 16);
                    }
                    else if (id == "data")
                    {
                        if (size < 0 || br.BaseStream.Position + size > br.BaseStream.Length) size = (int)(br.BaseStream.Length - br.BaseStream.Position);
                        int bytesPer = bits / 8;
                        int frames = size / (bytesPer * channels);
                        var outp = new float[frames];
                        for (int i = 0; i < frames; i++)
                        {
                            float acc = 0;
                            for (int c = 0; c < channels; c++)
                            {
                                if (format == 3 && bits == 32) acc += br.ReadSingle();
                                else if (bits == 16) acc += br.ReadInt16() / 32768f;
                                else if (bits == 8) acc += (br.ReadByte() - 128) / 128f;
                                else if (bits == 32) acc += br.ReadInt32() / 2147483648f;
                                else throw new InvalidDataException("Unsupported WAV bit depth " + bits);
                            }
                            outp[i] = acc / channels;
                        }
                        return outp;
                    }
                    else br.ReadBytes(Math.Max(0, size + (size & 1)));
                }
                throw new InvalidDataException("WAV has no data chunk");
            }
        }

        public static void WriteMono16(string path, float[] samples, int sampleRate)
        {
            using (var bw = new BinaryWriter(File.Create(path)))
            {
                int dataLen = samples.Length * 2;
                bw.Write(Encoding.ASCII.GetBytes("RIFF")); bw.Write(36 + dataLen); bw.Write(Encoding.ASCII.GetBytes("WAVE"));
                bw.Write(Encoding.ASCII.GetBytes("fmt ")); bw.Write(16); bw.Write((short)1); bw.Write((short)1);
                bw.Write(sampleRate); bw.Write(sampleRate * 2); bw.Write((short)2); bw.Write((short)16);
                bw.Write(Encoding.ASCII.GetBytes("data")); bw.Write(dataLen);
                foreach (var s in samples) bw.Write((short)Math.Max(-32767, Math.Min(32767, (int)(s * 32767f))));
            }
        }

        public static double DurationSeconds(string path)
        {
            var s = ReadMono(path, out int rate);
            return (double)s.Length / rate;
        }
    }

    /// <summary>Speed samples of one vehicle during a live take (from recorded poses).</summary>
    public sealed class SpeedTrack
    {
        public string VehicleId;
        public float BasePitch = 1f;    // 0.55 truck … 0.95 sport
        public float TopSpeedMs = 50f;
        public readonly List<double> Times = new List<double>();
        public readonly List<float> Speeds = new List<float>();
    }

    /// <summary>
    /// Builds the final audio track offline on the PROGRAM timeline, so it is frame-exact with the
    /// fixed-frame-rate video: narration clips + synthesised engine/wind/impact sound design + a quiet bed.
    /// All sound is generated here or comes from the TTS voice — no third-party music or samples.
    /// </summary>
    public sealed class Soundtrack
    {
        public readonly int Rate;
        readonly float[] voice;
        readonly float[] fx;
        readonly Random rng = new Random(1234);

        public Soundtrack(double seconds, int rate = 44100)
        {
            Rate = rate;
            int n = (int)Math.Ceiling(seconds * rate) + rate;
            voice = new float[n];
            fx = new float[n];
        }

        public double Length => (voice.Length - Rate) / (double)Rate;

        public void AddVoice(float[] clip, double at, float gain = 1f, double maxSeconds = double.MaxValue)
        {
            int start = (int)(at * Rate);
            int len = (int)Math.Min(clip.Length, maxSeconds * Rate);
            int fade = Math.Min(len, Rate / 20);
            for (int i = 0; i < len && start + i < voice.Length; i++)
            {
                if (start + i < 0) continue;
                float g = gain;
                if (len < clip.Length && i > len - fade) g *= (len - i) / (float)fade; // fade if trimmed
                voice[start + i] += clip[i] * g;
            }
        }

        public void AddEngine(SpeedTrack t, float gain = 0.12f)
        {
            if (t.Times.Count < 2) return;
            double phase = 0, phase2 = 0;
            int s0 = Math.Max(0, (int)(t.Times[0] * Rate));
            int s1 = Math.Min(fx.Length - 1, (int)(t.Times[t.Times.Count - 1] * Rate));
            int k = 0;
            for (int s = s0; s <= s1; s++)
            {
                double time = (double)s / Rate;
                while (k < t.Times.Count - 2 && t.Times[k + 1] < time) k++;
                double a = t.Times[k], b = t.Times[k + 1];
                float u = (float)Math.Max(0, Math.Min(1, (time - a) / Math.Max(1e-6, b - a)));
                float speed = t.Speeds[k] + (t.Speeds[k + 1] - t.Speeds[k]) * u;
                float s01 = Math.Max(0, Math.Min(1, speed / t.TopSpeedMs));
                double f = 55.0 * t.BasePitch * (0.7 + 1.6 * s01);
                phase += 2 * Math.PI * f / Rate;
                phase2 += 2 * Math.PI * f * 0.5 / Rate;
                double saw = 0;
                for (int h = 1; h <= 5; h++) saw += Math.Sin(phase * h) / h;
                float env = 1f;
                int edge = Rate / 4;
                if (s - s0 < edge) env = (s - s0) / (float)edge;
                if (s1 - s < edge) env = Math.Min(env, (s1 - s) / (float)edge);
                fx[s] += (float)(saw * 0.6 + Math.Sin(phase2) * 0.4) * gain * (0.45f + 0.55f * s01) * env;
            }
        }

        /// <summary>Filtered-noise whoosh (airborne, replays, flyovers).</summary>
        public void AddWind(double from, double to, float gain = 0.08f)
        {
            int s0 = Math.Max(0, (int)(from * Rate)), s1 = Math.Min(fx.Length - 1, (int)(to * Rate));
            float lp = 0;
            int len = Math.Max(1, s1 - s0);
            for (int s = s0; s <= s1; s++)
            {
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.04f;
                float x = (s - s0) / (float)len;
                float env = (float)Math.Sin(Math.PI * Math.Min(1, Math.Max(0, x)));
                fx[s] += lp * 3f * gain * env;
            }
        }

        public void AddImpact(double at, float strength01)
        {
            int s0 = (int)(at * Rate);
            int len = Rate / 2;
            float lp = 0;
            float g = 0.15f + 0.55f * Math.Max(0, Math.Min(1, strength01));
            for (int i = 0; i < len && s0 + i < fx.Length; i++)
            {
                if (s0 + i < 0) continue;
                float t = (float)i / Rate;
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.3f;
                float thump = (float)Math.Sin(2 * Math.PI * 55 * t) * (float)Math.Exp(-t * 10);
                fx[s0 + i] += (lp * (float)Math.Exp(-t * 9) * 0.7f + thump) * g;
            }
        }

        /// <summary>Very quiet outdoor bed across the whole program so there is never digital silence.</summary>
        public void AddBed(float gain = 0.012f)
        {
            float lp = 0;
            for (int s = 0; s < fx.Length; s++)
            {
                lp += (((float)rng.NextDouble() * 2f - 1f) - lp) * 0.01f;
                fx[s] += lp * 4f * gain;
            }
        }

        /// <summary>Duck effects under the voice, limit peaks, write a 16-bit mono WAV of exactly 'seconds'.</summary>
        public void Render(string path, double seconds)
        {
            int n = Math.Min(voice.Length, (int)Math.Round(seconds * Rate));
            var outp = new float[n];
            float env = 0;
            float attack = 1f / (Rate * 0.03f), release = 1f / (Rate * 0.35f);
            for (int i = 0; i < n; i++)
            {
                float v = Math.Abs(voice[i]);
                float target = v > 0.02f ? 1f : 0f;
                env += (target - env) * (target > env ? attack * 30 : release * 3);
                env = Math.Max(0, Math.Min(1, env));
                outp[i] = voice[i] * 0.95f + fx[i] * (1f - 0.65f * env);
            }
            float peak = 0;
            foreach (var x in outp) peak = Math.Max(peak, Math.Abs(x));
            float norm = peak > 0.95f ? 0.95f / peak : 1f;
            for (int i = 0; i < n; i++) outp[i] = (float)Math.Tanh(outp[i] * norm * 1.05f);
            Wav.WriteMono16(path, outp, Rate);
        }

        public bool VoiceActiveAround(double t, double radius)
        {
            int a = Math.Max(0, (int)((t - radius) * Rate)), b = Math.Min(voice.Length - 1, (int)((t + radius) * Rate));
            for (int i = a; i <= b; i += 64) if (Math.Abs(voice[i]) > 0.02f) return true;
            return false;
        }
    }
}
