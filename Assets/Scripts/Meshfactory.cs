using UnityEngine;

namespace DecisionTreeViz
{
    /// <summary>Unity has no built-in diamond, so we build an octahedron (flat-shaded, 8 faces).</summary>
    public static class MeshFactory
    {
        /// <summary>Half-height 1, half-width 'radius'. Scale the GameObject to resize.</summary>
        public static Mesh CreateDiamond(float radius = 0.75f)
        {
            float h = 1f;
            Vector3 top = new Vector3(0f, h, 0f);
            Vector3 bottom = new Vector3(0f, -h, 0f);
            Vector3[] ring =
            {
                new Vector3(radius, 0f, 0f),
                new Vector3(0f, 0f, radius),
                new Vector3(-radius, 0f, 0f),
                new Vector3(0f, 0f, -radius)
            };

            // 8 triangles x 3 unshared vertices = flat shading
            var vertices = new Vector3[24];
            var triangles = new int[24];
            int v = 0;

            for (int k = 0; k < 4; k++)
            {
                Vector3 a = ring[k];
                Vector3 b = ring[(k + 1) % 4];

                // upper face (outward normal), then lower face
                vertices[v] = top; vertices[v + 1] = b; vertices[v + 2] = a;
                v += 3;
                vertices[v] = bottom; vertices[v + 1] = a; vertices[v + 2] = b;
                v += 3;
            }

            for (int t = 0; t < 24; t++) triangles[t] = t;

            var mesh = new Mesh();
            mesh.name = "DecisionDiamond";
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}