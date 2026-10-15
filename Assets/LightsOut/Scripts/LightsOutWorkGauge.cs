using UnityEngine;

namespace LightsOut
{
    /// <summary>One cached world-space progress ring. Gameplay time and progress remain owned by the mover.</summary>
    public sealed class LightsOutWorkGauge : MonoBehaviour
    {
        const int Segments = 32;
        const float PixelRadius = 32;
        static readonly Color Mint = new Color(.55f, .91f, .77f, 1);
        static readonly Color Amber = new Color(1, .77f, .34f, 1);
        readonly Vector3[] fillVertices = new Vector3[(Segments + 1) * 2];
        LightsOutMover mover;
        Camera worldCamera;
        Transform ring;
        Mesh backgroundMesh, fillMesh;
        MeshRenderer fillRenderer;
        Material silhouetteMaterial, fillMaterial;
        float drawnProgress = -1;

        public bool IsVisible => ring && ring.gameObject.activeSelf;
        public float DisplayedProgress { get; private set; }

        public void Initialize(LightsOutMover player, Camera camera, Sprite sprite = null)
        {
            mover = player; worldCamera = camera;
            if (!ring)
            {
                ring = new GameObject("Room Work Progress Ring").transform;
                ring.SetParent(transform, false);
                Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
                if (!shader) shader = Shader.Find("Sprites/Default");
                silhouetteMaterial = new Material(shader) { name = "Work Ring Silhouette", hideFlags = HideFlags.HideAndDontSave };
                silhouetteMaterial.color = new Color(.015f, .035f, .055f, .9f);
                fillMaterial = new Material(shader) { name = "Work Ring Progress", hideFlags = HideFlags.HideAndDontSave };
                fillMaterial.color = Mint;
                backgroundMesh = CreateMesh("Work Ring Back", .78f, 1.08f, 1);
                fillMesh = CreateMesh("Work Ring Fill", .86f, 1, 0);
                fillMesh.MarkDynamic();
                CreateRingRenderer("Work Ring Back", backgroundMesh, silhouetteMaterial, 102);
                fillRenderer = CreateRingRenderer("Work Ring Fill", fillMesh, fillMaterial, 103);
            }
            Hide();
        }

        MeshRenderer CreateRingRenderer(string label, Mesh mesh, Material material, int order)
        {
            var item = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            item.transform.SetParent(ring, false);
            item.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = item.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material; renderer.sortingOrder = order;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        static Mesh CreateMesh(string label, float inner, float outer, float progress)
        {
            var mesh = new Mesh { name = label, hideFlags = HideFlags.HideAndDontSave };
            var vertices = new Vector3[(Segments + 1) * 2];
            var colors = new Color[vertices.Length];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[Segments * 6];
            WriteVertices(vertices, inner, outer, progress);
            for (int i = 0; i < vertices.Length; i++) { colors[i] = Color.white; uv[i] = new Vector2(.5f, .5f); }
            for (int i = 0; i < Segments; i++)
            {
                int v = i * 2, t = i * 6;
                triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 3;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            mesh.vertices = vertices; mesh.colors = colors; mesh.uv = uv; mesh.triangles = triangles;
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(2.2f, 2.2f, .1f));
            return mesh;
        }

        static void WriteVertices(Vector3[] vertices, float inner, float outer, float progress)
        {
            for (int i = 0; i <= Segments; i++)
            {
                // Noon origin, clockwise fill. Remaining triangles collapse at the end of the arc.
                float fraction = Mathf.Min(i / (float)Segments, progress);
                float angle = Mathf.PI * .5f - Mathf.PI * 2 * fraction;
                Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                vertices[i * 2] = direction * inner; vertices[i * 2 + 1] = direction * outer;
            }
        }

        public void Tick(float dt = 0)
        {
            if (!ring || !mover || !worldCamera || !mover.IsWorking || mover.Model == null || mover.WorkDuration <= 0)
            { Hide(); return; }
            float progress = mover.WorkProgress;
            if (progress <= 0) { Hide(); return; }
            ring.gameObject.SetActive(true); DisplayedProgress = progress;
            ring.position = new Vector3(mover.Position.x, mover.Position.y, -2);
            float height = worldCamera.pixelHeight > 0 ? worldCamera.pixelHeight : Screen.height;
            ring.localScale = Vector3.one * (2 * worldCamera.orthographicSize / Mathf.Max(1, height) * PixelRadius);
            if (Mathf.Abs(drawnProgress - progress) > .00001f)
            {
                WriteVertices(fillVertices, .86f, 1, progress); fillMesh.SetVertices(fillVertices);
                fillMaterial.color = Color.Lerp(Mint, Amber, Mathf.Clamp01((progress - .7f) / .3f));
                drawnProgress = progress;
            }
        }

        public void Hide()
        {
            if (ring) ring.gameObject.SetActive(false);
            DisplayedProgress = 0; drawnProgress = -1;
        }

        void OnDisable() { Hide(); }
        void OnDestroy()
        {
            if (backgroundMesh) Destroy(backgroundMesh);
            if (fillMesh) Destroy(fillMesh);
            if (silhouetteMaterial) Destroy(silhouetteMaterial);
            if (fillMaterial) Destroy(fillMaterial);
        }
    }
}
