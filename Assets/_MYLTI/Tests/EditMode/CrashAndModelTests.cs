using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mylti.Domain;
using NUnit.Framework;

namespace Mylti.Tests
{
    [TestFixture]
    public class CrashRoundTests
    {
        static AttemptRecord A(string v, AttemptOutcome o, double? dmg, double? impact = null, double? dist = null)
            => new AttemptRecord { AttemptId = v + "_" + Guid.NewGuid().ToString("N").Substring(0, 4), RoundIndex = 2, VehicleId = v, Outcome = o, DamagePercent = dmg, ImpactSpeedKmh = impact, LandingDistanceM = dist };

        static List<Entrant> Two() => new List<Entrant>
        {
            new Entrant { VehicleId = "sport_01", Lane = 0 }, new Entrant { VehicleId = "truck_01", Lane = 1 }
        };

        [Test]
        public void CrashWall_LeastDamageWins_AndNeedsARealImpact()
        {
            var spec = new RoundSpec { Index = 2, Rule = ScoringRule.CrashWall, CrashSpeedKmh = 90 };
            var att = new List<AttemptRecord>
            {
                A("sport_01", AttemptOutcome.Crashed, 62.4, 89),
                A("truck_01", AttemptOutcome.Crashed, 31.0, 88),
                A("truck_01", AttemptOutcome.Stuck, 0, null), // never reached the wall: does not count
            };
            var r = RoundScoring.Evaluate(spec, att, Two(), ContentLanguage.en, false);
            Assert.AreEqual("truck_01", r.WinnerVehicleId);
            Assert.AreEqual(31.0, r.BestMetric["truck_01"], 1e-9);
            StringAssert.Contains("least damage", r.Explanation);
            StringAssert.Contains("%", r.Explanation);
        }

        [Test]
        public void DropTest_RolloverStillScoresByDamage_LeastDamageJumpNeedsALanding()
        {
            var drop = new RoundSpec { Index = 2, Rule = ScoringRule.DropTest, DropHeightM = 30 };
            var r = RoundScoring.Evaluate(drop, new List<AttemptRecord> { A("sport_01", AttemptOutcome.Rollover, 80), A("truck_01", AttemptOutcome.CleanLanding, 45) }, Two(), ContentLanguage.en, false);
            Assert.AreEqual("truck_01", r.WinnerVehicleId);
            Assert.AreEqual(2, r.BestMetric.Count);

            var jump = new RoundSpec { Index = 2, Rule = ScoringRule.LeastDamage };
            var r2 = RoundScoring.Evaluate(jump, new List<AttemptRecord> { A("sport_01", AttemptOutcome.CleanLanding, 12, null, 55), A("truck_01", AttemptOutcome.NoJump, 0, null, null) }, Two(), ContentLanguage.en, false);
            Assert.AreEqual("sport_01", r2.WinnerVehicleId);
        }

        [Test]
        public void CrashLab_Plan_MixesJumpsCrashesAndDrops()
        {
            var entrants = Two();
            var rounds = RunPlanner.BuildRounds("CrashLab", "crash", entrants, "Progressive", 600, ContentLanguage.en);
            Assert.AreEqual(6, rounds.Count);
            Assert.IsTrue(rounds[0].IsJump);
            Assert.AreEqual(ScoringRule.CrashWall, rounds[1].Rule);
            Assert.AreEqual(ScoringRule.DropTest, rounds[2].Rule);
            Assert.AreEqual(ScoringRule.LeastDamage, rounds[5].Rule);
            Assert.IsTrue(rounds[5].HeadToHead);
            StringAssert.Contains("Crash Test at 50 km/h", rounds[1].Title);
            StringAssert.Contains("Drop Test", rounds[2].Title);
            StringAssert.Contains("km/h", rounds[1].SubQuestion);
            Assert.IsTrue(rounds[3].CrashSpeedKmh > rounds[1].CrashSpeedKmh);
            Assert.IsTrue(rounds[4].DropHeightM > rounds[2].DropHeightM);
            Assert.IsTrue(ConceptGenerator.TwistsFor("VehicleVersus").Contains("crash"));
            var concepts = ConceptGenerator.Generate("CrashLab", entrants, 3, ContentLanguage.en, null);
            Assert.AreEqual(3, concepts.Count);
            Assert.IsTrue(concepts.All(c => c.SeriesId == "CrashLab"));
        }

        [Test]
        public void CrashResultLine_UsesOnlyTheSlotFacts_AndPassesTheGuard()
        {
            var a = A("truck_01", AttemptOutcome.Crashed, 47.2, 91);
            a.WheelsLost = 1;
            var s = new NarrationSlot { Id = "s1", Kind = "result", Start = 10, End = 16, RoundIndex = 2 };
            s.Attempts.Add(a);
            string n = VehicleCatalog.Find("truck_01").DisplayNameEn;
            s.Facts["damagePercent_" + n] = "47";
            s.Facts["wallImpactKmh_" + n] = "91";
            foreach (var v in new[] { "47", "47.2", "91", "1" }) s.AllowedNumbers.Add(v);
            for (int seed = 0; seed < 8; seed++)
            {
                string line = NarrationWriter.Template(s, null, new Random(seed));
                StringAssert.Contains("47 percent damage", line);
                Assert.IsNull(SlotGuard.Check(line, s, null), line);
            }
            // a crash word is not allowed in a jump result without a rollover
            var jump = new NarrationSlot { Id = "s2", Kind = "result", Start = 0, End = 5 };
            jump.Attempts.Add(A("sport_01", AttemptOutcome.CleanLanding, 5, null, 40));
            Assert.IsNotNull(SlotGuard.Check("What a crash!", jump, null));
        }

        [Test]
        public void EpisodeWriter_AcceptsCrashAndDropRounds()
        {
            string json = "{\"seriesId\":\"CrashLab\",\"twistKind\":\"crash\",\"vehicles\":[\"sport_01\",\"truck_01\"],\"viewerQuestion\":\"Which one survives?\",\"viewerPromise\":\"Crashes and drops.\",\"stakes\":\"Least damage wins.\",\"workingTitle\":\"Crash Lab\","
                        + "\"rounds\":[{\"rule\":\"FurthestValidLanding\",\"kickerHeight\":6},{\"rule\":\"CrashWall\",\"crashSpeedKmh\":400},{\"rule\":\"DropTest\",\"dropHeightM\":25},"
                        + "{\"rule\":\"CrashWall\",\"crashSpeedKmh\":100},{\"rule\":\"DropTest\",\"dropHeightM\":40},{\"rule\":\"LeastDamage\",\"kickerHeight\":20}]}";
            var script = EpisodeWriter.Parse(json, Two(), 1, out string err);
            Assert.IsNull(err, err);
            Assert.AreEqual(ScoringRule.CrashWall, script.Rounds[1].Rule);
            Assert.AreEqual(160f, script.Rounds[1].CrashSpeedKmh, 1e-3, "clamped to the game limit");
            Assert.AreEqual(25f, script.Rounds[2].DropHeightM, 1e-3);
        }
    }

    [TestFixture]
    public class UserVehicleTests
    {
        [TearDown] public void Reset() { VehicleCatalog.ClearUser(); VehicleCatalog.RealBrandsAllowed = false; }

        [Test]
        public void ModelFiles_GetATemplate_AndRegisterWithBrandNamesOnlyWhenAllowed()
        {
            string dir = Path.Combine(Path.GetTempPath(), "mylti_veh_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "hilux"));
            File.WriteAllBytes(Path.Combine(dir, "Toyota_Hilux_2024.glb"), new byte[] { 1 });
            File.WriteAllBytes(Path.Combine(dir, "hilux", "Ford_Mustang_GT.glb"), new byte[] { 1 });
            var log = new List<string>();
            var specs = UserVehicleSpec.LoadAll(dir, null, false, log);
            Assert.AreEqual(2, specs.Count);
            Assert.AreEqual(2, log.Count, "a template is written for each new model");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "Toyota_Hilux_2024.vehicle.json")));
            var hilux = specs.First(s => s.ModelRef.Contains("Hilux"));
            Assert.AreEqual("pickup", hilux.Class);
            Assert.AreEqual("Toyota", hilux.Brand);
            var def = VehicleCatalog.Find(UserVehicleSpec.IdFor(hilux.ModelRef));
            Assert.IsTrue(def.IsUserModel);
            Assert.AreEqual("Pickup Truck", def.DisplayNameEn, "generic name while brands are off");
            Assert.AreEqual(2, VehicleCatalog.Selectable.Count);
            Assert.AreEqual(1, VehicleCatalog.Lineups().Count);
            Assert.AreEqual("sports", specs.First(s => s.ModelRef.Contains("Mustang")).Class);

            // edit + brands on
            hilux.MassKg = 2250; hilux.PowerKw = 150; hilux.License = "CC-BY 4.0 by someone";
            hilux.Save();
            var specs2 = UserVehicleSpec.LoadAll(dir, null, true, null);
            var def2 = VehicleCatalog.Find(UserVehicleSpec.IdFor(hilux.ModelRef));
            StringAssert.StartsWith("Toyota Hilux", def2.DisplayNameEn);
            Assert.AreEqual(2250f, def2.MassKg, 1e-3);
            Assert.IsTrue(def2.MotorTorquePerWheel > 300);
            Assert.AreEqual(5, VehicleCatalog.All.Count, "3 built-in + 2 user");
        }

        [Test]
        public void BrandWords_AreAllowedOnlyForTheOwnersVehicles()
        {
            var spec = new UserVehicleSpec { ModelRef = "res:Vehicles/Toyota_Hilux", Brand = "Toyota", Model = "Hilux", Class = "pickup" };
            VehicleCatalog.RealBrandsAllowed = true;
            VehicleCatalog.RegisterUser(spec.ToDefinition(true));
            Assert.IsNull(ClaimChecker.FindBlockedBrand("can the toyota hilux survive a 90 km/h crash?"));
            Assert.AreEqual("ferrari", ClaimChecker.FindBlockedBrand("toyota vs ferrari"));
            VehicleCatalog.RealBrandsAllowed = false;
            Assert.AreEqual("toyota", ClaimChecker.FindBlockedBrand("the toyota hilux"));
        }

        [Test]
        public void ClassPresets_GiveSensiblePhysics()
        {
            foreach (var c in UserVehicleSpec.Classes)
            {
                var p = UserVehicleSpec.Preset(c);
                Assert.IsTrue(p.MassKg > 500 && p.WheelRadius > 0.2f && p.Suspension > 0.1f, c);
            }
            var truck = new UserVehicleSpec { ModelRef = "x.glb", Class = "truck" }.ToDefinition(false);
            var hatch = new UserVehicleSpec { ModelRef = "y.glb", Class = "hatchback" }.ToDefinition(false);
            Assert.IsTrue(truck.MassKg > hatch.MassKg * 3);
            Assert.AreEqual("truck", truck.BodyStyle);
        }
    }

    [TestFixture]
    public class MediaTests
    {
        [Test]
        public void RadianceHdr_DecodesFlatAndRunLengthScanlines()
        {
            // 8x2 image: row 0 RLE (all pixels 128,64,32 e=129 → 1.0,0.5,0.25), row 1 flat (e=0 → black)
            var sb = new List<byte>();
            sb.AddRange(Encoding.ASCII.GetBytes("#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y 2 +X 8\n"));
            sb.AddRange(new byte[] { 2, 2, 0, 8 });
            foreach (byte v in new byte[] { 128, 64, 32, 129 }) { sb.Add(128 + 8); sb.Add(v); }
            for (int i = 0; i < 8; i++) sb.AddRange(new byte[] { 10, 20, 30, 0 });
            var img = RadianceHdr.Decode(sb.ToArray());
            Assert.AreEqual(8, img.Width);
            Assert.AreEqual(2, img.Height);
            Assert.AreEqual(1.0f, img.Rgb[0], 1e-6);
            Assert.AreEqual(0.5f, img.Rgb[1], 1e-6);
            Assert.AreEqual(0.25f, img.Rgb[2], 1e-6);
            Assert.AreEqual(0f, img.Rgb[8 * 3], 1e-6);
            Assert.Throws<InvalidDataException>(() => RadianceHdr.Decode(Encoding.ASCII.GetBytes("nope\n")));
        }

        [Test]
        public void Soundtrack_CrashAndMusic_RenderCleanAndMusicDucksUnderVoice()
        {
            string path = Path.Combine(Path.GetTempPath(), "mylti_mix_test.wav");
            var st = new Soundtrack(6);
            var music = new float[44100];
            for (int i = 0; i < music.Length; i++) music[i] = (float)Math.Sin(i * 0.05) * 0.8f;
            st.AddMusic(music, 0.3f);
            st.AddCrash(1.0, 1f, true);
            var voice = new float[44100];
            for (int i = 0; i < voice.Length; i++) voice[i] = (float)Math.Sin(i * 0.11) * 0.5f;
            st.AddVoice(voice, 4.0);
            st.Render(path, 6);
            var outp = Wav.ReadMono(path, out int rate);
            Assert.AreEqual(44100, rate);
            Assert.IsTrue(outp.All(x => !float.IsNaN(x) && Math.Abs(x) <= 1f));
            double Rms(int a, int b) { double s = 0; for (int i = a; i < b; i++) s += outp[i] * outp[i]; return Math.Sqrt(s / (b - a)); }
            Assert.IsTrue(Rms((int)(1.0 * rate), (int)(1.3 * rate)) > Rms((int)(2.6 * rate), (int)(2.9 * rate)), "crash is louder than music alone");
        }
    }
}
