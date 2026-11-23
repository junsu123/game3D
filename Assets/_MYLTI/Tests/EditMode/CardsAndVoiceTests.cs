using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mylti.Domain;
using NUnit.Framework;

namespace Mylti.Tests
{
    [TestFixture]
    public class VideoCardTests
    {
        [Test]
        public void Cards_CoverHookTitleEveryRoundResultsAndStandings()
        {
            var run = DirectorLikeRun.SportWins();
            var cards = VideoCardBuilder.Build(run);
            Assert.AreEqual("hook", cards[0].Id);
            Assert.IsTrue(cards[0].End <= 8.0);
            var title = cards.First(c => c.Id == "title");
            StringAssert.StartsWith("COMING UP:", title.Subtitle);
            StringAssert.Contains("A ROLLOVER", title.Subtitle);  // the run really has one
            int rounds = run.Plan.ScoredRounds.Count();
            Assert.AreEqual(rounds, cards.Count(c => c.Id.StartsWith("round")));
            Assert.AreEqual(rounds, cards.Count(c => c.Id.StartsWith("result")));
            Assert.IsTrue(cards.Any(c => c.Id == "standings" && c.Title.Contains("SPORT")), "standings card names the real winner");
            Assert.IsTrue(cards.Any(c => c.Style == CardStyle.Badge));
            for (int i = 1; i < cards.Count; i++) Assert.IsTrue(cards[i].Start >= cards[i - 1].Start);
        }

        [Test]
        public void RoundHooks_TeaseOnlyWhatReallyHappens_AndNeverTheWinner()
        {
            var run = DirectorLikeRun.SportWins();
            var cards = VideoCardBuilder.Build(run);
            var round4 = cards.First(c => c.Id == "round4");
            Assert.IsTrue(round4.TeaserFacts.Contains("NOT EVERYONE LANDS ON THEIR WHEELS"), "round 4 has a rollover");
            foreach (var c in cards.Where(c => c.Id.StartsWith("round") && c.Id != "round4"))
                Assert.IsFalse(c.TeaserFacts.Contains("NOT EVERYONE LANDS ON THEIR WHEELS"), c.Id + " has no rollover");
            foreach (var c in cards.Where(c => c.Id.StartsWith("round")))
                foreach (var v in VehicleCatalog.All)
                    Assert.IsFalse((c.Hook ?? "").Contains(v.DisplayNameEn.ToUpperInvariant()), "hook names a vehicle: " + c.Hook);
        }

        [Test]
        public void HookCheck_RejectsInventedCrashesNumbersBrandsAndWinners()
        {
            var card = new VideoCard { Id = "round1", Title = "ROUND 1", Subtitle = "18 M KICKER" };
            card.TeaserFacts.Add("A NEW LONGEST JUMP IS COMING");
            Assert.IsNull(VideoCardBuilder.CheckHook("THIS JUMP CHANGES EVERYTHING", card));
            Assert.IsNull(VideoCardBuilder.CheckHook("AN 18 M KICKER. WHO FLIES FURTHEST?", card));
            Assert.IsNotNull(VideoCardBuilder.CheckHook("SOMEONE CRASHES HARD", card));
            Assert.IsNotNull(VideoCardBuilder.CheckHook("A 60 M MONSTER JUMP", card));
            Assert.IsNotNull(VideoCardBuilder.CheckHook("FERRARI FLIES", card));
            Assert.IsNotNull(VideoCardBuilder.CheckHook("THE SPORTS CAR WINS IT", card));
        }

        [Test]
        public void ClaudeHooks_AreAppliedOnlyWhenTheyPassTheCheck()
        {
            var run = DirectorLikeRun.SportWins();
            var cards = VideoCardBuilder.Build(run);
            var r4 = cards.First(c => c.Id == "round4");
            var r1 = cards.First(c => c.Id == "round1");
            string before1 = r1.Hook;
            const string reply = "{\"hooks\":[{\"id\":\"round4\",\"hook\":\"WATCH WHAT HAPPENS AT THE LANDING\"},{\"id\":\"round1\",\"hook\":\"A 99 M RECORD FALLS\"}]}";
            var fake = new ClaudeClient("test-key") { Responder = (sys, user) => System.Threading.Tasks.Task.FromResult(reply) };
            VideoCardBuilder.ImproveHooksWithClaudeAsync(cards, fake, null).GetAwaiter().GetResult();
            Assert.AreEqual("WATCH WHAT HAPPENS AT THE LANDING", r4.Hook);
            Assert.AreEqual("claude", r4.HookSource);
            Assert.AreEqual(before1, r1.Hook, "invented number must be rejected");
        }

        [Test]
        public void Filter_UsesTextFilesAndTimedFades()
        {
            var run = DirectorLikeRun.SportWins();
            var cards = VideoCardBuilder.Build(run);
            string dir = Path.Combine(Path.GetTempPath(), "mylti_cards_test");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
            string f = VideoCardBuilder.BuildFilter(cards, dir, "C:/Windows/Fonts/arialbd.ttf", "C:/Windows/Fonts/impact.ttf");
            StringAssert.Contains("drawtext=fontfile='C\\:/Windows/Fonts/impact.ttf'", f);
            StringAssert.Contains("textfile=", f);
            StringAssert.Contains("enable='between(t,", f);
            Assert.IsFalse(f.Contains("text='"), "text must come from files (no escaping problems)");
            int files = Directory.GetFiles(dir, "*.txt").Length;
            Assert.AreEqual(f.Split(new[] { "drawtext=" }, System.StringSplitOptions.None).Length - 1, files);
            Assert.AreEqual("WHO FLIES\nFURTHEST?", VideoCardBuilder.Wrap("WHO FLIES FURTHEST?", 10));
        }
    }

    [TestFixture]
    public class KokoroTests
    {
        [Test]
        public void Speakable_SpellsOutNumbersUnitsAndStripsNonAscii()
        {
            Assert.AreEqual("A eighteen point five metres jump, at one hundred and twenty kilometres per hour!",
                KokoroTts.Speakable("A 18.5 m jump — at 120 km/h!").Replace(" ,", ","));
            Assert.AreEqual("the third round", KokoroTts.Speakable("the 3rd round"));
            Assert.AreEqual("twenty first", KokoroTts.Speakable("21st"));
            Assert.AreEqual("two thousand and five", KokoroTts.NumberWords("2005"));
            Assert.AreEqual("It's over", KokoroTts.Speakable("It\u2019s \"over\""));
            Assert.AreEqual("five more", KokoroTts.Speakable("5 more"));
        }

        [Test]
        public void PickBinary_PrefersStaticWindowsCliWithTts()
        {
            var names = new[]
            {
                "sherpa-onnx-v2.0.3-win-x64-shared-MD-Release.tar.bz2",
                "sherpa-onnx-v2.0.3-win-x64-shared-MT-Release.tar.bz2",
                "sherpa-onnx-v2.0.3-win-x64-static-MT-Release-no-tts.tar.bz2",
                "sherpa-onnx-v2.0.3-win-x64-static-MT-Release-lib.tar.bz2",
                "sherpa-onnx-v2.0.3-win-x64-static-MT-Release.tar.bz2",
                "sherpa-onnx-v2.0.3-win-x64-cuda.tar.bz2",
            };
            Assert.AreEqual("sherpa-onnx-v2.0.3-win-x64-static-MT-Release.tar.bz2", KokoroTts.PickBinary(names));
            Assert.AreEqual("sherpa-onnx-v2.0.3-win-x64-shared-MT-Release.tar.bz2", KokoroTts.PickBinary(names.Take(4)));
            Assert.IsNull(KokoroTts.PickBinary(new[] { "sherpa-onnx-v2.0.3-linux-x64-static.tar.bz2" }));
        }

        [Test]
        public void Settings_DefaultToKokoro_AndRoundTrip()
        {
            string p = Path.Combine(Path.GetTempPath(), "mylti_settings_kokoro.json");
            var s = new AppSettings { KokoroVoice = 16, ElevenLabsModel = "eleven_turbo_v2_5", BurnTextCards = false };
            Assert.AreEqual(TtsProviderKind.Kokoro, s.TtsProvider);
            s.Save(p);
            var l = AppSettings.Load(p, out string err);
            Assert.IsNull(err);
            Assert.AreEqual(TtsProviderKind.Kokoro, l.TtsProvider);
            Assert.AreEqual(16, l.KokoroVoice);
            Assert.AreEqual("eleven_turbo_v2_5", l.ElevenLabsModel);
            Assert.IsFalse(l.BurnTextCards);
            Assert.IsInstanceOf<KokoroTts>(TtsFactory.Create(l));
        }
    }
}
