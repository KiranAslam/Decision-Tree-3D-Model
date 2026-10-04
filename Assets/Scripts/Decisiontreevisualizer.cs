using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;

namespace DecisionTreeViz
{
    /// <summary>
    /// Attach to: DT_Model / NetworkOrigin / DecisionTreeVisualizer
    /// Reads Assets/Resources/heart_disease_tree.json, lays the tree out recursively and builds:
    /// diamond split nodes, diamond (or cube) leaf nodes, branch lines and text labels.
    ///
    /// NOTE: do NOT rename this class to something ending in "Generator" -
    /// SceneSwitcher.DisableRuntimeGenerators() disables every such script.
    /// </summary>
    public class DecisionTreeVisualizer : MonoBehaviour
    {
        public enum LeafShape { Diamond, Cube }

        [Header("Data (file must be in Assets/Resources, name without .json)")]
        [SerializeField] private string treeResourceName = "heart_disease_tree";
        [SerializeField] private bool buildOnStart = true;
        [Tooltip("Optional. If empty, Camera.main or the IntegratedCameraController camera is used.")]
        [SerializeField] private Camera labelCamera;

        [Header("Layout (units)")]
        [SerializeField] private float levelHeight = 2.4f;
        [SerializeField] private float leafSpacing = 1.8f;
        [SerializeField] private float zStep = 0.35f;
        [Range(0f, 0.9f)]
        [SerializeField] private float zDamping = 0.5f;
        [Tooltip("Scales the whole tree. Use this if your camera is tuned for a much bigger scene.")]
        [SerializeField] private float worldScale = 1f;

        [Header("Nodes")]
        [SerializeField] private bool scaleBySamples = true;
        [SerializeField] private Vector2 splitSizeRange = new Vector2(0.30f, 0.65f);
        [SerializeField] private Vector2 leafSizeRange = new Vector2(0.35f, 0.80f);
        [Tooltip("Shape of the leaf nodes. The colour (green / red) tells the prediction.")]
        [SerializeField] private LeafShape leafShape = LeafShape.Diamond;
        [Tooltip("Width of the leaf diamond. 0.75 = same as the split diamonds, bigger = fatter.")]
        [SerializeField] private float leafDiamondRadius = 0.95f;
        [Tooltip("Optional material to copy (e.g. a URP Lit). If empty, a pipeline-appropriate shader is found automatically.")]
        [SerializeField] private Material materialTemplate;
        [SerializeField] private Color splitColor = new Color(0.81f, 0.92f, 0.88f);
        [SerializeField] private Color noDiseaseColor = new Color(0.25f, 0.81f, 0.56f);
        [SerializeField] private Color diseaseColor = new Color(0.90f, 0.28f, 0.30f);
        [SerializeField] private float splitEmission = 0.35f;
        [SerializeField] private Vector2 leafEmissionRange = new Vector2(0.45f, 1.0f);

        [Header("Branches")]
        [SerializeField] private float edgeWidth = 0.05f;
        [SerializeField] private Color leftEdgeColor = new Color(0.36f, 0.66f, 0.91f);
        [SerializeField] private Color rightEdgeColor = new Color(0.91f, 0.71f, 0.30f);

        [Header("Labels")]
        [SerializeField] private bool showLabels = true;
        [Tooltip("Show sample count and confidence under the leaf label. Off = only the predicted class name.")]
        [SerializeField] private bool showLeafDetails = false;
        [SerializeField] private bool showEdgeLabels = true;
        [Tooltip("On: show names (\"Chest pain type\" / \"Asymptomatic\"). Off, or JSON without names: show \"cp <= 0.5\".")]
        [SerializeField] private bool useReadableLabels = true;
        [SerializeField] private TMP_FontAsset labelFont;
        [SerializeField] private float nodeFontSize = 2.4f;
        [SerializeField] private float edgeFontSize = 1.8f;

        // ---- public read-only access (for the interaction layer later) ----
        public TreeModel Model { get; private set; }
        public Dictionary<int, TreeNodeView> Views { get; private set; }
        public Bounds LocalBounds { get; private set; }

        private Transform treeRoot;
        private bool built;
        private Mesh diamondMesh, leafMesh;
        private Material splitMat, noDiseaseMat, diseaseMat, lineMat;

        private const string RootName = "TreeRoot";

        private void Awake()
        {
            Views = new Dictionary<int, TreeNodeView>();
        }

        private void Start()
        {
            // DT_Model starts inactive (SceneSwitcher), so Start runs the first time it is shown.
            if (buildOnStart && !built) Build();
        }

        // ================================================================== build

        [ContextMenu("Rebuild Tree")]
        public void Build()
        {
            if (Views == null) Views = new Dictionary<int, TreeNodeView>();
            Clear();

            TextAsset asset = Resources.Load<TextAsset>(treeResourceName);
            if (asset == null)
            {
                Debug.LogError("[DecisionTreeVisualizer] Resources/" + treeResourceName + ".json not found.", this);
                return;
            }

            try
            {
                Model = TreeParser.Parse(asset.text);
            }
            catch (Exception e)
            {
                Debug.LogError("[DecisionTreeVisualizer] Failed to parse tree JSON: " + e.Message, this);
                return;
            }

            foreach (string w in Model.Warnings)
                Debug.LogWarning("[DecisionTreeVisualizer] " + w, this);

            LocalBounds = TreeLayout.Compute(Model, levelHeight, leafSpacing, zStep, zDamping);

            ResolveCamera();
            CreateMaterials();
            diamondMesh = MeshFactory.CreateDiamond();
            leafMesh = MeshFactory.CreateDiamond(leafDiamondRadius);

            treeRoot = new GameObject(RootName).transform;
            treeRoot.SetParent(transform, false);
            treeRoot.localScale = Vector3.one * worldScale;

            foreach (TreeNode n in Model.AllNodes)
                CreateNodeVisual(n);

            foreach (TreeNode n in Model.AllNodes)
                if (n.Parent != null) CreateEdge(n);

            built = true;
            Debug.Log("[DecisionTreeVisualizer] Built " + Model.AllNodes.Count + " nodes (" +
                      Model.SplitCount + " splits, " + Model.LeafCount + " leaves), depth " +
                      Model.MaxDepthComputed + ".", this);
        }

        public void Clear()
        {
            Transform existing = transform.Find(RootName);
            if (existing != null)
            {
                if (Application.isPlaying) Destroy(existing.gameObject);
                else DestroyImmediate(existing.gameObject);
            }

            treeRoot = null;
            if (Views != null) Views.Clear();
            built = false;
        }

        // ================================================================== nodes

        private void CreateNodeVisual(TreeNode n)
        {
            var go = new GameObject(n.IsLeaf ? "Leaf_" + n.Id : "Split_" + n.Id + "_" + n.Feature);
            go.transform.SetParent(treeRoot, false);
            go.transform.localPosition = n.Position;

            float t = scaleBySamples
                ? Mathf.Sqrt((float)n.Samples / Mathf.Max(1, Model.Root.Samples))
                : 0.5f;

            GameObject shape;
            float size;
            float leafLabelDrop = 0f;

            if (n.IsLeaf)
            {
                size = Mathf.Lerp(leafSizeRange.x, leafSizeRange.y, t);
                bool isDisease = n.Prediction == 1;
                Renderer r;

                if (leafShape == LeafShape.Diamond)
                {
                    shape = new GameObject("Shape");
                    shape.transform.SetParent(go.transform, false);
                    shape.AddComponent<MeshFilter>().sharedMesh = leafMesh;
                    r = shape.AddComponent<MeshRenderer>();
                    shape.transform.localScale = Vector3.one * size;
                    shape.AddComponent<BoxCollider>();       // fits the mesh automatically
                    leafLabelDrop = size + 0.45f;            // diamond half-height = size
                }
                else
                {
                    shape = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    shape.name = "Shape";
                    shape.transform.SetParent(go.transform, false);
                    shape.transform.localScale = new Vector3(size * 1.3f, size, size);
                    r = shape.GetComponent<Renderer>();
                    leafLabelDrop = size * 0.5f + 0.55f;
                }

                r.sharedMaterial = isDisease ? diseaseMat : noDiseaseMat;

                // brighter glow = more confident leaf (near-ties glow less)
                float k = Mathf.Lerp(leafEmissionRange.x, leafEmissionRange.y,
                                     Mathf.InverseLerp(0.5f, 1f, (float)n.Confidence));
                Color baseC = Opaque(isDisease ? diseaseColor : noDiseaseColor);
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", baseC);   // URP
                block.SetColor("_Color", baseC);       // Built-in
                block.SetColor("_EmissionColor", new Color(baseC.r * k, baseC.g * k, baseC.b * k, 1f));
                r.SetPropertyBlock(block);
                StyleRenderer(r);
            }
            else
            {
                size = Mathf.Lerp(splitSizeRange.x, splitSizeRange.y, t);
                shape = new GameObject("Shape");
                shape.transform.SetParent(go.transform, false);
                shape.AddComponent<MeshFilter>().sharedMesh = diamondMesh;
                Renderer r = shape.AddComponent<MeshRenderer>();
                r.sharedMaterial = splitMat;
                shape.transform.localScale = Vector3.one * size;
                shape.AddComponent<BoxCollider>(); // fits the mesh automatically
                StyleRenderer(r);
            }

            var view = go.AddComponent<TreeNodeView>();
            view.Node = n;
            view.ShapeRenderer = shape.GetComponent<Renderer>();

            if (showLabels)
            {
                if (n.IsLeaf)
                {
                    string text = showLeafDetails
                        ? n.LeafLabel + "\nn=" + n.Samples + ", " +
                          Mathf.RoundToInt((float)n.Confidence * 100f) + "%" +
                          (n.IsTie ? " (tie)" : "")
                        : n.LeafLabel;
                    view.Label = CreateLabel(text, go.transform, new Vector3(0f, -leafLabelDrop, 0f),
                                             nodeFontSize, n.Prediction == 1 ? diseaseColor : noDiseaseColor);
                }
                else
                {
                    string splitText = UseNames(n) ? n.FeatureLabel : n.SplitLabel;
                    view.Label = CreateLabel(splitText, go.transform, new Vector3(0f, size + 0.3f, 0f),
                                             nodeFontSize, Color.white);
                }
            }

            Views[n.Id] = view;
        }

        // ================================================================== edges

        private void CreateEdge(TreeNode child)
        {
            TreeNode parent = child.Parent;

            var go = new GameObject("Edge_" + parent.Id + "_to_" + child.Id);
            go.transform.SetParent(treeRoot, false);

            Color c = child.IsLeftChild ? leftEdgeColor : rightEdgeColor;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace = false;
            lr.positionCount = 2;
            lr.SetPosition(0, parent.Position);
            lr.SetPosition(1, child.Position);
            lr.widthMultiplier = edgeWidth;
            lr.sharedMaterial = lineMat;
            lr.startColor = c;
            lr.endColor = c;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;

            TreeNodeView childView;
            if (Views.TryGetValue(child.Id, out childView)) childView.IncomingEdge = lr;

            if (showLabels && showEdgeLabels)
            {
                // left = condition true ("<="), right = condition false (">")
                Vector3 mid = Vector3.Lerp(parent.Position, child.Position, 0.45f);
                string edgeText = UseNames(parent)
                    ? EdgeText(child)
                    : (child.IsLeftChild ? "<=" : ">");
                // push the label sideways by half its width so it does not sit on the line
                float halfWidth = 0.025f * edgeText.Length * edgeFontSize;
                mid.x += (child.IsLeftChild ? -1f : 1f) * (halfWidth + 0.15f);
                TextMeshPro edgeLabel = CreateLabel(edgeText, treeRoot, mid, edgeFontSize, c);
                if (childView != null) childView.EdgeLabel = edgeLabel;
            }
        }

        // ================================================================== helpers

        private TextMeshPro CreateLabel(string text, Transform parent, Vector3 localPos, float fontSize, Color color)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var tmp = go.AddComponent<TextMeshPro>();
            if (labelFont != null) tmp.font = labelFont;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.rectTransform.sizeDelta = new Vector2(12f, 3f);

            go.AddComponent<LabelBillboard>().targetCamera = labelCamera;
            return tmp;
        }

        private bool UseNames(TreeNode split)
        {
            return useReadableLabels && !string.IsNullOrEmpty(split.FeatureLabel)
                   && !string.IsNullOrEmpty(split.LeftText) && !string.IsNullOrEmpty(split.RightText);
        }

        /// <summary>
        /// Short readable text for the branch that leads to 'child'.
        /// Categorical: Asymptomatic or Not Reversible defect. Numeric: less-or-equal 142.5 bpm.
        /// </summary>
        private static string EdgeText(TreeNode child)
        {
            TreeNode p = child.Parent;
            string mine = child.IsLeftChild ? p.LeftText : p.RightText;
            string other = child.IsLeftChild ? p.RightText : p.LeftText;
            string prefix = p.FeatureLabel + ": ";
            string[] sep = { " / " };

            if (mine.StartsWith(prefix, StringComparison.Ordinal))
            {
                string[] mineNames = mine.Substring(prefix.Length).Split(sep, StringSplitOptions.None);
                string[] otherNames = other.StartsWith(prefix, StringComparison.Ordinal)
                    ? other.Substring(prefix.Length).Split(sep, StringSplitOptions.None)
                    : new string[0];

                if (mineNames.Length == 1) return mineNames[0];
                if (otherNames.Length == 1) return "Not " + otherNames[0];
                return string.Join(" / ", mineNames);
            }

            // numeric split: "Max heart rate <= 142.5 bpm" -> "<= 142.5 bpm"
            string numPrefix = p.FeatureLabel + " ";
            return mine.StartsWith(numPrefix, StringComparison.Ordinal) ? mine.Substring(numPrefix.Length) : mine;
        }

        private static Color Opaque(Color c)
        {
            c.a = 1f;   // an accidental alpha of 0 in the Inspector must never make a node invisible
            return c;
        }

        private static void StyleRenderer(Renderer r)
        {
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private void CreateMaterials()
        {
            splitMat = MakeMaterial(splitColor, splitEmission);
            noDiseaseMat = MakeMaterial(noDiseaseColor, leafEmissionRange.y);
            diseaseMat = MakeMaterial(diseaseColor, leafEmissionRange.y);

            Shader lineShader = Shader.Find("Sprites/Default");
            lineMat = new Material(lineShader != null ? lineShader : Shader.Find("Unlit/Color"));
        }

        /// <summary>Works with URP/Lit, Built-in/Standard, or a custom template material.</summary>
        private Material MakeMaterial(Color baseColor, float emissionStrength)
        {
            baseColor = Opaque(baseColor);
            Material m;
            if (materialTemplate != null)
            {
                m = new Material(materialTemplate);
            }
            else
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Lit");
                if (sh == null) sh = Shader.Find("Standard");
                if (sh == null) sh = Shader.Find("Unlit/Color");
                m = new Material(sh);
            }

            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", baseColor);
            if (m.HasProperty("_Color")) m.SetColor("_Color", baseColor);

            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                m.SetColor("_EmissionColor", new Color(baseColor.r * emissionStrength,
                                                       baseColor.g * emissionStrength,
                                                       baseColor.b * emissionStrength, 1f));
            }

            return m;
        }

        private void ResolveCamera()
        {
            if (labelCamera != null) return;
            if (Camera.main != null) { labelCamera = Camera.main; return; }

#if UNITY_2023_1_OR_NEWER
            var ctrl = FindFirstObjectByType<IntegratedCameraController>();
#else
            var ctrl = FindObjectOfType<IntegratedCameraController>();
#endif
            if (ctrl != null) labelCamera = ctrl.GetComponent<Camera>();
        }
    }
}