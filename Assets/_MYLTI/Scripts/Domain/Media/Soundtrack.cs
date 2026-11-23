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
    /// Sound design is generated here; music comes only from files the owner puts in the Music folder.
    /// </summary>
    public sealed class Soundtrack
    {
        public readonly int Rate;
        readonly float[] voice;
        readonly float[] fx;
        float[] music;
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

        /// <summary>Metal crunch for a crash: noise burst + decaying metallic partials, scaled 0..1.</summary>
        public void AddCrash(double at, float strength01, bool glass = false)
        {
            float k = Math.Max(0.15f, Math.Min(1f, strength01));
            int s0 = (int)(at * Rate);
            int len = (int)(Rate * (0.6 + 0.9 * k));
            double[] partials = { 87, 143, 211, 389, 577, 1013 };
            float lp = 0, hp = 0, prev = 0;
            for (int i = 0; i < len && s0 + i < fx.Length; i++)
            {
                if (s0 + i < 0) continue;
                float t = (float)i / Rate;
                float n = (float)rng.NextDouble() * 2f - 1f;
                lp += (n - lp) * 0.25f;
                float body = lp * (float)Math.Exp(-t * 7) * 1.1f;
                float ring = 0;
                for (int p = 0; p < partials.Length; p++)
                    ring += (float)Math.Sin(2 * Math.PI * partials[p] * (1 + 0.002 * p) * t) * (float)Math.Exp(-t * (4 + p * 2.5)) / (p + 1.5f);
                float thump = (float)Math.Sin(2 * Math.PI * 48 * t) * (float)Math.Exp(-t * 8);
                float sample = (body + ring * 0.9f + thump * 1.2f) * (0.25f + 0.65f * k);
                if (glass && t > 0.03f)
                {
                    hp = 0.92f * (hp + n - prev);
                    prev = n;
                    sample += hp * (float)Math.Exp(-(t - 0.03f) * 5) * 0.35f * (rng.NextDouble() < 0.02 ? 3f : 1f);
                }
                fx[s0 + i] += sample;
            }
        }

        /// <summary>Background music (owner-supplied file), looped over the program with fades; ducked under the voice.</summary>
        public void AddMusic(float[] track, float gain = 0.22f)
        {
            if (track == null || track.Length < Rate) return;
            music = new float[voice.Length];
            int fadeIn = Rate * 2, fadeOut = Rate * 4;
            int total = voice.Length - Rate;
            for (int i = 0; i < total; i++)
            {
                float g = gain;
                if (i < fadeIn) g *= i / (float)fadeIn;
                if (i > total - fadeOut) g *= Math.Max(0, (total - i) / (float)fadeOut);
                music[i] = track[i % track.Length] * g;
            }
        }

        /// <summary>
        /// Live crowd, driven by the same CrowdMood timeline as the animated spectators (so what you hear matches
        /// what you see): a murmur bed, a cheering roar ("ahh" formants on noise + a chorus of buzzing voices),
        /// dense applause (a Poisson train of clap transients) and a collective "ooh" on near-misses and crashes.
        /// Curves are sampled every <paramref name="step"/> seconds of program time, values 0..1.
        /// </summary>
        public void AddCrowd(float[] level, float[] applause, float[] gasp, double step, float gain = 0.7f)
        {
            if (level == null || level.Length == 0 || gain <= 0f || step <= 0) return;
            var r = new Random(4321);
            var ahF1 = new Biquad(Rate, 760, 1.4f); var ahF2 = new Biquad(Rate, 1220, 2.0f); var ahF3 = new Biquad(Rate, 2500, 3f);
            var ooF1 = new Biquad(Rate, 340, 2.2f); var ooF2 = new Biquad(Rate, 820, 2.8f);
            var murF = new Biquad(Rate, 480, 0.7f);
            var clapA = new Biquad(Rate, 1250, 1.1f); var clapB = new Biquad(Rate, 2300, 1.3f); var clapC = new Biquad(Rate, 3800, 1.6f);
            const int V = 12;
            var ph = new double[V]; var f0 = new double[V]; var vib = new double[V]; var amp = new float[V]; var ampT = new float[V];
            for (int v = 0; v < V; v++) { f0[v] = 105 + r.NextDouble() * 170; vib[v] = r.NextDouble() * 6; amp[v] = ampT[v] = (float)r.NextDouble(); ph[v] = r.NextDouble(); }
            float envA = 0, envB = 0, envC = 0, mur = 0.5f, murT = 0.5f, swell = 1f, swellT = 1f;
            float clapDecay = (float)Math.Exp(-1.0 / (Rate * 0.011));
            float perStep = (float)(Rate * step);
            int n = fx.Length - Rate;
            for (int i = 0; i < n; i++)
            {
                float u = i / perStep;
                int k = (int)u;
                float fr = u - k;
                float Curve(float[] c) => c == null || c.Length == 0 ? 0f : k + 1 < c.Length ? c[k] + (c[k + 1] - c[k]) * fr : c[c.Length - 1];
                float lv = Curve(level), ap = Math.Min(1f, Curve(applause)), gs = Math.Min(1f, Curve(gasp));
                float ex = Math.Max(0f, Math.Min(1f, (lv - CrowdMood.Base) / (1f - CrowdMood.Base)));
                float w = (float)r.NextDouble() * 2f - 1f;
                float w2 = (float)r.NextDouble() * 2f - 1f;

                // slow random modulation: murmur syllables (~5 Hz) and crowd swells (~0.7 Hz)
                if (i % (Rate / 5) == 0) murT = 0.25f + (float)r.NextDouble() * 0.75f;
                if (i % (Rate * 3 / 2) == 0) swellT = 0.75f + (float)r.NextDouble() * 0.35f;
                mur += (murT - mur) * 0.0006f;
                swell += (swellT - swell) * 0.00005f;

                // chorus of voices (pitch rises with excitement), each fading in and out on its own
                float buzz = 0;
                if (ex > 0.02f || gs > 0.02f)
                {
                    double rise = 1 + 0.35 * ex;
                    for (int v = 0; v < V; v++)
                    {
                        vib[v] += 5.5 / Rate;
                        ph[v] += f0[v] * rise * (1 + 0.025 * Math.Sin(2 * Math.PI * vib[v])) / Rate;
                        if (ph[v] >= 1) ph[v] -= 1;
                        if ((i + v * 977) % (Rate / 3) == 0) { ampT[v] = r.NextDouble() < 0.25 ? 0f : (float)r.NextDouble(); if (r.NextDouble() < 0.1) f0[v] = 105 + r.NextDouble() * 170; }
                        amp[v] += (ampT[v] - amp[v]) * 0.0004f;
                        buzz += (float)(ph[v] * 2 - 1) * amp[v];
                    }
                    buzz /= V;
                }
                float src = w * 0.75f + buzz * 0.9f;
                float roar = ahF1.Process(src) + 0.8f * ahF2.Process(src) + 0.35f * ahF3.Process(w2);
                float ooh = ooF1.Process(src) + 0.6f * ooF2.Process(src);
                float murmur = murF.Process(w) * mur;

                // applause: three bands, each an independent stream of clap transients
                float claps = 30f + 1700f * ap + 260f * ex * ex;
                float pr = claps / Rate / 3f;
                if (r.NextDouble() < pr) envA += 0.5f + (float)r.NextDouble();
                if (r.NextDouble() < pr) envB += 0.5f + (float)r.NextDouble();
                if (r.NextDouble() < pr) envC += 0.5f + (float)r.NextDouble();
                envA *= clapDecay; envB *= clapDecay; envC *= clapDecay;
                float norm = 1f / (float)Math.Sqrt(1 + claps / 3f * 0.011f);
                float clap = (clapA.Process(w) * envA + clapB.Process(w2) * envB + 0.6f * clapC.Process(w) * envC) * norm;
                float clapGain = Math.Min(1f, ap + 0.35f * ex * ex + 0.04f);

                float x = 0.10f * murmur
                        + 1.0f * (float)Math.Pow(ex, 1.3) * swell * roar
                        + 1.1f * gs * ooh
                        + 0.15f * clapGain * clap;
                fx[i] += x * gain;
            }
        }

        /// <summary>RBJ band-pass (constant 0 dB peak).</summary>
        sealed class Biquad
        {
            readonly float b0, b2, a1, a2;
            float x1, x2, y1, y2;
            public Biquad(int rate, double hz, double q)
            {
                double w0 = 2 * Math.PI * hz / rate, alpha = Math.Sin(w0) / (2 * q), a0 = 1 + alpha;
                b0 = (float)(alpha / a0); b2 = (float)(-alpha / a0); a1 = (float)(-2 * Math.Cos(w0) / a0); a2 = (float)((1 - alpha) / a0);
            }
            public float Process(float x)
            {
                float y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
                x2 = x1; x1 = x; y2 = y1; y1 = y;
                return y;
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
                outp[i] = voice[i] * 0.95f + fx[i] * (1f - 0.65f * env) + (music != null ? music[i] * (1f - 0.72f * env) : 0f);
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
