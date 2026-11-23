using System;
using System.Linq;
using Mylti.Domain;
using NUnit.Framework;
using V3 = System.Numerics.Vector3;

namespace Mylti.Tests
{
    [TestFixture]
    public class ToonCarTests
    {
        [TearDown]
        public void Reset() => VehicleCatalog.Cast = VehicleCatalog.CastMode.Real;

        [Test]
        public void Toons_AreOriginal_NoBrands_AndRegisteredWithSanePhysics()
        {
            Assert.AreEqual(6, ToonCars.All.Length);
            Assert.AreEqual(ToonCars.All.Length, ToonCars.All.Select(t => t.Name).Distinct().Count());
            Assert.AreEqual(ToonCars.All.Length, ToonCars.All.Select(t => t.Body).Distinct().Count(), "each toon has its own colour");
            foreach (var t in ToonCars.All)
            {
                Assert.IsNull(ClaimChecker.FindBlockedBrand(t.Name + " " + t.ClassEn), t.Name);
                var d = VehicleCatalog.Find(t.Id);
                Assert.IsNotNull(d, t.Id);
                Assert.AreEqual("toon", d.BodyStyle);
                Assert.IsFalse(d.IsUserModel);
                Assert.Greater(d.PowerToWeight, 2.0, t.Name + " can climb the ramp");
                Assert.IsTrue(d.Track > d.Width * 0.7f && d.Wheelbase < d.Length, t.Name + " wheels under the body");
                Assert.Greater(t.CabinTop, t.Top);
                Assert.IsTrue(t.CabinRear < t.CabinFront && t.CabinRear >= -t.Length * 0.5f && t.CabinFront <= t.Length * 0.5f);
            }
        }

        [Test]
        public void Meshes_AreValid_AndTheFaceIsOnTheFront()
        {
            foreach (var t in ToonCars.All)
            {
                var parts = ToonCars.Build(t, true);
                foreach (var name in new[] { "Body", "Glass", "EyeL", "EyeR", "PupilL", "PupilR", "LidL", "LidR", "BrowL", "BrowR", "Mouth", "Wheels" })
                    Assert.IsTrue(parts.Any(p => p.Name == name), t.Name + " has " + name);
                var pal = ToonCars.Palette(t);
                foreach (var p in parts)
                {
                    Assert.AreEqual(0, p.Triangles.Length % 3);
                    Assert.IsTrue(p.Triangles.All(i => i >= 0 && i < p.Vertices.Length), p.Name);
                    Assert.IsTrue(p.Cells.All(c => c >= 0 && c < 16 && pal[c] != "FF00FF"), p.Name + " colours");
                }
                // eyes stick out of the front, mouth below the eyes, both on the nose
                var ex = ToonExpression.Neutral;
                var eyeL = ToonCars.Posed(t, parts.First(p => p.Name == "EyeL"), ex);
                var body = parts.First(p => p.Name == "Body").Vertices;
                Assert.Greater(eyeL.Max(v => v.Z), t.Length * 0.5f - 0.02f, t.Name + " eye visible from the front");
                Assert.Less(ToonCars.MouthCenter(t).Y, ToonCars.EyeCenter(t, -1).Y, "mouth under the eyes");
                Assert.Less(ToonCars.EyeCenter(t, -1).X, 0f);
                Assert.Greater(ToonCars.EyeCenter(t, 1).X, 0f);
                Assert.Less(body.Min(v => v.Y), t.Bottom + 0.01f);
            }
        }

        [Test]
        public void Lids_OpenAndClose_PupilsStayOnTheEye()
        {
            var t = ToonCars.All[0];
            var parts = ToonCars.Build(t);
            var lid = parts.First(p => p.Name == "LidL");
            var r = ToonCars.EyeRadii(t);
            var front = ToonCars.EyeCenter(t, -1) + new V3(0, 0, r.Z);
            float Cover(ToonExpression x) => ToonCars.Posed(t, lid, x).Max(v => v.Z) ;
            var open = ToonExpression.Surprised; var shut = ToonExpression.Wince;
            var ec = ToonCars.EyeCenter(t, -1);
            Assert.AreEqual(0, ToonCars.Posed(t, lid, open).Count(v => v.Y < ec.Y + r.Y * 0.35f && v.Z > ec.Z + r.Z * 0.3f), "open lid never covers the front of the eye");
            Assert.Greater(Cover(shut), front.Z, "closed lid covers the front of the eye");
            var pupil = parts.First(p => p.Name == "PupilL");
            foreach (float lx in new[] { -1f, 0f, 1f })
                foreach (float ly in new[] { -1f, 0f, 1f })
                {
                    var x = ToonExpression.Neutral; x.LookX = lx; x.LookY = ly;
                    var v = ToonCars.Posed(t, pupil, x);
                    var c = v.Aggregate(V3.Zero, (a, b) => a + b) / v.Length;
                    var rel = c - ToonCars.EyeCenter(t, -1);
                    float e = (rel.X * rel.X) / (r.X * r.X) + (rel.Y * rel.Y) / (r.Y * r.Y) + (rel.Z * rel.Z) / (r.Z * r.Z);
                    Assert.IsTrue(e > 0.8f && e < 1.4f, $"pupil on the eye surface ({lx},{ly}): {e}");
                }
        }

        [Test]
        public void Expressions_ReadCorrectly()
        {
            var t = ToonCars.All[3];
            ToonCars.PartPose(t, "Mouth", ToonExpression.Surprised, out _, out _, out var oh);
            ToonCars.PartPose(t, "Mouth", ToonExpression.Happy, out _, out _, out var smile);
            Assert.Greater(oh.Y / oh.X, smile.Y / smile.X, "surprised = round O, happy = wide smile");
            ToonCars.PartPose(t, "BrowL", ToonExpression.Determined, out _, out var angryL, out _);
            ToonCars.PartPose(t, "BrowR", ToonExpression.Determined, out _, out var angryR, out _);
            Assert.IsTrue(Math.Sign(angryL.Z) == -Math.Sign(angryR.Z) && angryL.Z != 0, "brows tilt symmetrically");
            ToonCars.PartPose(t, "BrowL", ToonExpression.Surprised, out var up, out _, out _);
            ToonCars.PartPose(t, "BrowL", ToonExpression.Wince, out var down, out _, out _);
            Assert.Greater(up.Y, down.Y);
            var mid = ToonExpression.Lerp(ToonExpression.Neutral, ToonExpression.Happy, 0.5f);
            Assert.AreEqual((ToonExpression.Neutral.MouthOpen + ToonExpression.Happy.MouthOpen) / 2f, mid.MouthOpen, 1e-5);
        }

        [Test]
        public void Cast_PicksTheRightVehicles()
        {
            VehicleCatalog.Cast = VehicleCatalog.CastMode.Toons;
            Assert.IsTrue(VehicleCatalog.Selectable.All(VehicleCatalog.IsToon));
            Assert.AreEqual(6, VehicleCatalog.Selectable.Count);
            Assert.IsTrue(VehicleCatalog.Lineups().All(l => l.All(id => id.StartsWith("toon_"))));
            VehicleCatalog.Cast = VehicleCatalog.CastMode.Real;
            Assert.IsFalse(VehicleCatalog.Selectable.Any(VehicleCatalog.IsToon));
            VehicleCatalog.Cast = VehicleCatalog.CastMode.Mixed;
            Assert.IsTrue(VehicleCatalog.Selectable.Any(VehicleCatalog.IsToon) && VehicleCatalog.Selectable.Any(d => !VehicleCatalog.IsToon(d)));
            var s = AppSettings.Load("/nonexistent/settings.json", out _);
            Assert.AreEqual("toons", s.VehicleCast, "new installs race the toons");
            Assert.AreEqual(VehicleCatalog.CastMode.Toons, s.CastMode);
        }
    }
}
