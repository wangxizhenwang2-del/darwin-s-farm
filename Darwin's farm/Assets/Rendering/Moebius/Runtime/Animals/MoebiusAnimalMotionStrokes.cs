using UnityEngine;
using UnityEngine.Rendering;

namespace Darwin.Rendering
{
    // One reusable mesh follows measured air/water movement, without changing ecological state.
    [ExecuteAlways, DisallowMultipleComponent, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class MoebiusAnimalMotionStrokes : MonoBehaviour
    {
        public MoebiusAnimalMotionStyle style;
        public bool swimming;
        const int History = 128, Segments = 32, Strips = 11, Vertices = Strips * (Segments + 1) * 2;
        readonly Vector3[] positions = new Vector3[History];
        readonly float[] times = new float[History];
        readonly Vector3[] vertices = new Vector3[Vertices];
        readonly Color[] colors = new Color[Vertices];
        readonly Vector2[] uv = new Vector2[Vertices];
        Mesh mesh;
        MeshRenderer display;
        int head, count;
        Vector3 live, previous, heading = Vector3.right;
        float liveTime, previousTime, strength, measuredSpeed;
        Matrix4x4 toLocal;
        public int LiveMeshVertices => mesh != null ? mesh.vertexCount : 0;
        public float VisibleStrength => strength;
        public int HistoryCount => count;

        void OnEnable() { EnsureMesh(); ResetHistory(); }
        void EnsureMesh()
        {
            if (mesh != null) return;
            mesh = new Mesh { name = "Moebius animal motion strokes", hideFlags = HideFlags.DontSave };
            mesh.MarkDynamic();
            var triangles = new int[Strips * Segments * 6];
            int at = 0;
            for (int strip = 0; strip < Strips; strip++)
                for (int j = 0; j <= Segments; j++)
                {
                    int i = (strip * (Segments + 1) + j) * 2;
                    uv[i] = new Vector2(0, j / (float)Segments); uv[i + 1] = new Vector2(1, j / (float)Segments);
                    if (j == Segments) continue;
                    triangles[at++] = i; triangles[at++] = i + 2; triangles[at++] = i + 1;
                    triangles[at++] = i + 1; triangles[at++] = i + 2; triangles[at++] = i + 3;
                }
            mesh.vertices = vertices; mesh.colors = colors; mesh.uv = uv; mesh.triangles = triangles;
            GetComponent<MeshFilter>().sharedMesh = mesh;
            display = GetComponent<MeshRenderer>();
            display.shadowCastingMode = ShadowCastingMode.Off; display.receiveShadows = false;
        }

        public void ResetHistory() { count = head = 0; strength = measuredSpeed = 0; previousTime = float.NaN; }

        public void Evaluate(Vector3 position, float height, Camera camera, float time, float wingPhase, float intensity = 1)
        {
            EnsureMesh();
            if (style == null) style = Resources.Load<MoebiusAnimalMotionStyle>("Population/AnimalMotionVFX");
            if (style == null || style.material == null || camera == null) { display.enabled = false; return; }
            display.sharedMaterial = style.material;
            height = Mathf.Max(.1f, height);
            float dt = float.IsNaN(previousTime) ? 0 : time - previousTime;
            if (dt < 0 || dt > .5f || count > 0 && Vector3.Distance(position, previous) > height * 5)
            { ResetHistory(); dt = 0; }
            Vector3 travel = position - previous;
            Vector3 horizontal = new Vector3(travel.x, 0, travel.z);
            float speed = dt > .00001f ? horizontal.magnitude / dt : measuredSpeed;
            if (dt > .00001f) measuredSpeed = speed;
            if (horizontal.sqrMagnitude > .000001f) heading = horizontal.normalized;
            float desired = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.04f, height * .8f, speed));
            strength = Mathf.Lerp(strength, desired, 1 - Mathf.Exp(-dt * 9));
            if (count == 0 || time - times[head] >= 1f / 40f)
            {
                head = count == 0 ? 0 : (head + 1) % History;
                positions[head] = position; times[head] = time; count = Mathf.Min(count + 1, History);
            }
            live = previous = position; liveTime = previousTime = time;
            toLocal = transform.worldToLocalMatrix;
            float opacity = strength * style.intensity * Mathf.Clamp01(intensity);
            if (swimming)
            {
                DrawSwimming(height, time, opacity, intensity);
                mesh.vertices = vertices; mesh.colors = colors; mesh.RecalculateBounds();
                display.enabled = intensity > .001f;
                return;
            }
            Vector3 up = camera.transform.up, right = camera.transform.right;
            float span = Mathf.Min(style.trailLifetime, height * 3.2f / Mathf.Max(speed, height));
            for (int strip = 0; strip < 5; strip++)
                for (int j = 0; j <= Segments; j++)
                {
                    float age = j / (float)Segments, sampleTime = time - age * span;
                    Vector3 p = Sample(sampleTime, out bool valid) - heading * height * .2f;
                    float lane = strip - 2;
                    p += up * height * (lane * .095f + style.curvature * Mathf.Sin(age * 8 - time * 1.6f + strip) * age);
                    float taper = Mathf.Sin(Mathf.PI * age);
                    float broken = strip == 1 || strip == 3 ? Mathf.SmoothStep(0, 1,
                        Mathf.InverseLerp(-.2f, .35f, Mathf.Sin(age * 11 + strip * 2))) : 1;
                    float alpha = valid ? opacity * Mathf.Pow(1 - age, 1.6f) *
                        Mathf.SmoothStep(0, 1, age / .12f) * broken : 0;
                    Color color = strip == 2 ? style.ink : style.pale;
                    if (strip == 2) alpha *= .32f;
                    Put(strip, j, p, up, height * style.lineWidth * taper * (strip % 2 == 0 ? .65f : 1), color, alpha);
                }
            float seed = (GetInstanceID() & 127) * .07f;
            for (int arc = 0; arc < 6; arc++)
            {
                bool wing = arc < 2;
                float age = wing ? Mathf.Repeat(wingPhase + arc * .08f, 1) :
                    (time - (Mathf.Floor((time + seed) / .32f) - (arc - 2)) * .32f + seed) / 1.05f;
                Vector3 center = wing ? position : Sample(time - age * 1.05f, out _);
                bool available = wing || time - age * 1.05f >= times[(head - count + 1 + History) % History];
                float fade = available && age >= 0 && age < 1 ? Mathf.Sin(Mathf.PI * age) * (1 - age) : 0;
                center -= heading * height * (wing ? .03f : .25f + age * .22f);
                if (wing) center += right * height * (arc == 0 ? -.34f : .34f) + up * height * .07f;
                for (int j = 0; j <= Segments; j++)
                {
                    float u = j / (float)Segments;
                    float a = Mathf.Lerp(-1.05f, 1.15f, u);
                    float radius = height * (wing ? .11f + age * .13f : .18f + age * .36f);
                    Vector3 radial = right * Mathf.Cos(a) + up * Mathf.Sin(a) * .65f;
                    Vector3 p = center + radial * radius * (wing && arc == 0 ? -1 : 1);
                    Vector3 tangent = right * -Mathf.Sin(a) + up * Mathf.Cos(a) * .65f;
                    Vector3 across = Vector3.Cross(camera.transform.forward, tangent).normalized;
                    float taper = Mathf.Sin(Mathf.PI * u);
                    Put(5 + arc, j, p, across, height * style.lineWidth * .65f * taper * (1 - age * .4f),
                        style.pale, opacity * fade * taper * (wing ? style.wingtipStrength : style.gustStrength));
                }
            }
            mesh.vertices = vertices; mesh.colors = colors; mesh.RecalculateBounds();
            display.enabled = opacity > .005f;
        }

        void DrawSwimming(float height, float time, float opacity, float intensity)
        {
            for (int lane = 0; lane < 5; lane++)
                for (int j = 0; j <= Segments; j++)
                {
                    float age = j / (float)Segments, t = time - age * style.wakeLifetime;
                    Vector3 forward = SampleHeading(t), side = Vector3.Cross(Vector3.up, forward).normalized;
                    Vector3 p = Sample(t, out bool valid) - forward * height * .22f;
                    float direction = lane == 2 ? 0 : lane % 2 == 0 ? -1 : 1;
                    float spread = lane < 3 ? .2f + age * .52f : .28f + age * .65f;
                    p += side * height * (direction * spread + Mathf.Sin(age * 8 - time * 1.2f + lane) * .018f * age);
                    p.y = live.y + lane * .001f;
                    float alpha = valid ? opacity * Mathf.Pow(1 - age, 1.4f) * Mathf.SmoothStep(0, 1, age / .13f) : 0;
                    if (lane >= 3) alpha *= .35f * Mathf.SmoothStep(0, 1,
                        Mathf.InverseLerp(-.2f, .3f, Mathf.Sin(age * 12 + lane)));
                    if (lane == 2) alpha *= .22f;
                    Put(lane, j, p, side, height * style.lineWidth * Mathf.Sin(Mathf.PI * age),
                        lane == 2 ? style.ink : style.pale, alpha);
                }
            for (int ring = 0; ring < 6; ring++)
            {
                float interval = Mathf.Max(.1f, style.rippleInterval);
                float born = (Mathf.Floor(time / interval) - ring) * interval;
                float age = time - born, life = age / Mathf.Max(.1f, style.rippleLifetime);
                Vector3 center = Sample(born, out bool available), forward = SampleHeading(born);
                Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
                Vector3 delta = Sample(born + .025f, out _) - Sample(born - .025f, out _); delta.y = 0;
                float birthSpeed = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.04f, height * .8f, delta.magnitude / .05f));
                float fade = available && life >= 0 && life < 1 ? Mathf.Sin(Mathf.PI * life) * Mathf.Pow(1 - life, 1.3f) : 0;
                float radius = height * (.22f + age * style.rippleExpansion);
                for (int j = 0; j <= Segments; j++)
                {
                    float a = j / (float)Segments * Mathf.PI * 2;
                    Vector3 radial = side * Mathf.Cos(a) + forward * Mathf.Sin(a) * .65f;
                    float distortion = 1 + .06f * Mathf.Sin(a * 3 + born) + .025f * Mathf.Sin(a * 7 - born);
                    Vector3 p = center + radial * radius * distortion; p.y = live.y + .007f;
                    float gap = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(-.15f, .2f,
                        Mathf.Sin(a * 3 + born * 1.7f) + .3f * Mathf.Sin(a * 5 - born)));
                    Put(5 + ring, j, p, radial.normalized, height * style.lineWidth * .7f * (1 - life * .65f),
                        style.pale, fade * birthSpeed * gap * style.intensity * intensity * .8f);
                }
            }
        }

        Vector3 SampleHeading(float time)
        {
            Vector3 travel = Sample(time + .025f, out _) - Sample(time - .025f, out _); travel.y = 0;
            return travel.sqrMagnitude > .000001f ? travel.normalized : heading;
        }

        Vector3 Sample(float time, out bool valid)
        {
            int oldest = (head - count + 1 + History) % History;
            valid = count > 0 && time >= times[oldest];
            if (time >= times[head]) return Vector3.Lerp(positions[head], live,
                Mathf.InverseLerp(times[head], liveTime, time));
            for (int i = 0; i < count - 1; i++)
            {
                int newer = (head - i + History) % History, older = (newer - 1 + History) % History;
                if (time >= times[older]) return Vector3.Lerp(positions[older], positions[newer], Mathf.InverseLerp(times[older], times[newer], time));
            }
            return positions[oldest];
        }
        void Put(int strip, int step, Vector3 center, Vector3 across, float width, Color color, float alpha)
        {
            int i = (strip * (Segments + 1) + step) * 2;
            vertices[i] = toLocal.MultiplyPoint3x4(center - across * width);
            vertices[i + 1] = toLocal.MultiplyPoint3x4(center + across * width);
            color.a = Mathf.Clamp01(alpha); colors[i] = colors[i + 1] = color;
        }
        void OnDisable()
        {
            if (display != null) display.enabled = false;
            if (mesh != null)
            {
                GetComponent<MeshFilter>().sharedMesh = null;
                if (Application.isPlaying) Destroy(mesh); else DestroyImmediate(mesh);
            }
            mesh = null; ResetHistory();
        }
    }
}
