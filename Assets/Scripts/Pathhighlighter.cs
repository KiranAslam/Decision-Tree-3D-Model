using System.Collections.Generic;
using UnityEngine;

namespace DecisionTreeViz
{
    /// <summary>
    /// Shows ONE path through the tree brightly and fades everything else.
    ///
    /// Attach to: DT_Model / NetworkOrigin / DecisionTreeVisualizer  (the same object as DecisionTreeVisualizer).
    ///
    /// "Transparent" is done by blending the faded nodes into the background colour and by lowering the
    /// alpha of branch lines and labels. This works with any render pipeline and any material.
    /// </summary>
    public class PathHighlighter : MonoBehaviour
    {
        [SerializeField] private DecisionTreeVisualizer visualizer;

        [Header("Nodes on the path")]
        [Tooltip("Multiplies the normal glow of the node.")]
        [SerializeField] private float highlightGlow = 1.6f;
        [Tooltip("Nodes on the path become this much bigger.")]
        [SerializeField] private float highlightScale = 1.2f;
        [Tooltip("Branch lines on the path become this much thicker.")]
        [SerializeField] private float pathEdgeWidthFactor = 3f;

        [Header("Everything else (the faded, 'transparent' look)")]
        [Tooltip("0 = normal colour, 1 = exactly the background colour.")]
        [Range(0f, 1f)] [SerializeField] private float fade = 0.85f;
        [Tooltip("Set this to your scene background colour so faded nodes melt into it.")]
        [SerializeField] private Color fadeToColor = new Color(0.09f, 0.34f, 0.31f);
        [Range(0f, 1f)] [SerializeField] private float fadedLabelAlpha = 0.15f;
        [Range(0f, 1f)] [SerializeField] private float fadedEdgeAlpha = 0.12f;

        private sealed class Original
        {
            public TreeNodeView Owner;
            public Color BaseColor;
            public Color Emission;
            public Color LabelColor;
            public Color EdgeColor;
            public Color EdgeLabelColor;
            public Vector3 Scale;
            public float EdgeWidth;
        }

        private readonly Dictionary<int, Original> originals = new Dictionary<int, Original>();
        private MaterialPropertyBlock block;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");   // URP
        private static readonly int ColorId = Shader.PropertyToID("_Color");           // Built-in
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            if (visualizer == null) visualizer = GetComponent<DecisionTreeVisualizer>();
        }

        /// <summary>
        /// Lights the first 'litCount' nodes of 'path' and fades all other nodes, branches and labels.
        /// litCount = path.Count shows the whole path; smaller values reveal it step by step.
        /// </summary>
        public void ShowPath(IList<TreeNode> path, int litCount)
        {
            if (!EnsureCaptured()) return;

            var lit = new HashSet<int>();
            for (int i = 0; i < path.Count && i < litCount; i++) lit.Add(path[i].Id);

            foreach (KeyValuePair<int, TreeNodeView> kv in visualizer.Views)
                Apply(kv.Key, kv.Value, true, lit.Contains(kv.Key));
        }

        /// <summary>Back to the normal look for the whole tree.</summary>
        public void Clear()
        {
            if (!EnsureCaptured()) return;

            foreach (KeyValuePair<int, TreeNodeView> kv in visualizer.Views)
                Apply(kv.Key, kv.Value, false, false);
        }

        /// <summary>Call this if you changed sizes or colours and rebuilt the tree while playing.</summary>
        public void ResetCache()
        {
            originals.Clear();
        }

        // ------------------------------------------------------------------

        private bool EnsureCaptured()
        {
            if (visualizer == null || visualizer.Views == null || visualizer.Views.Count == 0) return false;

            foreach (KeyValuePair<int, TreeNodeView> kv in visualizer.Views)
            {
                TreeNodeView v = kv.Value;
                if (v == null || v.ShapeRenderer == null) continue;

                Original old;
                if (originals.TryGetValue(kv.Key, out old) && old.Owner == v) continue;   // already known

                var o = new Original();
                o.Owner = v;
                o.BaseColor = ReadBaseColor(v.ShapeRenderer);
                o.Emission = ReadEmission(v.ShapeRenderer);
                o.Scale = v.ShapeRenderer.transform.localScale;
                if (v.Label != null) o.LabelColor = v.Label.color;
                if (v.IncomingEdge != null)
                {
                    o.EdgeColor = v.IncomingEdge.startColor;
                    o.EdgeWidth = v.IncomingEdge.widthMultiplier;
                }
                if (v.EdgeLabel != null) o.EdgeLabelColor = v.EdgeLabel.color;
                originals[kv.Key] = o;
            }

            return originals.Count > 0;
        }

        private static Color ReadBaseColor(Renderer r)
        {
            Material m = r.sharedMaterial;
            if (m == null) return Color.white;
            if (m.HasProperty(BaseColorId)) return m.GetColor(BaseColorId);
            if (m.HasProperty(ColorId)) return m.GetColor(ColorId);
            return Color.white;
        }

        private Color ReadEmission(Renderer r)
        {
            r.GetPropertyBlock(block);
            Color e = block.GetColor(EmissionId);   // leaves store their own glow here
            if (e.maxColorComponent <= 0.0001f && r.sharedMaterial != null && r.sharedMaterial.HasProperty(EmissionId))
                e = r.sharedMaterial.GetColor(EmissionId);
            return e;
        }

        private void Apply(int id, TreeNodeView v, bool highlightMode, bool onPath)
        {
            Original o;
            if (v == null || v.ShapeRenderer == null || !originals.TryGetValue(id, out o)) return;

            Color baseColor = o.BaseColor;
            Color emission = o.Emission;
            float scale = 1f;
            float widthFactor = 1f;
            float labelAlpha = 1f;
            float edgeAlpha = 1f;

            if (highlightMode)
            {
                if (onPath)
                {
                    emission = new Color(o.Emission.r * highlightGlow, o.Emission.g * highlightGlow,
                                         o.Emission.b * highlightGlow, 1f);
                    scale = highlightScale;
                    widthFactor = pathEdgeWidthFactor;
                }
                else
                {
                    baseColor = Color.Lerp(o.BaseColor, fadeToColor, fade);
                    emission = Color.black;
                    labelAlpha = fadedLabelAlpha;
                    edgeAlpha = fadedEdgeAlpha;
                }
            }

            // node shape
            block.Clear();
            block.SetColor(BaseColorId, baseColor);
            block.SetColor(ColorId, baseColor);
            block.SetColor(EmissionId, emission);
            v.ShapeRenderer.SetPropertyBlock(block);
            v.ShapeRenderer.transform.localScale = o.Scale * scale;

            // text under / above the node
            if (v.Label != null) v.Label.color = WithAlpha(o.LabelColor, labelAlpha);

            // the branch that leads INTO this node
            if (v.IncomingEdge != null)
            {
                Color ec = WithAlpha(o.EdgeColor, edgeAlpha);
                v.IncomingEdge.startColor = ec;
                v.IncomingEdge.endColor = ec;
                v.IncomingEdge.widthMultiplier = o.EdgeWidth * widthFactor;
            }
            if (v.EdgeLabel != null) v.EdgeLabel.color = WithAlpha(o.EdgeLabelColor, edgeAlpha > 0.5f ? 1f : labelAlpha);
        }

        private static Color WithAlpha(Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}