using System;
using System.Collections.Generic;
using System.Linq;

namespace Mylti.Domain
{
    /// <summary>
    /// How excited the crowd is, driven only by what really happens (takeoffs, landings, crashes, results).
    /// Level = cheering energy, Applause = clapping (after results / finishes). Used for the animated
    /// spectators live and, from the event log, for the crowd sound in the final video — so both match.
    /// </summary>
    public sealed class CrowdMood
    {
        public const float Base = 0.12f;
        public float Level { get; private set; } = Base;
        public float Applause { get; private set; }
        public float Gasp { get; private set; }   // short "ooh" right at a crash

        float target = Base, hold, applauseTarget, applauseHold, gaspHold;
        readonly float[] history = new float[64]; // 20 Hz, ~3 s
        int historyPos;
        float historyAcc;

        /// <summary>React to a run event (types as logged by the attempt tracker / director).</summary>
        public void OnEvent(string type, IDictionary<string, double> values)
        {
            double V(string k) => values != null && values.TryGetValue(k, out var v) ? v : 0;
            switch (type)
            {
                case "AttemptStart": Bump(0.35f, 1.5f); break;
                case "RampEntered": Bump(0.55f, 1.2f); break;
                case "Takeoff": Bump(0.8f, 1.5f); break;
                case "Drop": Bump(0.7f, 1.2f); break;
                case "Apex": Bump(0.9f, 1.0f); break;
                case "NearImpact": Bump(0.85f, 0.8f); break;
                case "Landed": Bump(V("impactSpeed") > 9 ? 1f : 0.85f, 2.2f); break;
                case "ObstacleHit": Bump(0.9f, 1.5f); break;
                case "Rollover":
                case "WallCrash":
                case "FellOff":
                    Bump(1f, 2.8f);
                    Gasp = 1f; gaspHold = 0.7f;
                    break;
                case "Damage":
                    if (V("wheelsLost") > 0 || V("damagePercent") > 45) Bump(1f, 2f);
                    break;
                case "RoundResult":
                case "PhaseStarted":
                    if (type == "RoundResult") { applauseTarget = 1f; applauseHold = 4.5f; Bump(0.75f, 3f); }
                    break;
                case "FinalStandings":
                    applauseTarget = 1f; applauseHold = 8f; Bump(1f, 6f);
                    break;
            }
        }

        void Bump(float to, float seconds)
        {
            if (to >= target) { target = to; hold = Math.Max(hold, seconds); }
            else hold = Math.Max(hold, seconds * 0.5f);
        }

        public void Tick(float dt)
        {
            if (dt <= 0) return;
            hold -= dt;
            if (hold <= 0) target += (Base - target) * (1f - (float)Math.Exp(-dt * 0.6f));
            float rate = target > Level ? 7f : 1.1f; // jump up fast, calm down slowly
            Level += (target - Level) * (1f - (float)Math.Exp(-dt * rate));

            applauseHold -= dt;
            if (applauseHold <= 0) applauseTarget = 0;
            Applause += (applauseTarget - Applause) * (1f - (float)Math.Exp(-dt * (applauseTarget > Applause ? 5f : 1.2f)));

            gaspHold -= dt;
            if (gaspHold <= 0) Gasp += (0 - Gasp) * (1f - (float)Math.Exp(-dt * 4f));

            historyAcc += dt;
            while (historyAcc >= 0.05f)
            {
                historyAcc -= 0.05f;
                history[historyPos] = Level;
                historyPos = (historyPos + 1) % history.Length;
            }
        }

        /// <summary>Level a little while ago — far spectators react later than near ones.</summary>
        public float LevelAgo(float seconds)
        {
            int back = Math.Max(0, Math.Min(history.Length - 1, (int)(seconds / 0.05f)));
            if (back == 0) return Level;
            return history[(historyPos - back + history.Length * 2) % history.Length];
        }

        /// <summary>Sample the crowd over a whole recorded program (for the soundtrack), at 'step' seconds.</summary>
        public static void Timeline(IEnumerable<RunEvent> events, double duration, double step, out float[] level, out float[] applause, out float[] gasp)
        {
            int n = Math.Max(1, (int)Math.Ceiling(duration / step));
            level = new float[n]; applause = new float[n]; gasp = new float[n];
            var mood = new CrowdMood();
            var ev = events.OrderBy(e => e.ProgramTime).ToList();
            int k = 0;
            for (int i = 0; i < n; i++)
            {
                double t = i * step;
                while (k < ev.Count && ev[k].ProgramTime <= t) { mood.OnEvent(ev[k].Type, ev[k].Values); k++; }
                mood.Tick((float)step);
                level[i] = mood.Level; applause[i] = mood.Applause; gasp[i] = mood.Gasp;
            }
        }
    }
}
