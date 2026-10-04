using System;
using System.Collections.Generic;

namespace DecisionTreeViz
{
    /// <summary>
    /// Layer 1 - reads heart_disease_tree.json and builds the TreeNode graph.
    /// Nothing is hardcoded: structure, features, thresholds and predictions all come from the file.
    /// </summary>
    public static class TreeParser
    {
        public static TreeModel Parse(string json)
        {
            var rootDict = MiniJson.Parse(json) as Dictionary<string, object>;
            if (rootDict == null)
                throw new FormatException("Tree JSON root must be an object.");

            var model = new TreeModel();
            model.ModelType = GetString(rootDict, "model_type", "");
            model.Task = GetString(rootDict, "task", "");
            model.Criterion = GetString(rootDict, "criterion", "");
            model.MaxDepthConfigured = GetInt(rootDict, "max_depth", -1);
            model.ActualDepthDeclared = GetInt(rootDict, "actual_depth", -1);
            model.LeafCountDeclared = GetInt(rootDict, "n_leaves", -1);

            var features = rootDict.ContainsKey("selected_features") ? rootDict["selected_features"] as List<object> : null;
            if (features != null)
                foreach (object f in features)
                    model.SelectedFeatures.Add(Convert.ToString(f));

            Dictionary<string, object> nodeDict = GetDict(rootDict, "root");
            if (nodeDict == null)
                throw new FormatException("Tree JSON has no 'root' object.");

            model.Root = ReadNode(nodeDict, null, false, 0, model);
            Validate(model);
            return model;
        }

        // ------------------------------------------------------------------ recursion

        private static TreeNode ReadNode(Dictionary<string, object> d, TreeNode parent, bool isLeft, int depth, TreeModel model)
        {
            var n = new TreeNode();
            n.Id = GetInt(d, "node_id", -1);
            n.Parent = parent;
            n.IsLeftChild = isLeft;
            n.Depth = depth;
            n.Samples = GetInt(d, "samples", 0);
            n.WeightedSamples = GetDouble(d, "weighted_samples", 0.0);
            n.Impurity = GetDouble(d, "impurity", 0.0);
            n.ClassShare = GetDoubleArray(d, "class_counts");

            model.AllNodes.Add(n); // pre-order
            if (depth > model.MaxDepthComputed) model.MaxDepthComputed = depth;

            string type = GetString(d, "node_type", "");
            if (type == "split")
            {
                n.Kind = NodeKind.Split;
                n.Feature = GetString(d, "feature", "?");
                n.FeatureIndex = GetInt(d, "feature_index", -1);
                n.Threshold = GetDouble(d, "threshold", 0.0);
                n.FeatureLabel = GetString(d, "feature_label", null);
                n.LeftText = GetString(d, "left_text", null);
                n.RightText = GetString(d, "right_text", null);
                model.SplitCount++;

                int used;
                model.FeatureUsage.TryGetValue(n.Feature, out used);
                model.FeatureUsage[n.Feature] = used + 1;

                Dictionary<string, object> l = GetDict(d, "left");
                Dictionary<string, object> r = GetDict(d, "right");
                if (l == null || r == null)
                    throw new FormatException("Split node " + n.Id + " is missing a left or right child.");

                n.Left = ReadNode(l, n, true, depth + 1, model);
                n.Right = ReadNode(r, n, false, depth + 1, model);
            }
            else if (type == "leaf")
            {
                n.Kind = NodeKind.Leaf;
                n.Prediction = GetInt(d, "prediction", 0);
                n.PredictionLabel = GetString(d, "prediction_label", "Class " + n.Prediction);
                n.IsTie = GetBool(d, "is_tie", false);
                model.LeafCount++;
            }
            else
            {
                throw new FormatException("Node " + n.Id + " has unknown node_type '" + type + "'.");
            }

            return n;
        }

        // ------------------------------------------------------------------ validation

        private static void Validate(TreeModel m)
        {
            var ids = new HashSet<int>();
            int leafSamples = 0;

            foreach (TreeNode n in m.AllNodes)
            {
                if (!ids.Add(n.Id)) m.Warnings.Add("Duplicate node_id " + n.Id);
                if (n.IsLeaf) leafSamples += n.Samples;
                if (!n.IsLeaf && m.SelectedFeatures.Count > 0 && !m.SelectedFeatures.Contains(n.Feature))
                    m.Warnings.Add("Node " + n.Id + " splits on '" + n.Feature + "' which is not in selected_features.");
                if (n.ClassShare == null || n.ClassShare.Length < 2)
                    m.Warnings.Add("Node " + n.Id + " has no usable class_counts.");
            }

            if (m.LeafCountDeclared >= 0 && m.LeafCountDeclared != m.LeafCount)
                m.Warnings.Add("Declared n_leaves=" + m.LeafCountDeclared + " but parsed " + m.LeafCount + ".");
            if (m.ActualDepthDeclared >= 0 && m.ActualDepthDeclared != m.MaxDepthComputed)
                m.Warnings.Add("Declared actual_depth=" + m.ActualDepthDeclared + " but parsed " + m.MaxDepthComputed + ".");
            if (leafSamples != m.Root.Samples)
                m.Warnings.Add("Leaf samples sum to " + leafSamples + " but root has " + m.Root.Samples + ".");
        }

        // ------------------------------------------------------------------ small helpers

        private static string GetString(Dictionary<string, object> d, string key, string fallback)
        {
            object v;
            return d.TryGetValue(key, out v) && v != null ? Convert.ToString(v) : fallback;
        }

        private static bool GetBool(Dictionary<string, object> d, string key, bool fallback)
        {
            object v;
            return d.TryGetValue(key, out v) && v is bool ? (bool)v : fallback;
        }

        private static double GetDouble(Dictionary<string, object> d, string key, double fallback)
        {
            object v;
            return d.TryGetValue(key, out v) && v is double ? (double)v : fallback;
        }

        private static int GetInt(Dictionary<string, object> d, string key, int fallback)
        {
            object v;
            return d.TryGetValue(key, out v) && v is double ? (int)Math.Round((double)v) : fallback;
        }

        private static Dictionary<string, object> GetDict(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        private static double[] GetDoubleArray(Dictionary<string, object> d, string key)
        {
            object v;
            if (!d.TryGetValue(key, out v)) return null;
            var list = v as List<object>;
            if (list == null) return null;

            var result = new double[list.Count];
            for (int k = 0; k < list.Count; k++)
                result[k] = list[k] is double ? (double)list[k] : 0.0;
            return result;
        }
    }
}