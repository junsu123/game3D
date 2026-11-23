using System.Linq;
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

            var paint = MaterialFactory.Hex(entrant.ColorHex ?? def.DefaultColorHex, 0.82f, 0.45f); // glossy metallic paint

            // real 3D model supplied by the channel owner?
            if (def.IsUserModel && VehicleModelLibrary.Instance != null)
            {
                var model = VehicleModelLibrary.Instance.Clone(def, root.transform);
                if (model != null) return FinishWithModel(root, rb, model, def, entrant, enableAudio);
            }

            float W = def.Width, H = def.Height, Lg = def.Length, r = def.WheelRadius;
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);

            // Physics shape: simple boxes (stable, cheap). Visual shape: smooth lofted body built on top.
            float bottom, lowerH;
            switch (def.BodyStyle)
            {
                case "truck":
                    bottom = r * 1.05f; lowerH = 0.55f;
                    Box(body, "Col_Frame", new Vector3(0, bottom + lowerH * 0.5f, 0), new Vector3(W * 0.92f, lowerH, Lg), paint, true);
                    Box(body, "Col_Cab", new Vector3(0, bottom + lowerH + 0.85f, Lg * 0.5f - 1.15f), new Vector3(W, 1.7f, 2.3f), paint, true);
                    Box(body, "Col_Cargo", new Vector3(0, bottom + lowerH + 1.0f, -1.25f), new Vector3(W, 2.0f, Lg - 2.5f), paint, true);
                    break;
                case "suv":
                    bottom = r * 0.95f; lowerH = 0.85f;
                    Box(body, "Col_Lower", new Vector3(0, bottom + lowerH * 0.5f, 0), new Vector3(W, lowerH, Lg), paint, true);
                    Box(body, "Col_Cabin", new Vector3(0, bottom + lowerH + 0.33f, -Lg * 0.06f), new Vector3(W * 0.92f, 0.66f, Lg * 0.62f), paint, true);
                    break;
                default:
                    bottom = r * 0.75f; lowerH = 0.5f;
                    Box(body, "Col_Lower", new Vector3(0, bottom + lowerH * 0.5f, 0), new Vector3(W, lowerH, Lg), paint, true);
                    Box(body, "Col_Cabin", new Vector3(0, bottom + lowerH + 0.2f, -Lg * 0.08f), new Vector3(W * 0.78f, 0.42f, Lg * 0.42f), paint, true);
                    break;
            }
            CarBodyBuilder.Build(def, body, def.BodyStyle == "truck" ? bottom + 0.1f : bottom, paint);

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
                CarBodyBuilder.Wheel(pivot, r, pos[i].x < 0);
                vc.WheelVisuals[i] = pivot;
            }
            vc.Wheels[0].ConfigureVehicleSubsteps(5f, 12, 15);
            vc.CaptureWheelRestPose();

            root.AddComponent<SpeedFx>().Init(vc, def.BodyStyle == "truck" ? bottom + 0.6f : bottom);
            var bodyMeshes = body.GetComponentsInChildren<MeshFilter>().Where(m => m.GetComponent<Renderer>() != null && m.GetComponent<Renderer>().enabled && !GlassLike(m.name)).ToList();
            var windows = body.GetComponentsInChildren<Renderer>().Where(x => x.enabled && GlassLike(x.name)).ToList();
            root.AddComponent<VehicleDamage>().Init(vc, bodyMeshes, windows, def.Length);
            var rep = root.AddComponent<Replayable>();
            rep.Id = def.Id;
            rep.Body = rb;
            root.AddComponent<AIDriver>();
            if (enableAudio) root.AddComponent<VehicleAudio>();
            return vc;
        }

        static bool GlassLike(string n)
        {
            n = n.ToLowerInvariant();
            return n.Contains("glass") || n.Contains("window") || n.Contains("windshield") || n.Contains("canopy");
        }

        /// <summary>Vehicle built around a real 3D model: colliders from its bounds, wheel colliders on its wheels.</summary>
        static VehicleController FinishWithModel(GameObject root, Rigidbody rb, GameObject model, VehicleDefinition def, Entrant entrant, bool enableAudio)
        {
            var rig = ModelRigger.Fit(model, def, root.transform);
            var bb = rig.Body;
            // real dimensions feed the rest of the app (camera framing, nameplate height, trails)
            def.Length = Mathf.Max(2.2f, bb.size.z);
            def.Width = Mathf.Max(1.2f, bb.size.x);
            def.Height = Mathf.Max(0.9f, bb.size.y);
            def.WheelRadius = rig.WheelRadius;
            def.Track = Mathf.Max(0.9f, rig.Track);
            def.Wheelbase = Mathf.Max(1.6f, Mathf.Abs(rig.Wheelbase));

            if (def.Repaint) Repaint(rig, entrant.ColorHex ?? def.DefaultColorHex);

            // physics proxies: lower body (axle height → 55 %) and cabin (55 % → 95 %), invisible boxes
            var col = new GameObject("Colliders").transform;
            col.SetParent(root.transform, false);
            float floorY = Mathf.Max(rig.WheelCenterY * 0.9f, bb.min.y + 0.12f);
            float midY = bb.min.y + bb.size.y * 0.55f;
            BoxCol(col, "Col_Lower", new Vector3(bb.center.x, (floorY + midY) * 0.5f, bb.center.z), new Vector3(bb.size.x * 0.96f, midY - floorY, bb.size.z * 0.98f));
            float topY = bb.min.y + bb.size.y * 0.95f;
            BoxCol(col, "Col_Cabin", new Vector3(bb.center.x, (midY + topY) * 0.5f, bb.center.z - bb.size.z * 0.04f), new Vector3(bb.size.x * 0.86f, topY - midY, bb.size.z * 0.62f));
            float axleMidZ = (rig.FrontAxleZ + rig.RearAxleZ) * 0.5f;
            rb.centerOfMass = new Vector3(0, rig.WheelCenterY + 0.15f + def.CenterOfMassY, axleMidZ);

            var vc = root.AddComponent<VehicleController>();
            vc.Def = def;
            vc.Entrant = entrant;
            string[] names = { "FL", "FR", "RL", "RR" };
            float hx = Mathf.Max(0.45f, rig.Track * 0.5f);
            Vector3[] pos =
            {
                new Vector3(-hx, 0, rig.FrontAxleZ), new Vector3(hx, 0, rig.FrontAxleZ),
                new Vector3(-hx, 0, rig.RearAxleZ), new Vector3(hx, 0, rig.RearAxleZ)
            };
            if (rig.WheelsFound)
                for (int i = 0; i < 4; i++) pos[i] = new Vector3(rig.WheelPivots[i].localPosition.x, 0, rig.WheelPivots[i].localPosition.z);
            float load = def.MassKg * 9.81f / 4f;
            float spring = 2.5f * load / (def.SuspensionDistance * 0.5f);
            float damper = 2f * Mathf.Sqrt(spring * def.MassKg / 4f) * 0.55f;
            for (int i = 0; i < 4; i++)
            {
                var wgo = new GameObject("Wheel_" + names[i]);
                wgo.transform.SetParent(root.transform, false);
                wgo.transform.localPosition = pos[i] + Vector3.up * (rig.WheelCenterY + def.SuspensionDistance * 0.5f);
                var wc = wgo.AddComponent<WheelCollider>();
                wc.mass = Mathf.Clamp(def.MassKg / 60f, 15f, 120f);
                wc.radius = rig.WheelRadius;
                wc.wheelDampingRate = 0.6f;
                wc.suspensionDistance = def.SuspensionDistance;
                wc.forceAppPointDistance = 0.12f;
                wc.suspensionSpring = new JointSpring { spring = spring, damper = damper, targetPosition = 0.5f };
                wc.forwardFriction = new WheelFrictionCurve { extremumSlip = 0.4f, extremumValue = 1f, asymptoteSlip = 0.8f, asymptoteValue = 0.55f, stiffness = 1.6f };
                wc.sidewaysFriction = new WheelFrictionCurve { extremumSlip = 0.25f, extremumValue = 1f, asymptoteSlip = 0.5f, asymptoteValue = 0.75f, stiffness = 1.9f };
                vc.Wheels[i] = wc;
                vc.WheelVisuals[i] = rig.WheelPivots[i]; // null when the model's wheels could not be identified
            }
            vc.Wheels[0].ConfigureVehicleSubsteps(5f, 12, 15);
            vc.CaptureWheelRestPose();
            vc.ModelNotes = rig.Notes;

            root.AddComponent<SpeedFx>().Init(vc, Mathf.Max(0.3f, rig.WheelCenterY));
            root.AddComponent<VehicleDamage>().Init(vc, rig.BodyMeshes, rig.Windows, def.Length);
            var rep = root.AddComponent<Replayable>();
            rep.Id = def.Id;
            rep.Body = rb;
            root.AddComponent<AIDriver>();
            if (enableAudio) root.AddComponent<VehicleAudio>();
            return vc;
        }

        static void BoxCol(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var bc = go.AddComponent<BoxCollider>();
            bc.center = center;
            bc.size = new Vector3(Mathf.Max(0.3f, size.x), Mathf.Max(0.2f, size.y), Mathf.Max(0.5f, size.z));
        }

        static void Repaint(ModelRig rig, string hex)
        {
            if (!ColorUtility.TryParseHtmlString(hex, out var c)) return;
            foreach (var r in rig.BodyRenderers)
            {
                if (rig.Windows.Contains(r)) continue;
                var mats = r.materials; // instance copies
                foreach (var m in mats)
                {
                    if (m == null) continue;
                    string n = m.name.ToLowerInvariant();
                    if (!(n.Contains("paint") || n.Contains("body") || n.Contains("exterior") || n.Contains("carpaint"))) continue;
                    if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", c);
                    else if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                    else if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                }
                r.materials = mats;
            }
        }

        static void Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat, bool collider)
        {
            if (collider)
            {
                var go = MaterialFactory.Solid(PrimitiveType.Cube, parent, pos, size, mat, name);
                go.GetComponent<Renderer>().enabled = false; // collision proxy only
            }
            else MaterialFactory.Visual(PrimitiveType.Cube, parent, pos, size, mat, name);
        }
    }
}
