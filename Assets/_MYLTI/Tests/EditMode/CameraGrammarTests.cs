using System;
using Mylti.Domain;
using NUnit.Framework;

namespace Mylti.Tests
{
    [TestFixture]
    public class CameraGrammarTests
    {
        [Test]
        public void Lens_FramesTheCarAtTheRequestedSize()
        {
            // trackside: 4.6 m car, 33 m away, 45 % of the frame → a long lens (narrow FOV)
            float fov = Lens.FovFor(4.6f, 33f, 0.45f, 1f, 90f);
            Assert.Less(fov, 15f, "telephoto");
            Assert.AreEqual(0.45, Lens.Fill(4.6f, 33f, fov), 0.01);
            // the same framing from 8 m needs a wide lens
            Assert.Greater(Lens.FovFor(4.6f, 8f, 0.45f, 1f, 90f), 35f);
            // limits are respected
            Assert.AreEqual(12f, Lens.FovFor(4.6f, 300f, 0.45f, 12f, 40f), 1e-4);
            Assert.AreEqual(40f, Lens.FovFor(40f, 5f, 0.45f, 12f, 40f), 1e-4);
        }

        [Test]
        public void ThirtyDegreeRule_DetectsJumpCuts()
        {
            Assert.IsTrue(Lens.WouldJumpCut(12f, 0.40f, 0.45f), "same angle, same size = jump cut");
            Assert.IsFalse(Lens.WouldJumpCut(45f, 0.40f, 0.45f), "new angle");
            Assert.IsFalse(Lens.WouldJumpCut(10f, 0.20f, 0.50f), "clearly tighter shot");
        }

        [Test]
        public void Blend_StartsAndEndsWithoutAJerk()
        {
            Assert.AreEqual(0.0, Lens.Smoother(0f), 1e-6);
            Assert.AreEqual(1.0, Lens.Smoother(1f), 1e-6);
            Assert.AreEqual(0.5, Lens.Smoother(0.5f), 1e-6);
            float h = 1e-3f;
            Assert.Less(Lens.Smoother(h) / h, 0.01, "zero speed at the start");
            Assert.Less((1f - Lens.Smoother(1f - h)) / h, 0.01, "zero speed at the end");
            float prev = 0f;
            for (float t = 0.01f; t <= 1f; t += 0.01f) { float v = Lens.Smoother(t); Assert.IsTrue(v >= prev); prev = v; }
        }
    }
}
