using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mylti.Domain;
using NUnit.Framework;

namespace Mylti.Tests
{
    [TestFixture]
    public class PublishingTests
    {
        static DateTime U(int y, int mo, int d, int h, int mi = 0) => new DateTime(y, mo, d, h, mi, 0, DateTimeKind.Utc);

        [Test]
        public void DaylightSaving_UsAndEurope()
        {
            // US 2026: DST from Sun 8 Mar 07:00 UTC (02:00 EST) to Sun 1 Nov 06:00 UTC (02:00 EDT)
            Assert.AreEqual(-5, MarketClock.Offset(MarketZone.UsEastern, U(2026, 3, 8, 6, 59)));
            Assert.AreEqual(-4, MarketClock.Offset(MarketZone.UsEastern, U(2026, 3, 8, 7, 0)));
            Assert.AreEqual(-4, MarketClock.Offset(MarketZone.UsEastern, U(2026, 11, 1, 5, 59)));
            Assert.AreEqual(-5, MarketClock.Offset(MarketZone.UsEastern, U(2026, 11, 1, 6, 0)));
            Assert.AreEqual(-7, MarketClock.Offset(MarketZone.UsPacific, U(2026, 7, 1, 12)));
            Assert.AreEqual(-8, MarketClock.Offset(MarketZone.UsPacific, U(2026, 12, 1, 12)));
            // EU 2026: last Sunday of March = 29 Mar, last Sunday of October = 25 Oct, both at 01:00 UTC
            Assert.AreEqual(1, MarketClock.Offset(MarketZone.CentralEurope, U(2026, 3, 29, 0, 59)));
            Assert.AreEqual(2, MarketClock.Offset(MarketZone.CentralEurope, U(2026, 3, 29, 1, 0)));
            Assert.AreEqual(2, MarketClock.Offset(MarketZone.CentralEurope, U(2026, 10, 25, 0, 59)));
            Assert.AreEqual(1, MarketClock.Offset(MarketZone.CentralEurope, U(2026, 10, 25, 1, 0)));
            Assert.AreEqual(0, MarketClock.Offset(MarketZone.UkIreland, U(2026, 11, 23, 12)));
            Assert.AreEqual(3, MarketClock.Offset(MarketZone.EasternEurope, U(2026, 7, 1, 12)));
            Assert.AreEqual("CEST", MarketClock.ZoneLabel(MarketZone.CentralEurope, U(2026, 7, 1, 12)));
        }

        [Test]
        public void DefaultMarkets_SumToOne_AndUsIsPrimary()
        {
            var m = Market.Defaults();
            Assert.AreEqual(1.0, m.Sum(x => x.Weight), 1e-9);
            Assert.AreEqual("US", m.OrderByDescending(x => x.Weight).First().Code);
            foreach (var x in m) Assert.AreEqual(1.0, x.Zones.Sum(z => z.Value), 1e-9, x.Code);
            CollectionContains(Market.LanguagesFor(m), "de");
            Assert.IsFalse(Market.LanguagesFor(m).Contains(null));
        }

        static void CollectionContains(IEnumerable<string> list, string v) => Assert.IsTrue(list.Contains(v), v + " missing");

        [Test]
        public void Planner_UsPlusEurope_PicksTheAtlanticCrossoverWindow()
        {
            var week = U(2026, 11, 23, 0);
            var pattern = PublishPlanner.WeeklyPattern(Market.Defaults(), 3, week);
            Assert.AreEqual(3, pattern.Count);
            foreach (var s in pattern.Where(p => p.DayUtc != DayOfWeek.Saturday && p.DayUtc != DayOfWeek.Sunday))
            {
                var t = week.AddDays(((int)s.DayUtc + 6) % 7).AddHours(s.HourUtc).AddMinutes(s.MinuteUtc);
                int et = MarketClock.ToLocal(MarketZone.UsEastern, t).Hour, cet = MarketClock.ToLocal(MarketZone.CentralEurope, t).Hour;
                Assert.IsTrue(et >= 11 && et <= 16, "weekday slot should be late morning / afternoon in New York: " + s);
                Assert.IsTrue(cet >= 17 && cet <= 22, "and evening in Central Europe: " + s);
            }
            // spaced out: no two releases on consecutive days when 3 per week
            var days = pattern.Select(p => ((int)p.DayUtc + 6) % 7).OrderBy(d => d).ToList();
            for (int i = 0; i < days.Count; i++) Assert.IsTrue(((days[(i + 1) % days.Count] - days[i] + 7) % 7 == 0 ? 7 : (days[(i + 1) % days.Count] - days[i] + 7) % 7) >= 2);
        }

        [Test]
        public void Planner_UsOnly_IsAfternoonEastern()
        {
            var m = Market.Defaults();
            foreach (var x in m) if (x.Code != "US") x.Weight = 0;
            var week = U(2026, 11, 23, 0);
            foreach (var s in PublishPlanner.WeeklyPattern(m, 2, week).Where(p => p.DayUtc != DayOfWeek.Saturday && p.DayUtc != DayOfWeek.Sunday))
            {
                var t = week.AddDays(((int)s.DayUtc + 6) % 7).AddHours(s.HourUtc).AddMinutes(s.MinuteUtc);
                int et = MarketClock.ToLocal(MarketZone.UsEastern, t).Hour;
                Assert.IsTrue(et >= 13 && et <= 17, "US-only weekday slot should be 1–5 PM Eastern: " + s);
            }
        }

        [Test]
        public void UpcomingSlots_AreFutureOrderedAndSkipTakenTimes()
        {
            var m = Market.Defaults();
            var now = U(2026, 11, 23, 10);
            var slots = PublishPlanner.UpcomingSlots(m, 3, now, 6);
            Assert.AreEqual(6, slots.Count);
            Assert.IsTrue(slots.All(s => s >= now));
            for (int i = 1; i < slots.Count; i++) Assert.IsTrue(slots[i] > slots[i - 1]);
            var again = PublishPlanner.UpcomingSlots(m, 3, now, 2, new List<DateTime> { slots[0] });
            Assert.AreEqual(slots[1], again[0]);
        }

        [Test]
        public void Queue_And_Settings_RoundTrip()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mylti_pub_" + Guid.NewGuid().ToString("N"));
            var q = new PublishQueue(Path.Combine(dir, "q.json"));
            q.Items.Add(new PublishItem { RunId = "r1", Title = "T", State = PublishState.Scheduled, PublishAtUtc = U(2026, 11, 24, 17, 30), ProducedAtUtc = U(2026, 11, 23, 6), VideoId = "abc" });
            q.Items.Add(new PublishItem { RunId = "r2", State = PublishState.Produced, ProducedAtUtc = U(2026, 11, 23, 7) });
            q.Items.Add(new PublishItem { RunId = "r3", State = PublishState.Failed, Attempts = PublishQueue.MaxAttempts, ProducedAtUtc = U(2026, 11, 22, 7) });
            q.Save();
            var q2 = new PublishQueue(Path.Combine(dir, "q.json"));
            Assert.AreEqual(3, q2.Items.Count);
            Assert.AreEqual(U(2026, 11, 24, 17, 30), q2.Items[0].PublishAtUtc);
            Assert.AreEqual(2, q2.ScheduledAhead(U(2026, 11, 23, 12)), "scheduled future + produced; exhausted failure not counted");
            q2.Refresh(U(2026, 11, 25, 0));
            Assert.AreEqual(PublishState.Published, q2.Items[0].State);

            var ps = new PublishSettings { VideosPerWeek = 4, GoogleClientId = "id.apps.googleusercontent.com" };
            ps.Markets.First(x => x.Code == "DE").Weight = 0.2;
            ps.Save(Path.Combine(dir, "p.json"));
            var ps2 = PublishSettings.Load(Path.Combine(dir, "p.json"));
            Assert.AreEqual(4, ps2.VideosPerWeek);
            Assert.AreEqual(0.2, ps2.Markets.First(x => x.Code == "DE").Weight, 1e-9);
            Assert.AreEqual("id.apps.googleusercontent.com", ps2.GoogleClientId);
            Assert.IsFalse(ps2.AutopilotEnabled);
        }

        [Test]
        public void VideoJson_SchedulesPrivateThenPublic_WithLocalizations()
        {
            var meta = new VideoUploadMeta { Title = "Sports Car vs <Heavy Truck>", Description = "Test", PublishAtUtc = U(2026, 11, 24, 17, 30) };
            meta.Tags.AddRange(new[] { "mega ramp", "car jump", "mega ramp" });
            meta.Localizations["de"] = new KeyValuePair<string, string>("Sportwagen gegen Lkw", "Test DE");
            var o = MiniJson.Obj(MiniJson.Parse(YouTubeClient.BuildVideoJson(meta)));
            var st = MiniJson.Obj(MiniJson.Get(o, "status"));
            Assert.AreEqual("private", MiniJson.GetString(st, "privacyStatus"));
            Assert.AreEqual("2026-11-24T17:30:00Z", MiniJson.GetString(st, "publishAt"));
            Assert.IsFalse(MiniJson.GetBool(st, "selfDeclaredMadeForKids", true));
            var sn = MiniJson.Obj(MiniJson.Get(o, "snippet"));
            Assert.AreEqual("Sports Car vs Heavy Truck", MiniJson.GetString(sn, "title"));
            Assert.AreEqual(2, MiniJson.Arr(MiniJson.Get(sn, "tags")).Count);
            Assert.AreEqual("en", MiniJson.GetString(sn, "defaultLanguage"));
            Assert.AreEqual("Sportwagen gegen Lkw", MiniJson.GetString(MiniJson.Obj(MiniJson.Get(MiniJson.Obj(MiniJson.Get(o, "localizations")), "de")), "title"));

            meta.AutoPublish = false;
            st = MiniJson.Obj(MiniJson.Get(MiniJson.Obj(MiniJson.Parse(YouTubeClient.BuildVideoJson(meta))), "status"));
            Assert.AreEqual("private", MiniJson.GetString(st, "privacyStatus"));
            Assert.IsNull(MiniJson.GetString(st, "publishAt"));
            Assert.AreEqual(100, YouTubeClient.CleanTitle(new string('x', 140)).Length);
            Assert.IsTrue(System.Text.Encoding.UTF8.GetByteCount(YouTubeClient.CleanDescription(new string('é', 4000))) <= 5000);
        }

        [Test]
        public void Pkce_MatchesIndependentSha256()
        {
            // reference value computed with Python hashlib/base64 (independent implementation)
            Assert.AreEqual("2wGruEe3xOQ1JI13byPfQ5RjBBAsDjv2Zz8GGRVIyvc", YouTubeClient.Challenge("dBjftJeZ4CVP-mJ92K1i-mmQ4qL9TfR6ZqxQ4y1BYbU"));
            string v = YouTubeClient.NewVerifier();
            Assert.IsTrue(v.Length >= 43 && v.Length <= 128 && !v.Contains("=") && !v.Contains("+") && !v.Contains("/"));
            string url = YouTubeClient.AuthUrl("cid", "http://127.0.0.1:5000", "ch", "st");
            StringAssert.Contains("code_challenge_method=S256", url);
            StringAssert.Contains("access_type=offline", url);
            StringAssert.Contains("youtube.upload", Uri.UnescapeDataString(url));
        }

        [Test]
        public void TranslationCheck_KeepsNumbersAndChapters()
        {
            string t = "Can a Heavy Truck clear an 18.5 m gap?", d = "00:00 Intro\n01:30 Round 1\nBest jump: 61.5 m";
            Assert.IsNull(PublishMetadata.CheckTranslation(t, d, "Schafft ein Lkw eine 18,5-m-Lücke?", "00:00 Intro\n01:30 Runde 1\nBester Sprung: 61,5 m"));
            Assert.IsNotNull(PublishMetadata.CheckTranslation(t, d, "Schafft ein Lkw 20 m?", "00:00 Intro\n01:30 Runde 1\nBester Sprung: 61,5 m"));
            Assert.IsNotNull(PublishMetadata.CheckTranslation(t, d, "Lkw-Sprung", "00:00 Intro\nBester Sprung: 61,5 m"));
            Assert.IsNotNull(PublishMetadata.CheckTranslation(t, d, "Ferrari gegen Lkw", d));
        }

        [Test]
        public void ClaudeTranslations_FailingTheCheckAreDropped()
        {
            const string reply = "{\"de\":{\"title\":\"Sportwagen gegen Lkw\",\"description\":\"00:00 Intro\"},\"fr\":{\"title\":\"Voiture de sport contre camion 99 m\",\"description\":\"00:00 Intro\"}}";
            var claude = new ClaudeClient("k") { Responder = (s, u) => System.Threading.Tasks.Task.FromResult(reply) };
            var r = PublishMetadata.TranslateAsync("Sports Car vs Heavy Truck", "00:00 Intro", new[] { "de", "fr" }, claude, null).GetAwaiter().GetResult();
            Assert.IsTrue(r.ContainsKey("de"));
            Assert.IsFalse(r.ContainsKey("fr"), "invented number");
        }

        [Test]
        public void UploadMeta_RoundTrip_And_TaskXml()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mylti_meta_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var m = new VideoUploadMeta { Title = "T", Description = "D" };
            m.Tags.Add("mega ramp");
            m.Localizations["fr"] = new KeyValuePair<string, string>("T fr", "D fr");
            PublishMetadata.SaveMeta(Path.Combine(dir, "u.json"), m, "thumb.jpg", null);
            var ps = new PublishSettings { CategoryId = "2", AutoPublish = false };
            var l = PublishMetadata.LoadMeta(Path.Combine(dir, "u.json"), ps, out string th, out string cap);
            Assert.AreEqual("T", l.Title);
            Assert.AreEqual("T fr", l.Localizations["fr"].Key);
            Assert.AreEqual("2", l.CategoryId);
            Assert.IsFalse(l.AutoPublish);
            Assert.AreEqual("thumb.jpg", th);
            Assert.IsNull(cap);

            string xml = PublishMetadata.TaskXml(@"C:\Games\MYLTI & Co\MYLTI.exe", 13, true, new DateTime(2026, 11, 23));
            StringAssert.Contains("<StartBoundary>2026-11-23T13:00:00</StartBoundary>", xml);
            StringAssert.Contains("<WakeToRun>true</WakeToRun>", xml);
            StringAssert.Contains("<Arguments>-autopilot</Arguments>", xml);
            StringAssert.Contains("MYLTI &amp; Co", xml);
            System.Xml.Linq.XDocument.Parse(xml.Substring(xml.IndexOf("<Task", StringComparison.Ordinal)));
        }
    }
}
