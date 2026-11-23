using UnityEngine;

namespace Mylti
{
    /// <summary>
    /// Floating label above a vehicle: name + live speed (km/h, in-game). It is a world-space TextMesh,
    /// so it IS rendered into the recorded video. Always faces the camera, keeps a constant on-screen size,
    /// stays above the car even when it flips, and hides speed during replays (replay speed is slowed).
    /// </summary>
    public sealed class Nameplate : MonoBehaviour
    {
        public static bool Enabled = true;
        public VehicleController Vehicle;
        public ChallengeSession Session;

        Transform root;
        TextMesh nameText, nameShadow, speedText, speedShadow;
        float shownSpeed;

        public void Init(VehicleController v, ChallengeSession session, Color teamColor)
        {
            Vehicle = v;
            Session = session;
            var font = MaterialFactory.RuntimeFont();
            if (font == null) { enabled = false; return; }
            root = new GameObject("Nameplate").transform;
            root.SetParent(null, false); // world space: never rotates with the vehicle
            nameShadow = Make("NameShadow", font, Color.black, 64, new Vector3(0.025f, 0.32f, 0.01f));
            nameText = Make("Name", font, Color.Lerp(teamColor, Color.white, 0.55f), 64, new Vector3(0, 0.35f, 0));
            speedShadow = Make("SpeedShadow", font, Color.black, 80, new Vector3(0.025f, -0.03f, 0.01f));
            speedText = Make("Speed", font, new Color(1f, 0.86f, 0.2f), 80, Vector3.zero);
            nameText.text = nameShadow.text = v.Def.DisplayNameEn.ToUpperInvariant();
        }

        TextMesh Make(string n, Font font, Color c, int size, Vector3 offset)
        {
            var go = new GameObject(n);
            go.transform.SetParent(root, false);
            go.transform.localPosition = offset;
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = size;
            tm.characterSize = 0.01f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.fontStyle = FontStyle.Bold;
            tm.color = c;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return tm;
        }

        void LateUpdate()
        {
            if (root == null || Vehicle == null) return;
            var cam = Camera.main;
            bool active = Enabled && cam != null && IsInPlay();
            if (root.gameObject.activeSelf != active) root.gameObject.SetActive(active);
            if (!active) return;

            float lift = Vehicle.Def.Height + 1.4f;
            Vector3 pos = Vehicle.transform.position + Vector3.up * lift;
            float dist = Vector3.Distance(cam.transform.position, pos);
            if (dist > 260f || Vector3.Dot(cam.transform.forward, pos - cam.transform.position) < 0f) { root.gameObject.SetActive(false); return; }
            root.position = pos;
            root.rotation = Quaternion.LookRotation(pos - cam.transform.position, Vector3.up);
            float scale = Mathf.Clamp(dist * 0.055f, 0.6f, 9f) * (cam.fieldOfView / 55f);
            root.localScale = Vector3.one * scale;

            bool replay = Vehicle.ReplayMode;
            float kmh = replay ? 0f : Vehicle.SpeedKmh;
            shownSpeed = Mathf.Lerp(shownSpeed, kmh, Time.deltaTime * 8f);
            string s = replay ? "" : Mathf.RoundToInt(shownSpeed) + " km/h";
            if (speedText.text != s) speedText.text = speedShadow.text = s;
        }

        bool IsInPlay()
        {
            if (Vehicle.ReplayMode) return true;
            if (Session == null) return true;
            foreach (var t in Session.Active) if (t.Vehicle == Vehicle) return true;
            var cam = Camera.main;
            // line-up shots (intro / results): show the name when close to the camera
            return cam != null && Vehicle.InputLocked && Vector3.Distance(cam.transform.position, Vehicle.transform.position) < 25f;
        }

        void OnDestroy()
        {
            if (root != null) Destroy(root.gameObject);
        }
    }
}
