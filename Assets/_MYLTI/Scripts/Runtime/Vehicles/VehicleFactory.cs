using Mylti.Domain;
using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Builds original placeholder vehicles from primitives (FR-02): body + 4 wheels + Rigidbody + WheelColliders.
    /// Replace the visual children with licensed meshes later; physics setup stays the same.
    /// </summary>
    public static class VehicleFactory
    {
        public static VehicleController Create(Entrant entrant, Transform parent, bool enableAudio)
        {
            var def = VehicleCatalog.Find(entrant.VehicleId);
            if (def == null) throw new System.ArgumentException("Unknown vehicle id: " + entrant.VehicleId);

            var root = new GameObject("Vehicle_" + def.Id);
            root.transform.SetParent(parent, false);
            var rb = root.AddComponent<Rigidbody>();
            rb.mass = def.MassKg;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.maxAngularVelocity = 14f;
            rb.linearDamping = 0.02f;
            rb.angularDamping = 0.05f;

            var paint = MaterialFactory.Hex(entrant.ColorHex ?? def.DefaultColorHex, 0.75f, 0.35f);
            var dark = MaterialFactory.Get(new Color(0.08f, 0.08f, 0.09f), 0.6f);
            var glass = MaterialFactory.Get(new Color(0.15f, 0.22f, 0.3f), 0.95f, 0.1f);
            var chrome = MaterialFactory.Get(new Color(0.8f, 0.8f, 0.82f), 0.9f, 0.9f);
            var lightMat = MaterialFactory.Get(new Color(1f, 0.95f, 0.75f), 0.8f, 0f, true);
            var tail = MaterialFactory.Get(new Color(0.9f, 0.05f, 0.05f), 0.8f, 0f, true);

            float W = def.Width, H = def.Height, Lg = def.Length, r = def.WheelRadius;
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            float bottom, lowerH;
            switch (def.BodyStyle)
            {
                case "truck":
                    bottom = r * 1.05f; lowerH = 0.55f;
                    Box(body, "Frame", new Vector3(0, bottom + lowerH * 0.5f, 0), new Vector3(W * 0.92f, lowerH, Lg), dark, true);
                    float cabL = 2.3f;
                    Box(body, "Cab", new Vector3(0, bottom + lowerH + 0.85f, Lg * 0.5f - cabL * 0.5f), new Vector3(W, 1.7f, cabL), paint, true);
                    Box(body, "Windshield", new Vector3(0, bottom + lowerH + 1.25f, Lg * 0.5f + 0.01f), new Vector3(W * 0.85f, 0.7f, 0.05f), glass, false);
                    Box(body, "Cargo", new Vector3(0, bottom + lowerH + 1.0f, -cabL * 0.5f - 0.1f), new Vector3(W, 2.0f, Lg - cabL - 0.2f), MaterialFactory.Get(new Color(0.85f, 0.86f, 0.88f), 0.4f), true);
                    Box(body, "CargoStripe", new Vector3(0, bottom + lowerH + 1.0f, -cabL * 0.5f - 0.1f), new Vector3(W + 0.02f, 0.35f, Lg - cabL - 0.4f), paint, false);
                    Box(body, "Grille", new Vector3(0, bottom + lowerH + 0.3f, Lg * 0.5f + 0.01f), new Vector3(W * 0.7f, 0.5f, 0.05f), chrome, false);
                    break;
                case "suv":
                    bottom = r * 0.95f; lowerH = 0.85f;
                    Box(body, "Lower", new Vector3(0, bottom + lowerH * 0.5f, 0), new Vector3(W, lowerH, Lg), paint, true);
                    Box(body, "Cabin", new Vector3(0, bottom + lowerH + 0.33f, -Lg * 0.06f), new Vector3(W * 0.92f, 0.66f, Lg * 0.62f), paint, true);
                    Box(body, "Glass", new Vector3(0, bottom + lowerH + 0.36f, -Lg * 0.06f), new Vector3(W * 0.93f, 0.46f, Lg * 0.6f), glass, false);
                    Box(body, "RoofRack", new Vector3(0, bottom + lowerH + 0.7f, -Lg * 0.06f), new Vector3(W * 0.8f, 0.06f, Lg * 0.5f), dark, false);
                    Box(body, "Bumper", new Vector3(0, bottom + 0.15f, Lg * 0.5f + 0.05f), new Vector3(W * 0.95f, 0.3f, 0.12f), dark, false);
                    break;
                default: // sport
                    bottom = r * 0.75f; lowerH = 0.5f;
                    Box(body, "Lower", new Vector3(0, bottom + lowerH * 0.5f, 0), new Vector3(W, lowerH, Lg), paint, true);
                    Box(body, "Cabin", new Vector3(0, bottom + lowerH + 0.2f, -Lg * 0.08f), new Vector3(W * 0.78f, 0.42f, Lg * 0.42f), paint, true);
                    Box(body, "Glass", new Vector3(0, bottom + lowerH + 0.22f, -Lg * 0.08f), new Vector3(W * 0.79f, 0.3f, Lg * 0.4f), glass, false);
                    Box(body, "Spoiler", new Vector3(0, bottom + lowerH + 0.25f, -Lg * 0.47f), new Vector3(W * 0.9f, 0.06f, 0.35f), dark, false);
                    Box(body, "SpoilerPostL", new Vector3(-W * 0.3f, bottom + lowerH + 0.11f, -Lg * 0.46f), new Vector3(0.06f, 0.22f, 0.1f), dark, false);
                    Box(body, "SpoilerPostR", new Vector3(W * 0.3f, bottom + lowerH + 0.11f, -Lg * 0.46f), new Vector3(0.06f, 0.22f, 0.1f), dark, false);
                    Box(body, "Splitter", new Vector3(0, bottom + 0.03f, Lg * 0.5f), new Vector3(W * 0.95f, 0.05f, 0.25f), dark, false);
                    break;
            }
            // lights
            float ly = bottom + lowerH * 0.65f;
            Box(body, "HeadL", new Vector3(-W * 0.33f, ly, Lg * 0.5f + 0.02f), new Vector3(0.35f, 0.12f, 0.04f), lightMat, false);
            Box(body, "HeadR", new Vector3(W * 0.33f, ly, Lg * 0.5f + 0.02f), new Vector3(0.35f, 0.12f, 0.04f), lightMat, false);
            Box(body, "TailL", new Vector3(-W * 0.36f, ly, -Lg * 0.5f - 0.02f), new Vector3(0.3f, 0.1f, 0.04f), tail, false);
            Box(body, "TailR", new Vector3(W * 0.36f, ly, -Lg * 0.5f - 0.02f), new Vector3(0.3f, 0.1f, 0.04f), tail, false);

            rb.centerOfMass = new Vector3(0, bottom + 0.3f + def.CenterOfMassY, 0);

            // wheels
            var vc = root.AddComponent<VehicleController>();
            vc.Def = def;
            vc.Entrant = entrant;
            float halfTrack = Mathf.Max(def.Track * 0.5f, W * 0.5f + 0.06f);
            float hb = def.Wheelbase * 0.5f;
            Vector3[] pos =
            {
                new Vector3(-halfTrack, 0, hb), new Vector3(halfTrack, 0, hb),
                new Vector3(-halfTrack, 0, -hb), new Vector3(halfTrack, 0, -hb)
            };
            string[] names = { "FL", "FR", "RL", "RR" };
            float load = def.MassKg * 9.81f / 4f;
            float spring = 2.5f * load / (def.SuspensionDistance * 0.5f); // stiff enough not to bottom out in the ramp transition
            float damper = 2f * Mathf.Sqrt(spring * def.MassKg / 4f) * 0.55f;
            for (int i = 0; i < 4; i++)
            {
                var wgo = new GameObject("Wheel_" + names[i]);
                wgo.transform.SetParent(root.transform, false);
                wgo.transform.localPosition = pos[i] + Vector3.up * (r + def.SuspensionDistance * 0.5f);
                var wc = wgo.AddComponent<WheelCollider>();
                wc.mass = Mathf.Clamp(def.MassKg / 60f, 15f, 70f);
                wc.radius = r;
                wc.wheelDampingRate = 0.6f;
                wc.suspensionDistance = def.SuspensionDistance;
                wc.forceAppPointDistance = 0.12f;
                wc.suspensionSpring = new JointSpring { spring = spring, damper = damper, targetPosition = 0.5f };
                wc.forwardFriction = new WheelFrictionCurve { extremumSlip = 0.4f, extremumValue = 1f, asymptoteSlip = 0.8f, asymptoteValue = 0.55f, stiffness = 1.6f };
                wc.sidewaysFriction = new WheelFrictionCurve { extremumSlip = 0.25f, extremumValue = 1f, asymptoteSlip = 0.5f, asymptoteValue = 0.75f, stiffness = 1.9f };
                vc.Wheels[i] = wc;

                // visual: pivot (posed from WheelCollider) → rotated cylinder child
                var pivot = new GameObject("WheelVisual_" + names[i]).transform;
                pivot.SetParent(root.transform, false);
                pivot.localPosition = pos[i] + Vector3.up * r;
                var tyre = MaterialFactory.Visual(PrimitiveType.Cylinder, pivot, Vector3.zero, new Vector3(r * 2f, 0.16f, r * 2f), dark, "Tyre");
                tyre.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                var rim = MaterialFactory.Visual(PrimitiveType.Cylinder, pivot, new Vector3(Mathf.Sign(pos[i].x) * 0.02f, 0, 0), new Vector3(r * 1.2f, 0.17f, r * 1.2f), chrome, "Rim");
                rim.transform.localRotation = Quaternion.Euler(0, 0, 90f);
                vc.WheelVisuals[i] = pivot;
            }
            vc.Wheels[0].ConfigureVehicleSubsteps(5f, 12, 15);
            vc.CaptureWheelRestPose();

            var rep = root.AddComponent<Replayable>();
            rep.Id = def.Id;
            rep.Body = rb;
            root.AddComponent<AIDriver>();
            if (enableAudio) root.AddComponent<VehicleAudio>();
            return vc;
        }

        static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool collider)
        {
            if (collider)
            {
                var go = MaterialFactory.Solid(PrimitiveType.Cube, parent, pos, size, mat, name);
                var bc = go.GetComponent<BoxCollider>();
                if (bc != null) bc.sharedMaterial = null;
            }
            else MaterialFactory.Visual(PrimitiveType.Cube, parent, pos, size, mat, name);
        }
    }
}
