using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DecisionTreeViz
{
    public enum NodeKind
    {
        Split,
        Leaf
    }

    /// <summary>
    /// One node of the trained sklearn DecisionTreeClassifier, exactly as stored in heart_disease_tree.json.
    /// Pure data - no GameObjects here.
    /// </summary>
    public sealed class TreeNode
    {
        // ---- from JSON ----
        public int Id;
        public NodeKind Kind;
        public int Samples;               // raw training samples reaching this node
        public double WeightedSamples;    // after class_weight="balanced"
        public double Impurity;           // gini
        public double[] ClassShare;       // JSON "class_counts": WEIGHTED class proportions, NOT raw counts

        // split only
        public string Feature;
        public int FeatureIndex;
        public double Threshold;          // rule: feature <= Threshold  -> Left, else Right

        // leaf only
        public int Prediction;
        public string PredictionLabel;
        public bool IsTie;                // leaf: both classes have exactly the same share

        // readable text written by train_model.py (null when the JSON does not have it)
        public string FeatureLabel;       // e.g. "Chest pain type"
        public string LeftText;           // e.g. "Chest pain type: Asymptomatic"          (feature <= threshold)
        public string RightText;          // e.g. "Chest pain type: Atypical angina / ..." (feature >  threshold)

        // ---- structure ----
        public TreeNode Left;
        public TreeNode Right;
        public TreeNode Parent;
        public bool IsLeftChild;
        public int Depth;

        // ---- filled by TreeLayout ----
        public Vector3 Position;

        public bool IsLeaf { get { return Kind == NodeKind.Leaf; } }

        /// <summary>Share of the winning class (0.5 = coin flip, 1.0 = pure).</summary>
        public double Confidence
        {
            get
            {
                if (ClassShare == null || ClassShare.Length == 0) return 0.0;
                double sum = 0.0, max = 0.0;
                for (int k = 0; k < ClassShare.Length; k++)
                {
                    sum += ClassShare[k];
                    if (ClassShare[k] > max) max = ClassShare[k];
                }
                return sum > 0.0 ? max / sum : 0.0;
            }
        }

        public string SplitLabel
        {
            get { return Feature + " <= " + FormatNumber(Threshold); }
        }

        public string LeafLabel
        {
            get { return PredictionLabel; }
        }

        /// <summary>Removes float32 noise: 3.549999952316284 -> "3.55".</summary>
        public static string FormatNumber(double value)
        {
            return Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
        }

        public override string ToString()
        {
            return IsLeaf
                ? "Leaf #" + Id + " -> " + PredictionLabel + " (n=" + Samples + ")"
                : "Split #" + Id + ": " + SplitLabel + " (n=" + Samples + ")";
        }
    }

    /// <summary>The whole trained tree + its metadata + a flat list of all nodes (pre-order).</summary>
    public sealed class TreeModel
    {
        public string ModelType;
        public string Task;
        public string Criterion;
        public int MaxDepthConfigured;
        public int ActualDepthDeclared;
        public int LeafCountDeclared;
        public List<string> SelectedFeatures = new List<string>();

        public TreeNode Root;
        public List<TreeNode> AllNodes = new List<TreeNode>();

        // computed while parsing
        public int SplitCount;
        public int LeafCount;
        public int MaxDepthComputed;
        public Dictionary<string, int> FeatureUsage = new Dictionary<string, int>();
        public List<string> Warnings = new List<string>();
    }
}