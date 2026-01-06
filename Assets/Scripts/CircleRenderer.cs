using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class CircleRenderer : MonoBehaviour
{
    public Material material;
    public int maxCircles = 20000;

    Mesh quad;
    MaterialPropertyBlock mpb;

    readonly List<Matrix4x4> matrices = new();
    readonly List<Vector4> colors = new();

    static readonly int ColorID = Shader.PropertyToID("_Color");
    const int BatchSize = 1023; // Unity limit for DrawMeshInstanced

    void Awake()
    {
        quad = BuildQuad();
        mpb = new MaterialPropertyBlock();
    }
    void OnEnable()
    {
        if (quad == null) quad = BuildQuad();
        if (mpb == null) mpb = new MaterialPropertyBlock();
    }


    // Call this from anywhere (Update, FixedUpdate, etc.)
    public void DrawCircle(Vector2 position, float radius, Color color)
    {
        if (matrices.Count >= maxCircles) return;

        // Scale quad so that shader radius=1 becomes your radius:
        // quad is 1 unit wide (from -0.5 to 0.5), so scale = diameter
        var trs = Matrix4x4.TRS(
            new Vector3(position.x, position.y, 0),
            Quaternion.identity,
            Vector3.one * (radius * 2f)
        );

        matrices.Add(trs);
        colors.Add(color);
    }
    public void DrawRectOutline(Vector2 center, Vector2 size, float thickness, Color color, float spacing = -1f)
    {
        if (spacing <= 0) spacing = thickness * 0.9f;

        Vector2 half = size * 0.5f;
        float left = center.x - half.x;
        float right = center.x + half.x;
        float bottom = center.y - half.y;
        float top = center.y + half.y;

        // Top + bottom edges
        for (float x = left; x <= right; x += spacing)
        {
            DrawCircle(new Vector2(x, top), thickness, color);
            DrawCircle(new Vector2(x, bottom), thickness, color);
        }

        // Left + right edges
        for (float y = bottom; y <= top; y += spacing)
        {
            DrawCircle(new Vector2(left, y), thickness, color);
            DrawCircle(new Vector2(right, y), thickness, color);
        }
    }


    void LateUpdate()
    {
        Render();
        matrices.Clear();
        colors.Clear();
    }

    void Render()
    {
        if (quad == null) quad = BuildQuad();
        if (mpb == null) mpb = new MaterialPropertyBlock();
        if (material == null || matrices.Count == 0) return;
        
        int total = matrices.Count;
        int i = 0;

        while (i < total)
        {
            int count = Mathf.Min(BatchSize, total - i);

            // Copy this batch
            var batchMatrices = new Matrix4x4[count];
            var batchColors = new Vector4[count];

            for (int k = 0; k < count; k++)
            {
                batchMatrices[k] = matrices[i + k];
                batchColors[k] = colors[i + k];
            }

            mpb.Clear();
            mpb.SetVectorArray(ColorID, batchColors);

            Graphics.DrawMeshInstanced(
                quad, 0, material, batchMatrices, count, mpb,
                ShadowCastingMode.Off, false, 0, null, LightProbeUsage.Off
            );

            i += count;
        }
    }

    static Mesh BuildQuad()
    {
        var m = new Mesh();
        m.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0),
            new Vector3(-0.5f,  0.5f, 0),
            new Vector3( 0.5f,  0.5f, 0),
            new Vector3( 0.5f, -0.5f, 0),
        };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        m.RecalculateBounds();
        return m;
    }
}
