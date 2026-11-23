using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mylti.Domain;
using NUnit.Framework;
using V3 = System.Numerics.Vector3;

namespace Mylti.Tests
{
    [TestFixture]
    public class CrowdTests
    {
        static IEnumerable<HumanMeshData> AllKinds()
        {
            for (int s = 0; s < 12; s++) yield return HumanMesh.Build(s);
            for (int h = 0; h < HumanMesh.Heroes.Length; h++) { yield return HumanMesh.Build(100 + h, h, false); yield return HumanMesh.Build(200 + h, h, true); }
            yield return HumanMesh.Build(7, -1, true);
        }

        [Test]
        public void Spectators_AreValidSkinnedMeshes_StandingOnTheGround()
        {
            foreach (var m in AllKinds())
            {
                Assert.AreEqual(0, m.Triangles.Length % 3);
                Assert.IsTrue(m.Triangles.All(t => t >= 0 && t < m.Vertices.Length));
                Assert.AreEqual(m.Vertices.Length, m.Bones.Length);
                Assert.AreEqual(m.Vertices.Length, m.Cells.Length);
                Assert.IsTrue(m.Bones.All(b => b >= 0 && b < HumanMesh.BoneCount));
                Assert.IsTrue(m.Cells.All(c => HumanMesh.PaletteHex(c) != "FF00FF"), "every vertex has a real palette colour");
                Assert.IsTrue(m.Height >= 1.0f && m.Height <= 1.95f);
                float minY = m.Vertices.Min(v => v.Y), maxY = m.Vertices.Max(v => v.Y);
                Assert.IsTrue(minY > -0.02f && minY < 0.03f, "feet at y = 0: " + minY);
                Assert.IsTrue(Math.Abs(maxY - m.Height) < 0.12f, "top of head ≈ height");
                Assert.IsTrue(m.Vertices.Length < 3000, "light enough for hundreds of spectators");
            }
        }

        [Test]
        public void SuperheroSquad_IsOurOwnDesigns_WithCostumesCapesAndKids()
        {
            var names = HumanMesh.Heroes.Select(h => h.Name).ToList();
            Assert.AreEqual(7, names.Count);
            Assert.AreEqual(names.Count, names.Distinct().Count());
            Assert.AreEqual(names.Count, HumanMesh.Heroes.Select(h => h.Suit).Distinct().Count(), "every hero has their own suit colour");
            // no trademarked hero names anywhere in the squad
            foreach (var n in names)
                Assert.IsNull(ClaimChecker.FindBlockedBrand(n), n);
            foreach (var word in new[] { "spider", "super", "bat", "iron", "hulk", "marvel", "wonder", "flash", "thor" })
                Assert.IsFalse(names.Any(n => n.ToLowerInvariant().Contains(word)), word);

            for (int h = 0; h < HumanMesh.Heroes.Length; h++)
            {
                var d = HumanMesh.Heroes[h];
                var m = HumanMesh.Build(300 + h, h, false);
                Assert.IsTrue(m.Cells.Contains(80 + d.Suit), "suit colour used");
                Assert.IsTrue(m.Cells.Contains(80 + d.Accent), "mask / gloves / boots colour used");
                Assert.IsTrue(m.Cells.Contains(80 + d.EmblemColor), "chest emblem present");
                if (d.Cape >= 0) Assert.IsTrue(m.Vertices.Where((v, i) => m.Cells[i] == 80 + d.Cape).Min(v => v.Z) < -0.1f * m.Height, "cape hangs behind");
                var kid = HumanMesh.Build(300 + h, h, true);
                Assert.Less(kid.Height, 1.4f);
            }
        }

        static V3 HandCentre(HumanMeshData m, V3[] posed, int fore)
        {
            var idx = Enumerable.Range(0, m.Vertices.Length).Where(i => m.Bones[i] == fore && m.Vertices[i].Y < 0.47f * m.Height).ToList();
            var c = V3.Zero;
            foreach (int i in idx) c += posed[i];
            return c / idx.Count;
        }

        static V3[] Pose(HumanMeshData m, CrowdPose.Style style, float excite, float applause, float t)
        {
            var eu = new V3[HumanMesh.BoneCount];
            CrowdPose.Compute(style, excite, applause, t, 0f, eu, out float lift);
            return CrowdPose.Apply(m, eu, lift);
        }

        [Test]
        public void Cheering_RaisesBothHandsAboveTheHead()
        {
            var m = HumanMesh.Build(3);
            float headTop = m.Vertices.Max(v => v.Y);
            foreach (var style in new[] { CrowdPose.Style.Waver, CrowdPose.Style.Jumper })
                for (float t = 0; t < 2f; t += 0.25f)
                {
                    var p = Pose(m, style, 1f, 0f, t);
                    Assert.Greater(HandCentre(m, p, HumanMesh.LFore).Y, headTop - 0.05f, style + " left hand up");
                    Assert.Greater(HandCentre(m, p, HumanMesh.RFore).Y, headTop - 0.05f, style + " right hand up");
                }
        }

        [Test]
        public void Applause_BringsTheHandsTogetherInFrontOfTheChest()
        {
            var m = HumanMesh.Build(5);
            float closest = float.MaxValue, widest = 0f;
            for (float t = 0; t < 1f; t += 0.01f)
            {
                var p = Pose(m, CrowdPose.Style.Clapper, 0.6f, 1f, t);
                V3 l = HandCentre(m, p, HumanMesh.LFore), r = HandCentre(m, p, HumanMesh.RFore);
                float d = V3.Distance(l, r);
                closest = Math.Min(closest, d); widest = Math.Max(widest, d);
                Assert.Greater((l.Z + r.Z) * 0.5f, 0.05f, "hands in front of the body");
                Assert.Greater((l.Y + r.Y) * 0.5f, 0.5f * m.Height, "hands at chest height");
            }
            Assert.Less(closest, 0.09f, "hands meet");
            Assert.Greater(widest - closest, 0.08f, "hands open and close (it is a clap, not a pose)");
        }

        [Test]
        public void CalmSpectators_KeepTheirHandsDown()
        {
            var m = HumanMesh.Build(8);
            var p = Pose(m, CrowdPose.Style.Waver, CrowdMood.Base, 0f, 0.4f);
            Assert.Less(HandCentre(m, p, HumanMesh.LFore).Y, 0.55f * m.Height);
            Assert.Less(HandCentre(m, p, HumanMesh.RFore).Y, 0.55f * m.Height);
        }

        [Test]
        public void Mood_ReactsToRealEvents_AndCalmsDown()
        {
            var mood = new CrowdMood();
            Assert.AreEqual(CrowdMood.Base, mood.Level, 1e-4);
            mood.OnEvent("Takeoff", null);
            for (int i = 0; i < 20; i++) mood.Tick(0.05f);
            Assert.Greater(mood.Level, 0.6f, "takeoff gets the crowd going");
            float atOne = mood.Level;
            for (int i = 0; i < 20; i++) mood.Tick(0.05f);
            Assert.AreEqual(atOne, mood.LevelAgo(1f), 0.05, "history lets far spectators react later");
            mood.OnEvent("WallCrash", new Dictionary<string, double> { ["speedKmh"] = 80 });
            mood.Tick(0.05f);
            Assert.Greater(mood.Gasp, 0.9f, "crash = ooh");
            mood.OnEvent("RoundResult", null);
            for (int i = 0; i < 20; i++) mood.Tick(0.05f);
            Assert.Greater(mood.Applause, 0.9f, "result = applause");
            for (int i = 0; i < 400; i++) mood.Tick(0.05f);
            Assert.Less(mood.Applause, 0.05f);
            Assert.Less(mood.Gasp, 0.05f);
            Assert.Less(mood.Level, 0.25f, "back to a murmur");
        }

        static List<RunEvent> SampleEvents() => new List<RunEvent>
        {
            new RunEvent { Type = "Takeoff", ProgramTime = 3 },
            new RunEvent { Type = "Landed", ProgramTime = 5 },
            new RunEvent { Type = "RoundResult", ProgramTime = 8 },
            new RunEvent { Type = "WallCrash", ProgramTime = 20 },
        };

        [Test]
        public void Timeline_MatchesTheEvents()
        {
            CrowdMood.Timeline(SampleEvents(), 30, 0.05, out var lv, out var ap, out var gs);
            Assert.AreEqual(600, lv.Length);
            Assert.IsTrue(lv.Concat(ap).Concat(gs).All(x => x >= 0f && x <= 1.0001f));
            Assert.Less(lv[40], 0.2f, "calm before the jump (2 s)");
            Assert.Greater(lv[(int)(4.5 / 0.05)], 0.6f, "loud in the air");
            Assert.Greater(ap[(int)(9.5 / 0.05)], 0.8f, "applause after the result");
            Assert.Less(ap[(int)(18 / 0.05)], 0.1f);
            Assert.Greater(gs[(int)(20.2 / 0.05)], 0.8f, "ooh at the crash");
        }

        [Test]
        public void CrowdAudio_FollowsTheMood_AndStaysClean()
        {
            CrowdMood.Timeline(SampleEvents(), 30, 0.05, out var lv, out var ap, out var gs);
            var st = new Soundtrack(30);
            st.AddCrowd(lv, ap, gs, 0.05, 0.7f);
            string path = Path.Combine(Path.GetTempPath(), "mylti_crowd_test.wav");
            try
            {
                st.Render(path, 30);
                var s = Wav.ReadMono(path, out int rate);
                Assert.AreEqual(30 * rate, s.Length);
                double Rms(double a, double b) { double acc = 0; int n = 0; for (int i = (int)(a * rate); i < (int)(b * rate); i++) { acc += s[i] * s[i]; n++; } return Math.Sqrt(acc / n); }
                double calm = Rms(0.5, 2.0), air = Rms(4.0, 5.0), applause = Rms(9.5, 11.5);
                Assert.Greater(calm, 0.001, "a murmur, never digital silence");
                Assert.Greater(air, calm * 2, "the crowd gets louder in the air");
                Assert.Greater(applause, calm * 3, "applause is clearly audible");
                Assert.IsTrue(s.All(x => !float.IsNaN(x) && Math.Abs(x) <= 1f));
            }
            finally { if (File.Exists(path)) File.Delete(path); }
        }
    }
}
