using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Mylti.Domain
{
    public sealed class ClaimIssue
    {
        public string Item;
        public CheckLevel Level;
        public string Message;
        public override string ToString() => $"[{Level}] {Item}: {Message}";
    }

    /// <summary>
    /// "Don't make things up" validator (spec §18.3, §27): every number must come from the fact sheet,
    /// crash/winner/survival words need supporting telemetry, real brands and misleading
    /// realism/science claims are rejected. Questions ("Who wins?") are allowed.
    /// </summary>
    public static class ClaimChecker
    {
        public static readonly string[] Trademarks =
        {
            "ferrari", "lamborghini", "porsche", "bugatti", "mclaren", "tesla", "ford", "toyota", "honda", "bmw",
            "mercedes", "audi", "nissan", "chevrolet", "chevy", "dodge", "jeep", "volvo", "scania", "kenworth",
            "peterbilt", "volkswagen", "hyundai", "kia", "mazda", "subaru", "lexus", "koenigsegg", "pagani",
            "mitsubishi", "land rover", "range rover", "jaguar", "fiat", "renault", "peugeot", "skoda", "cadillac", "gmc",
            "buick", "rivian", "lucid", "aston martin", "bentley", "rolls-royce", "maserati", "alfa romeo", "suzuki", "isuzu",
            "freightliner", "iveco", "byd", "polestar", "opel", "vauxhall", "citroen", "dacia", "cupra", "infiniti", "acura", "hummer",
            "beamng", "gta", "forza", "hot wheels", "monster jam", "marvel", "disney", "pixar", "batman", "spider-man",
            "lightning mcqueen", "transformers", "optimus"
        };

        public const string CreditPrefixEn = "3D models: ", CreditPrefixVi = "Mẫu 3D: ";

        /// <summary>First trademark in the (lower-case) text that is not an allowed brand of a vehicle in the catalogue, or null.</summary>
        public static string FindBlockedBrand(string lower)
        {
            foreach (var tm in Trademarks)
                if (ContainsWord(lower, tm) && !VehicleCatalog.IsAllowedBrand(tm)) return tm;
            return null;
        }

        static readonly string[] BannedClaims =
        {
            "realistic damage", "realistic car damage", "real-world physics", "real physics", "realistic crash",
            "scientific proof", "scientifically", "proven", "100% real", "real crash test", "crash test proves",
            "thiệt hại thực tế", "vật lý thực tế 100%", "chứng minh khoa học"
        };

        static readonly string[] CrashWords = { "crash", "crashes", "crashed", "crashing", "wreck", "wrecked", "destroyed", "tai nạn", "nát" };
        static readonly string[] RollWords = { "rollover", "rolled over", "rolls over", "flipped", "flips", "lật" };
        static readonly string[] WinWords = { "wins", "won", "winner", "beats", "beat", "champion", "victory", "dominates", "chiến thắng", "thắng", "vô địch" };
        static readonly string[] SurviveWords = { "survived", "survives", "made it", "landed it", "sống sót", "tiếp đất an toàn" };
        static readonly string[] FallWords = { "fell off", "falls off", "rơi khỏi" };
        static readonly string[] SmashWords = { "smash", "smashes", "smashed", "through the wall", "phá tường" };

        /// <summary>Validate one piece of text. isTitle=true is stricter about overall-winner claims.</summary>
        public static List<ClaimIssue> Check(string item, string text, ContentFactSheet fs, bool isTitle)
        {
            var issues = new List<ClaimIssue>();
            if (string.IsNullOrWhiteSpace(text)) { issues.Add(Issue(item, CheckLevel.Fail, "empty text")); return issues; }
            // model credit lines ("3D models: …") are attributions, not claims
            text = string.Join("\n", text.Split('\n').Where(l => !l.StartsWith(CreditPrefixEn, StringComparison.Ordinal) && !l.StartsWith(CreditPrefixVi, StringComparison.Ordinal)));
            string lower = text.ToLowerInvariant();

            foreach (var tm in Trademarks)
                if (ContainsWord(lower, tm) && !VehicleCatalog.IsAllowedBrand(tm)) issues.Add(Issue(item, CheckLevel.Fail, $"trademark/IP '{tm}' is not one of the vehicles in this episode"));
            foreach (var b in BannedClaims)
                if (lower.Contains(b)) issues.Add(Issue(item, CheckLevel.Fail, $"misleading realism/science claim '{b}'"));

            foreach (var sentence in SplitSentences(text))
            {
                string s = sentence.ToLowerInvariant();
                bool question = sentence.TrimEnd().EndsWith("?");
                if (!question)
                {
                    if (AnyWord(s, CrashWords) && fs.RolloverCount == 0 && fs.BodySlamCount == 0 && fs.FellOffCount == 0 && fs.WallCrashCount == 0)
                        issues.Add(Issue(item, CheckLevel.Fail, "mentions a crash but no rollover/body slam/fall was recorded"));
                    if (AnyWord(s, RollWords) && fs.RolloverCount == 0)
                        issues.Add(Issue(item, CheckLevel.Fail, "mentions a rollover but none was recorded"));
                    if (AnyWord(s, FallWords) && fs.FellOffCount == 0)
                        issues.Add(Issue(item, CheckLevel.Fail, "mentions falling off but no FellOff event was recorded"));
                    if (AnyWord(s, SmashWords) && fs.ObstacleHitCount == 0)
                        issues.Add(Issue(item, CheckLevel.Fail, "mentions smashing obstacles but no obstacle hit was recorded"));
                    if (AnyWord(s, SurviveWords) && fs.ValidLandingTotal == 0)
                        issues.Add(Issue(item, CheckLevel.Fail, "claims a successful landing but no valid landing was recorded"));
                    if (AnyWord(s, WinWords))
                    {
                        if (isTitle && fs.OverallWinnerId == null)
                            issues.Add(Issue(item, CheckLevel.Fail, "claims a winner but the run ended without an overall winner"));
                        else if (!isTitle && !fs.AnyRoundWinner && fs.OverallWinnerId == null)
                            issues.Add(Issue(item, CheckLevel.Fail, "claims a win but no round had a winner"));
                        else
                            CheckNamedWinner(item, sentence, fs, isTitle, issues);
                    }
                }
            }

            foreach (var num in ExtractNumbers(text))
            {
                if (!fs.AllowedNumbers.Contains(num))
                    issues.Add(Issue(item, CheckLevel.Fail, $"number '{num}' is not backed by the fact sheet"));
            }
            return issues;
        }

        /// <summary>If a sentence says "&lt;Vehicle&gt; wins" make sure that vehicle actually won something.</summary>
        static void CheckNamedWinner(string item, string sentence, ContentFactSheet fs, bool isTitle, List<ClaimIssue> issues)
        {
            string s = sentence.ToLowerInvariant();
            foreach (var v in fs.Vehicles)
            {
                string name = v.Name.ToLowerInvariant();
                int idx = s.IndexOf(name, StringComparison.Ordinal);
                if (idx < 0) continue;
                string after = s.Substring(idx + name.Length);
                bool directWin = Regex.IsMatch(after, @"^\s*(wins|won|beats|dominates|thắng|chiến thắng)");
                if (!directWin) continue;
                bool ok = isTitle ? fs.OverallWinnerId == v.VehicleId : v.RoundWins > 0;
                if (!ok) issues.Add(Issue(item, CheckLevel.Fail, $"says {v.Name} wins, but telemetry does not support it"));
            }
        }

        public static IEnumerable<string> ExtractNumbers(string text)
        {
            // Ignore timestamps (01:23), hashtags, "3D", "16:9", and ordinal-free resolution tokens.
            string cleaned = Regex.Replace(text ?? "", @"\b\d{3,4}\s*[x×]\s*\d{3,4}\b", " ");
            cleaned = Regex.Replace(cleaned, @"\b\d{1,2}:\d{1,2}(:\d{2})?\b", " ");
            cleaned = Regex.Replace(cleaned, @"#\S+", " ");
            cleaned = Regex.Replace(cleaned, @"\bv\d+(\.\d+)*\b", " ", RegexOptions.IgnoreCase); // version tags like v1
            cleaned = Regex.Replace(cleaned, @"\b3D\b", " ", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\b(1920|1080|1280|720)\b", " ");
            foreach (Match m in Regex.Matches(cleaned, @"(?<![\w.])\d+(\.\d+)?"))
            {
                string v = m.Value;
                if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                {
                    yield return v.Contains(".") ? d.ToString("0.0", CultureInfo.InvariantCulture) : d.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        static IEnumerable<string> SplitSentences(string text)
        {
            foreach (var line in text.Split('\n'))
            {
                foreach (var part in Regex.Split(line, @"(?<=[.!?…])\s+|\s[—–|]\s"))
                    if (!string.IsNullOrWhiteSpace(part)) yield return part;
            }
        }

        static bool AnyWord(string lower, IEnumerable<string> words) => words.Any(w => ContainsWord(lower, w));

        public static bool ContainsWord(string lower, string word)
        {
            int i = 0;
            while ((i = lower.IndexOf(word, i, StringComparison.Ordinal)) >= 0)
            {
                bool startOk = i == 0 || !char.IsLetterOrDigit(lower[i - 1]);
                int end = i + word.Length;
                bool endOk = end >= lower.Length || !char.IsLetterOrDigit(lower[end]);
                if (startOk && endOk) return true;
                i = end;
            }
            return false;
        }

        static ClaimIssue Issue(string item, CheckLevel level, string msg) => new ClaimIssue { Item = item, Level = level, Message = msg };

        public static string Status(IEnumerable<ClaimIssue> issues)
        {
            var fails = issues.Where(i => i.Level == CheckLevel.Fail).ToList();
            return fails.Count == 0 ? "Pass" : "Fail: " + string.Join("; ", fails.Select(f => f.Message));
        }
    }
}
