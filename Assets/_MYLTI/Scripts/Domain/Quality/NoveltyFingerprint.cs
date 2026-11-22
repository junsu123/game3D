using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Mylti.Domain
{
    /// <summary>
    /// Episode fingerprint (spec §26). Jaccard similarity over weighted feature tokens.
    /// Warns when an episode is too close to a previous one; never blocks automatically.
    /// </summary>
    public sealed class NoveltyFingerprint : IJsonWritable
    {
        public string EpisodeId;
        public string CreatedAtIso;
        public string SeriesId;
        public HashSet<string> Features = new HashSet<string>();

        public static NoveltyFingerprint FromConcept(ConceptBrief c, IList<Entrant> entrants, IList<RoundSpec> rounds)
        {
            var fp = new NoveltyFingerprint { EpisodeId = c.ConceptId, SeriesId = c.SeriesId };
            AddCommon(fp, c, entrants, rounds);
            return fp;
        }

        public static NoveltyFingerprint FromRun(RunRecord run, ContentFactSheet fs)
        {
            var plan = run.Plan;
            var fp = new NoveltyFingerprint
            {
                EpisodeId = run.RunId,
                SeriesId = plan?.Concept?.SeriesId,
                CreatedAtIso = run.StartedAtIso
            };
            AddCommon(fp, plan?.Concept, plan?.Entrants ?? new List<Entrant>(), plan?.ScoredRounds.ToList() ?? new List<RoundSpec>());
            fp.Features.Add("outcome:winner=" + (fs?.OverallWinnerId ?? "none"));
            fp.Features.Add("outcome:rollovers=" + Bucket(fs?.RolloverCount ?? 0, 2));
            if (fs?.ComebackVehicleId != null) fp.Features.Add("outcome:comeback");
            return fp;
        }

        static void AddCommon(NoveltyFingerprint fp, ConceptBrief c, IList<Entrant> entrants, IList<RoundSpec> rounds)
        {
            fp.Features.Add("series:" + (c?.SeriesId ?? "?"));
            fp.Features.Add("twist:" + (c?.TwistKind ?? "?"));
            fp.Features.Add("map:MegaRamp");
            foreach (var e in entrants.OrderBy(x => x.VehicleId)) fp.Features.Add("vehicle:" + e.VehicleId);
            fp.Features.Add("lineup:" + string.Join("+", entrants.Select(x => x.VehicleId).OrderBy(x => x)));
            foreach (var r in rounds)
            {
                fp.Features.Add($"r{r.Index}:rule={r.Rule}");
                fp.Features.Add($"r{r.Index}:obst={r.Obstacles}");
                fp.Features.Add($"r{r.Index}:h={Bucket(r.KickerHeight, 4)}");
                if (r.BridgeWidth > 0) fp.Features.Add($"r{r.Index}:bridge");
                if (r.LandingRamp) fp.Features.Add($"r{r.Index}:landingramp");
                if (r.HeadToHead) fp.Features.Add($"r{r.Index}:h2h");
            }
            // Colours are intentionally NOT features: recolouring is not a meaningful change (§17.4, §29.7).
        }

        static string Bucket(double v, double size) => ((int)Math.Floor(v / size) * size).ToString("0");

        public double Similarity(NoveltyFingerprint other)
        {
            if (other == null || other.Features.Count == 0 || Features.Count == 0) return 0;
            int inter = Features.Count(f => other.Features.Contains(f));
            int union = Features.Count + other.Features.Count - inter;
            return union == 0 ? 0 : (double)inter / union;
        }

        public List<string> Differences(NoveltyFingerprint other)
        {
            var mine = Features.Where(f => !other.Features.Contains(f)).Take(8).Select(f => "+" + f);
            var theirs = other.Features.Where(f => !Features.Contains(f)).Take(8).Select(f => "-" + f);
            return mine.Concat(theirs).ToList();
        }

        public void WriteJson(JsonWriter w)
        {
            w.BeginObject().Prop("episodeId", EpisodeId).Prop("createdAt", CreatedAtIso).Prop("seriesId", SeriesId)
                .PropStrings("features", Features.OrderBy(x => x)).EndObject();
        }

        public static NoveltyFingerprint FromJson(Dictionary<string, object> o)
        {
            var fp = new NoveltyFingerprint
            {
                EpisodeId = MiniJson.GetString(o, "episodeId"),
                CreatedAtIso = MiniJson.GetString(o, "createdAt"),
                SeriesId = MiniJson.GetString(o, "seriesId")
            };
            var arr = MiniJson.Arr(MiniJson.Get(o, "features"));
            if (arr != null) foreach (var f in arr) if (f is string s) fp.Features.Add(s);
            return fp;
        }
    }

    /// <summary>Episode history stored as JSON in the user data folder.</summary>
    public sealed class NoveltyHistory
    {
        public readonly string FilePath;
        public readonly List<NoveltyFingerprint> Items = new List<NoveltyFingerprint>();
        public string LoadError;

        public NoveltyHistory(string filePath)
        {
            FilePath = filePath;
            if (!File.Exists(filePath)) return;
            try
            {
                var arr = MiniJson.Arr(MiniJson.Parse(File.ReadAllText(filePath, Encoding.UTF8)));
                if (arr != null) foreach (var o in arr) { var d = MiniJson.Obj(o); if (d != null) Items.Add(NoveltyFingerprint.FromJson(d)); }
            }
            catch (Exception e)
            {
                LoadError = "Episode history unreadable (" + e.Message + ") — starting fresh; old file kept as .bak";
                try { File.Copy(filePath, filePath + ".bak", true); } catch (IOException) { }
            }
        }

        public void AddAndSave(NoveltyFingerprint fp)
        {
            Items.RemoveAll(x => x.EpisodeId == fp.EpisodeId);
            Items.Add(fp);
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            File.WriteAllText(FilePath, MiniJson.Serialize(Items), new UTF8Encoding(false));
        }
    }
}
