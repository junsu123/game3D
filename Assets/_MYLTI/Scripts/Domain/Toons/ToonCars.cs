using System;
using System.Collections.Generic;
using System.Numerics;

namespace Mylti.Domain
{
    /// <summary>
    /// MYLTI Toons — the channel's own cartoon cars (original designs, no logos, no brands): chunky toy proportions,
    /// big headlight eyes with eyelids, brows and pupils, and a mouth on the front bumper. Pure geometry/data here;
    /// the Unity side (ToonCarRig / ToonFace) turns it into meshes and animates the face.
    /// Car space: x right, y up, z forward (nose at +z), origin on the ground between the axles. Metres.
    /// </summary>
    public sealed class ToonDesign
    {
        public string Id, Name, ClassEn, ClassVi, Personality;
        public string Body, Dark, Accent, Glass = "4F86B8", Iris, Tongue = "E5546B";
        public float Length, Width, Bottom, Top, CabinTop, CabinRear, CabinFront, CabinWidth = 0.78f, Round = 2.6f;
        public float WheelRadius, Wheelbase, Track;
        public float EyeSize, EyeSpacing = 0.5f, MouthWidth = 0.5f;
        public bool Spoiler, RoofLights, RollBar, Stripe, BigBumper;
        // physics
        public float MassKg, TorquePerWheel, TopSpeedKmh, SuspensionDistance = 0.25f, AirStability = 1.2f, AntiRoll = 10000f, CenterOfMassY = -0.1f;
    }

    /// <summary>Everything the face can do, 0..1 unless stated.</summary>
    public struct ToonExpression
    {
        public float LidL, LidR;         // 0 open … 1 closed
        public float LookX, LookY;       // -1 … 1 (pupils)
        public float Pupil;              // pupil scale (1 = normal, 0.6 = shocked, 1.25 = cute)
        public float BrowRaise;          // -1 … 1
        public float BrowAngry;          // -1 (worried) … 1 (determined)
        public float MouthWidth, MouthOpen; // scale of the mouth (1, 0.55 = smile)

        public static ToonExpression Neutral => new ToonExpression { LidL = 0.12f, LidR = 0.12f, Pupil = 1f, MouthWidth = 0.85f, MouthOpen = 0.35f };
        public static ToonExpression Happy => new ToonExpression { LidL = 0.2f, LidR = 0.2f, Pupil = 1.1f, BrowRaise = 0.4f, MouthWidth = 1f, MouthOpen = 0.62f };
        public static ToonExpression Determined => new ToonExpression { LidL = 0.38f, LidR = 0.38f, Pupil = 0.9f, BrowRaise = -0.4f, BrowAngry = 1f, MouthWidth = 0.95f, MouthOpen = 0.3f };
        public static ToonExpression Surprised => new ToonExpression { LidL = 0f, LidR = 0f, Pupil = 0.6f, BrowRaise = 1f, BrowAngry = -0.3f, MouthWidth = 0.5f, MouthOpen = 0.95f };
        public static ToonExpression Scared => new ToonExpression { LidL = 0f, LidR = 0f, Pupil = 0.55f, BrowRaise = 0.8f, BrowAngry = -1f, MouthWidth = 0.75f, MouthOpen = 0.45f };
        public static ToonExpression Wince => new ToonExpression { LidL = 1f, LidR = 1f, Pupil = 1f, BrowRaise = -0.6f, BrowAngry = 0.6f, MouthWidth = 0.8f, MouthOpen = 0.2f };
        public static ToonExpression Cheeky => new ToonExpression { LidL = 0.05f, LidR = 0.95f, Pupil = 1.1f, BrowRaise = 0.3f, BrowAngry = 0.2f, MouthWidth = 1f, MouthOpen = 0.55f }; // wink

        public static ToonExpression Lerp(ToonExpression a, ToonExpression b, float t)
        {
            float L(float x, float y) => x + (y - x) * t;
            return new ToonExpression
            {
                LidL = L(a.LidL, b.LidL), LidR = L(a.LidR, b.LidR), LookX = L(a.LookX, b.LookX), LookY = L(a.LookY, b.LookY),
                Pupil = L(a.Pupil, b.Pupil), BrowRaise = L(a.BrowRaise, b.BrowRaise), BrowAngry = L(a.BrowAngry, b.BrowAngry),
                MouthWidth = L(a.MouthWidth, b.MouthWidth), MouthOpen = L(a.MouthOpen, b.MouthOpen),
            };
        }
    }

    public sealed class ToonPart
    {
        public string Name;          // Body, Glass, EyeL/R, PupilL/R, LidL/R, BrowL/R, Mouth, Wheels (preview only)
        public Vector3 Pivot;        // part origin in car space; vertices are relative to it
        public Vector3[] Vertices;
        public int[] Triangles;
        public int[] Cells;          // palette cell (ToonCars.Cell*)
    }

    public static class ToonCars
    {
        // palette cells (4×4 texture per car)
        public const int CBody = 0, CDark = 1, CAccent = 2, CGlass = 3, CWhite = 4, CIris = 5, CPupil = 6, CMouth = 7,
                         CTongue = 8, CTyre = 9, CChrome = 10, CTail = 11, CLamp = 12, CBodyLight = 13;

        public static readonly ToonDesign[] All =
        {
            new ToonDesign
            {
                Id = "toon_zippy", Name = "Zippy", ClassEn = "little hatchback", ClassVi = "xe nhỏ", Personality = "hyper and cheerful",
                Body = "8BD346", Dark = "2E5E1E", Accent = "FFFFFF", Iris = "3A86FF",
                Length = 3.6f, Width = 1.76f, Bottom = 0.30f, Top = 0.98f, CabinTop = 1.68f, CabinRear = -1.45f, CabinFront = 0.45f, CabinWidth = 0.8f, Round = 2.3f,
                WheelRadius = 0.38f, Wheelbase = 2.3f, Track = 1.56f, EyeSize = 0.25f, EyeSpacing = 0.48f, Stripe = true,
                MassKg = 950, TorquePerWheel = 560, TopSpeedKmh = 175, AirStability = 1.4f,
            },
            new ToonDesign
            {
                Id = "toon_bruno", Name = "Bruno", ClassEn = "off-road pickup", ClassVi = "bán tải địa hình", Personality = "tough but kind",
                Body = "FF8C1A", Dark = "5A2D0C", Accent = "34343A", Iris = "6B4226",
                Length = 4.9f, Width = 2.02f, Bottom = 0.62f, Top = 1.42f, CabinTop = 2.12f, CabinRear = -0.45f, CabinFront = 0.95f, CabinWidth = 0.86f, Round = 3.2f,
                WheelRadius = 0.52f, Wheelbase = 3.1f, Track = 1.78f, EyeSize = 0.27f, EyeSpacing = 0.5f, RoofLights = true, RollBar = true, BigBumper = true,
                MassKg = 2100, TorquePerWheel = 1000, TopSpeedKmh = 160, SuspensionDistance = 0.32f, AirStability = 0.9f, AntiRoll = 13000, CenterOfMassY = -0.15f,
            },
            new ToonDesign
            {
                Id = "toon_duchess", Name = "Duchess", ClassEn = "classy coupe", ClassVi = "coupe sang chảnh", Personality = "proud and graceful",
                Body = "B79CED", Dark = "4B2E83", Accent = "F8F0FF", Iris = "E36397",
                Length = 4.8f, Width = 1.86f, Bottom = 0.27f, Top = 0.86f, CabinTop = 1.38f, CabinRear = -1.5f, CabinFront = 0.15f, CabinWidth = 0.76f, Round = 2.9f,
                WheelRadius = 0.36f, Wheelbase = 2.9f, Track = 1.6f, EyeSize = 0.22f, EyeSpacing = 0.52f, MouthWidth = 0.45f,
                MassKg = 1350, TorquePerWheel = 720, TopSpeedKmh = 195,
            },
            new ToonDesign
            {
                Id = "toon_tito", Name = "Turbo Tito", ClassEn = "racer", ClassVi = "xe đua", Personality = "cocky speedster",
                Body = "12B5B0", Dark = "0B3D3B", Accent = "FFD23F", Iris = "1B263B",
                Length = 4.4f, Width = 1.96f, Bottom = 0.22f, Top = 0.76f, CabinTop = 1.22f, CabinRear = -1.1f, CabinFront = 0.25f, CabinWidth = 0.66f, Round = 3.0f,
                WheelRadius = 0.36f, Wheelbase = 2.7f, Track = 1.68f, EyeSize = 0.21f, EyeSpacing = 0.53f, Spoiler = true, Stripe = true,
                MassKg = 1150, TorquePerWheel = 760, TopSpeedKmh = 215, AirStability = 1.7f, CenterOfMassY = -0.18f,
            },
            new ToonDesign
            {
                Id = "toon_bao", Name = "Big Bao", ClassEn = "bubble van", ClassVi = "xe van tròn", Personality = "gentle giant",
                Body = "6EC6FF", Dark = "1D4E89", Accent = "FFFFFF", Iris = "2D6A4F",
                Length = 4.6f, Width = 1.98f, Bottom = 0.36f, Top = 1.25f, CabinTop = 2.3f, CabinRear = -2.05f, CabinFront = 1.45f, CabinWidth = 0.9f, Round = 2.5f,
                WheelRadius = 0.42f, Wheelbase = 2.9f, Track = 1.7f, EyeSize = 0.29f, EyeSpacing = 0.47f, MouthWidth = 0.55f,
                MassKg = 1900, TorquePerWheel = 860, TopSpeedKmh = 150, SuspensionDistance = 0.3f, AirStability = 0.9f, AntiRoll = 13000, CenterOfMassY = 0.05f,
            },
            new ToonDesign
            {
                Id = "toon_rumble", Name = "Rumble", ClassEn = "monster truck", ClassVi = "xe tải quái vật", Personality = "loud and goofy",
                Body = "8E44AD", Dark = "3D1556", Accent = "39FF88", Iris = "F77F00",
                Length = 4.6f, Width = 2.3f, Bottom = 1.15f, Top = 1.8f, CabinTop = 2.45f, CabinRear = -0.75f, CabinFront = 0.75f, CabinWidth = 0.78f, Round = 3.0f,
                WheelRadius = 0.85f, Wheelbase = 3.0f, Track = 2.3f, EyeSize = 0.3f, EyeSpacing = 0.5f, BigBumper = true, RollBar = true,
                MassKg = 3200, TorquePerWheel = 1900, TopSpeedKmh = 145, SuspensionDistance = 0.45f, AirStability = 0.6f, AntiRoll = 20000, CenterOfMassY = -0.5f,
            },
        };

        public static ToonDesign Find(string id)
        {
            foreach (var d in All) if (string.Equals(d.Id, id, StringComparison.OrdinalIgnoreCase)) return d;
            return null;
        }

        /// <summary>16 palette colours (4×4) for one car.</summary>
        public static string[] Palette(ToonDesign d) => new[]
        {
            d.Body, d.Dark, d.Accent, d.Glass, "FAFAFA", d.Iris, "121212", "3A0D12",
            d.Tongue, "1C1C1E", "D9DDE3", "E63946", "FFE8A3", Lighten(d.Body, 0.35f), "FF00FF", "FF00FF",
        };

        static string Lighten(string hex, float k)
        {
            int c = Convert.ToInt32(hex, 16);
            int r = (c >> 16) & 255, g = (c >> 8) & 255, b = c & 255;
            r = (int)(r + (255 - r) * k); g = (int)(g + (255 - g) * k); b = (int)(b + (255 - b) * k);
            return r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
        }

        public static VehicleDefinition Definition(ToonDesign d) => new VehicleDefinition
        {
            Id = d.Id, DisplayNameEn = d.Name, DisplayNameVi = d.Name, ClassNounEn = "cartoon " + d.ClassEn, ClassNounVi = d.ClassVi + " hoạt hình",
            BodyStyle = "toon", DefaultColorHex = "#" + d.Body,
            MassKg = d.MassKg, Width = d.Width, Height = d.CabinTop, Length = d.Length,
            WheelRadius = d.WheelRadius, Wheelbase = d.Wheelbase, Track = d.Track, CenterOfMassY = d.CenterOfMassY,
            MotorTorquePerWheel = d.TorquePerWheel, BrakeTorque = d.MassKg * 2.6f, MaxSteerDeg = 30,
            TopSpeedKmh = d.TopSpeedKmh, DefaultTargetSpeedKmh = d.TopSpeedKmh * 0.72f,
            SuspensionDistance = d.SuspensionDistance, AntiRoll = d.AntiRoll, AirStability = d.AirStability, Downforce = 1.8f,
        };

        // ------------------------------------------------------------------ face rig geometry

        public static Vector3 EyeCenter(ToonDesign d, int side) // side -1 left, +1 right
        {
            float hw = d.Width * 0.5f;
            return new Vector3(side * hw * d.EyeSpacing, d.Top - d.EyeSize * 0.45f, d.Length * 0.5f - d.EyeSize * 0.42f);
        }

        public static Vector3 EyeRadii(ToonDesign d) => new Vector3(d.EyeSize, d.EyeSize * 1.12f, d.EyeSize * 0.72f);

        public static Vector3 MouthCenter(ToonDesign d)
        {
            float y = d.Bottom + (d.Top - d.Bottom) * (d.BigBumper ? 0.42f : 0.34f);
            return new Vector3(0, y, d.Length * 0.5f - 0.005f);
        }

        public static Vector3 BrowBase(ToonDesign d, int side)
        {
            var e = EyeCenter(d, side);
            return e + new Vector3(side * d.EyeSize * 0.08f, d.EyeSize * 1.32f, d.EyeSize * 0.15f);
        }

        /// <summary>Transform of a face part for an expression (Unity convention: Euler = Ry·Rx·Rz, degrees).</summary>
        public static void PartPose(ToonDesign d, string part, ToonExpression x, out Vector3 pos, out Vector3 euler, out Vector3 scale)
        {
            euler = Vector3.Zero;
            scale = Vector3.One;
            int side = part.EndsWith("L") ? -1 : 1;
            var er = EyeRadii(d);
            switch (part)
            {
                case "LidL":
                case "LidR":
                    pos = EyeCenter(d, side);
                    float lid = Math.Clamp(side < 0 ? x.LidL : x.LidR, 0f, 1f);
                    euler = new Vector3(-68f + 158f * lid, 0, 0);
                    break;
                case "PupilL":
                case "PupilR":
                {
                    float lx = Math.Clamp(x.LookX, -1f, 1f) * 0.42f, ly = Math.Clamp(x.LookY, -1f, 1f) * 0.38f;
                    float zf = MathF.Sqrt(Math.Max(0.05f, 1f - lx * lx - ly * ly));
                    pos = EyeCenter(d, side) + new Vector3(lx * er.X, ly * er.Y, (zf - 1f) * er.Z);
                    euler = new Vector3(-ly * 30f, lx * 35f, 0);
                    float p = Math.Clamp(x.Pupil <= 0 ? 1f : x.Pupil, 0.4f, 1.4f);
                    scale = new Vector3(p, p, 1f);
                    break;
                }
                case "BrowL":
                case "BrowR":
                    pos = BrowBase(d, side) + new Vector3(0, Math.Clamp(x.BrowRaise, -1f, 1f) * d.EyeSize * 0.3f, 0);
                    euler = new Vector3(0, 0, -side * Math.Clamp(x.BrowAngry, -1f, 1f) * 24f);
                    break;
                case "Mouth":
                    pos = MouthCenter(d);
                    scale = new Vector3(Math.Clamp(x.MouthWidth, 0.3f, 1.2f), Math.Clamp(x.MouthOpen, 0.12f, 1.1f), 1f);
                    break;
                case "EyeL":
                case "EyeR":
                    pos = EyeCenter(d, side);
                    break;
                default:
                    pos = Vector3.Zero;
                    break;
            }
        }

        // ------------------------------------------------------------------ mesh

        /// <summary>Build all parts. 'previewWheels' adds static wheels (the game builds its own spinning wheels).</summary>
        public static List<ToonPart> Build(ToonDesign d, bool previewWheels = false)
        {
            var parts = new List<ToonPart>();
            float L = d.Length, hw = d.Width * 0.5f, yb = d.Bottom, yt = d.Top, zf = L * 0.5f, zr = -L * 0.5f;

            // ---- body
            var b = new HumanMesh.Builder();
            b.Loft(new[]
            {
                (zr, hw * 0.84f, yb + 0.07f, yt - 0.1f),
                (zr + 0.06f, hw * 0.95f, yb + 0.02f, yt - 0.03f),
                (zr + 0.22f, hw, yb, yt),
                (zf - 0.24f, hw, yb, yt),
                (zf - 0.07f, hw * 0.96f, yb + 0.02f, yt - 0.03f),
                (zf, hw * 0.86f, yb + 0.06f, yt - 0.09f),
            }, d.Round, CBody, 32);
            // fat fenders over the wheels
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var c = new Vector3(sx * (hw - 0.1f), Math.Max(yb + 0.08f, d.WheelRadius + 0.06f), sz * d.Wheelbase * 0.5f);
                    b.Ellipsoid(c, new Vector3(0.2f, Math.Min(d.WheelRadius * 0.85f, (yt - yb) * 0.75f), d.WheelRadius * 1.28f), CBody, 0, 0f, 5, 14);
                }
            // bumpers
            float bumpR = d.BigBumper ? 0.12f : 0.075f;
            b.Tube(new Vector3(-hw * 0.88f, yb + bumpR * 0.9f, zf - bumpR * 0.6f), new Vector3(hw * 0.88f, yb + bumpR * 0.9f, zf - bumpR * 0.6f), bumpR, bumpR, 1f, CAccent, 0, 12);
            b.Tube(new Vector3(-hw * 0.88f, yb + bumpR * 0.9f, zr + bumpR * 0.6f), new Vector3(hw * 0.88f, yb + bumpR * 0.9f, zr + bumpR * 0.6f), bumpR, bumpR, 1f, CAccent, 0, 12);
            // tail lights
            for (int sx = -1; sx <= 1; sx += 2)
                b.Ellipsoid(new Vector3(sx * hw * 0.68f, yt - (yt - yb) * 0.3f, zr + 0.02f), new Vector3(0.13f, 0.08f, 0.05f), CTail, 0, -1f, 4, 8);
            // roof cap over the cabin
            float cw = hw * d.CabinWidth, cMid = (d.CabinRear + d.CabinFront) * 0.5f, cLen = d.CabinFront - d.CabinRear;
            b.Ellipsoid(new Vector3(0, d.CabinTop - 0.045f, cMid - cLen * 0.04f), new Vector3(cw * 1.02f, 0.1f, cLen * 0.43f), CBody, 0, 0f, 4, 16);
            if (d.Stripe) // racing stripe over hood and roof
            {
                b.Tube(new Vector3(0, yt + 0.004f, d.CabinFront - 0.05f), new Vector3(0, yt + 0.004f, zf - 0.12f), 0.15f, 0.15f, 0.04f, CAccent, 0, 10);
                b.Tube(new Vector3(0, d.CabinTop + 0.05f, d.CabinRear + cLen * 0.22f), new Vector3(0, d.CabinTop + 0.05f, d.CabinFront - cLen * 0.25f), 0.15f, 0.15f, 0.04f, CAccent, 0, 10);
            }
            if (d.Spoiler)
            {
                for (int sx = -1; sx <= 1; sx += 2)
                    b.Tube(new Vector3(sx * hw * 0.55f, yt - 0.02f, zr + 0.25f), new Vector3(sx * hw * 0.55f, yt + 0.32f, zr + 0.18f), 0.04f, 0.04f, 1f, CDark, 0, 6);
                b.Tube(new Vector3(-hw * 0.95f, yt + 0.34f, zr + 0.2f), new Vector3(hw * 0.95f, yt + 0.34f, zr + 0.2f), 0.2f, 0.2f, 0.18f, CAccent, 0, 10);
            }
            if (d.RoofLights)
            {
                b.Tube(new Vector3(-cw * 0.85f, d.CabinTop + 0.1f, d.CabinFront - cLen * 0.3f), new Vector3(cw * 0.85f, d.CabinTop + 0.1f, d.CabinFront - cLen * 0.3f), 0.035f, 0.035f, 1f, CDark, 0, 6);
                for (int i = 0; i < 4; i++)
                    b.Ellipsoid(new Vector3(-cw * 0.6f + i * cw * 0.4f, d.CabinTop + 0.17f, d.CabinFront - cLen * 0.3f + 0.03f), new Vector3(0.08f, 0.08f, 0.06f), CLamp, 0, -1f, 4, 8);
            }
            if (d.RollBar)
            {
                float rz = Math.Min(d.CabinRear - 0.35f, zr + 0.6f);
                for (int sx = -1; sx <= 1; sx += 2)
                    b.Tube(new Vector3(sx * hw * 0.78f, yt - 0.02f, rz), new Vector3(sx * hw * 0.7f, yt + 0.55f, rz + 0.1f), 0.05f, 0.05f, 1f, CChrome, 0, 8);
                b.Tube(new Vector3(-hw * 0.72f, yt + 0.55f, rz + 0.1f), new Vector3(hw * 0.72f, yt + 0.55f, rz + 0.1f), 0.05f, 0.05f, 1f, CChrome, 0, 8);
            }
            // eye sockets: a lighter ring behind each eye so the eyes read as headlights
            for (int s = -1; s <= 1; s += 2)
            {
                var ec = EyeCenter(d, s); var er = EyeRadii(d);
                b.Ellipsoid(ec + new Vector3(0, 0, -er.Z * 0.25f), new Vector3(er.X * 1.12f, er.Y * 1.1f, er.Z * 0.9f), CBodyLight, 0, -1f, 6, 14);
            }
            parts.Add(Part("Body", Vector3.Zero, b));

            // ---- glass cabin
            var g = new HumanMesh.Builder();
            float gb = yt - 0.06f, gt = d.CabinTop - 0.02f;
            g.Loft(new[]
            {
                (d.CabinRear, cw * 0.8f, gb, gb + (gt - gb) * 0.75f),
                (d.CabinRear + cLen * 0.18f, cw, gb, gt),
                (d.CabinFront - cLen * 0.3f, cw, gb, gt),
                (d.CabinFront - cLen * 0.06f, cw * 0.92f, gb, gb + (gt - gb) * 0.6f),
                (d.CabinFront, cw * 0.82f, gb, gb + 0.06f),
            }, 2.4f, CGlass, 24);
            parts.Add(Part("Glass", Vector3.Zero, g));

            // ---- face parts (vertices relative to their pivots)
            var radii = EyeRadii(d);
            for (int s = -1; s <= 1; s += 2)
            {
                string suf = s < 0 ? "L" : "R";
                var eye = new HumanMesh.Builder();
                eye.Ellipsoid(Vector3.Zero, radii, CWhite, 0, -1f, 10, 18);
                parts.Add(Part("Eye" + suf, EyeCenter(d, s), eye));

                var pu = new HumanMesh.Builder();
                float z0 = radii.Z * 0.96f;
                pu.Ellipsoid(new Vector3(0, 0, z0), new Vector3(radii.X * 0.48f, radii.Y * 0.5f, radii.Z * 0.12f), CIris, 0, -1f, 5, 16);
                pu.Ellipsoid(new Vector3(0, 0, z0 + radii.Z * 0.06f), new Vector3(radii.X * 0.3f, radii.Y * 0.32f, radii.Z * 0.1f), CPupil, 0, -1f, 5, 14);
                pu.Ellipsoid(new Vector3(radii.X * 0.13f, radii.Y * 0.16f, z0 + radii.Z * 0.15f), new Vector3(radii.X * 0.1f, radii.Y * 0.1f, radii.Z * 0.05f), CWhite, 0, -1f, 3, 8);
                parts.Add(Part("Pupil" + suf, EyeCenter(d, s), pu));

                var lid = new HumanMesh.Builder();
                lid.Ellipsoid(Vector3.Zero, radii * 1.08f, CBody, 0, 0f, 6, 18); // upper half-shell
                parts.Add(Part("Lid" + suf, EyeCenter(d, s), lid));

                var brow = new HumanMesh.Builder();
                float bl = d.EyeSize * 0.78f, br = d.EyeSize * 0.15f;
                brow.Tube(new Vector3(-bl, 0, 0), new Vector3(bl, 0, 0), br, br * 0.85f, 0.75f, CDark, 0, 8);
                brow.Ellipsoid(new Vector3(-bl, 0, 0), new Vector3(br, br, br * 0.75f), CDark, 0, -1f, 4, 8);
                brow.Ellipsoid(new Vector3(bl, 0, 0), new Vector3(br * 0.85f, br * 0.85f, br * 0.64f), CDark, 0, -1f, 4, 8);
                parts.Add(Part("Brow" + suf, BrowBase(d, s), brow));
            }
            var m = new HumanMesh.Builder();
            float mw = hw * d.MouthWidth, mh = (yt - yb) * 0.32f;
            m.Ellipsoid(Vector3.Zero, new Vector3(mw, mh, 0.07f), CMouth, 0, -1f, 8, 18);
            m.Ellipsoid(new Vector3(0, mh * 0.62f, 0.035f), new Vector3(mw * 0.72f, mh * 0.24f, 0.05f), CWhite, 0, -1f, 4, 14); // teeth
            m.Ellipsoid(new Vector3(0, -mh * 0.5f, 0.03f), new Vector3(mw * 0.46f, mh * 0.34f, 0.05f), CTongue, 0, -1f, 4, 12);
            parts.Add(Part("Mouth", MouthCenter(d), m));

            if (previewWheels)
            {
                var w = new HumanMesh.Builder();
                for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                    {
                        var c = new Vector3(sx * d.Track * 0.5f, d.WheelRadius, sz * d.Wheelbase * 0.5f);
                        w.Tube(c - new Vector3(0.17f, 0, 0), c + new Vector3(0.17f, 0, 0), d.WheelRadius, d.WheelRadius, 1f, CTyre, 0, 18);
                        w.Tube(c + new Vector3(sx * 0.15f, 0, 0), c + new Vector3(sx * 0.18f, 0, 0), d.WheelRadius * 0.55f, d.WheelRadius * 0.5f, 1f, CChrome, 0, 14);
                    }
                parts.Add(Part("Wheels", Vector3.Zero, w));
            }
            return parts;
        }

        static ToonPart Part(string name, Vector3 pivot, HumanMesh.Builder b) => new ToonPart
        {
            Name = name, Pivot = pivot, Vertices = b.V.ToArray(), Triangles = b.T.ToArray(), Cells = b.Cells.ToArray(),
        };

        /// <summary>Car-space vertices of a part for an expression (used by previews and tests; Unity uses transforms).</summary>
        public static Vector3[] Posed(ToonDesign d, ToonPart p, ToonExpression x)
        {
            Vector3 pos = p.Pivot, euler = Vector3.Zero, scale = Vector3.One;
            if (p.Name != "Body" && p.Name != "Glass" && p.Name != "Wheels") PartPose(d, p.Name, x, out pos, out euler, out scale);
            var q = CrowdPose.Euler(euler);
            var o = new Vector3[p.Vertices.Length];
            for (int i = 0; i < o.Length; i++) o[i] = pos + Vector3.Transform(p.Vertices[i] * scale, q);
            return o;
        }
    }
}
