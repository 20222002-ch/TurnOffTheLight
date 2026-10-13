using UnityEngine;
using UnityEngine.UI;

namespace LightsOut
{
    // Vector geometry keeps the illustrated UI crisp at every desktop resolution.
    [AddComponentMenu("Lights Out/Vector Shape")]
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class LightsOutShape : UnityEngine.UI.MaskableGraphic
    {
        public enum Form { Rounded, Ellipse, Beam, Diamond }
        public Form form;
        public float radius = 12;
        public Color bottomColor = Color.clear;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = GetPixelAdjustedRect();
            if (form == Form.Beam)
            {
                vh.AddVert(new Vector3(r.xMin + r.width * .39f, r.yMax), color, Vector2.zero);
                vh.AddVert(new Vector3(r.xMin + r.width * .61f, r.yMax), color, Vector2.zero);
                vh.AddVert(new Vector3(r.xMax, r.yMin), bottomColor, Vector2.zero);
                vh.AddVert(new Vector3(r.xMin, r.yMin), bottomColor, Vector2.zero);
                vh.AddTriangle(0, 1, 2); vh.AddTriangle(0, 2, 3); return;
            }
            vh.AddVert(r.center, color, Vector2.zero);
            int segments = form == Form.Diamond ? 4 : 48;
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments;
                Vector2 p;
                if (form == Form.Ellipse || form == Form.Diamond)
                    p = r.center + new Vector2(Mathf.Cos(a) * r.width / 2, Mathf.Sin(a) * r.height / 2);
                else
                {
                    float rad = Mathf.Min(radius, r.width / 2, r.height / 2);
                    float sx = Mathf.Cos(a) >= 0 ? 1 : -1;
                    float sy = Mathf.Sin(a) >= 0 ? 1 : -1;
                    p = r.center + new Vector2(sx * (r.width / 2 - rad) + Mathf.Cos(a) * rad,
                        sy * (r.height / 2 - rad) + Mathf.Sin(a) * rad);
                }
                vh.AddVert(p, color, Vector2.zero);
                if (i > 0) vh.AddTriangle(0, i + 1, i);
            }
        }
    }
}
