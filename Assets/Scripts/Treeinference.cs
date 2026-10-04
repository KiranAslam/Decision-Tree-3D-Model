using System.Collections.Generic;

namespace DecisionTreeViz
{
    /// <summary>
    /// Walks the real trained tree for one patient and returns the path it takes.
    /// No GameObjects, no UI - pure logic, so it is easy to unit test.
    /// </summary>
    public static class TreeInference
    {
        public sealed class Step
        {
            public TreeNode Node;      // the split that was evaluated
            public double Value;       // the patient's value for Node.Feature
            public bool WentLeft;      // true when Value <= Threshold
        }

        public sealed class Result
        {
            public List<TreeNode> Path = new List<TreeNode>();   // root ... leaf (every node on the way)
            public List<Step> Steps = new List<Step>();          // one entry per split
            public TreeNode Leaf;
            public string Error;

            public bool Ok { get { return Error == null && Leaf != null; } }
        }

        public static Result Predict(TreeNode root, IDictionary<string, double> values)
        {
            var result = new Result();
            TreeNode n = root;

            while (n != null)
            {
                result.Path.Add(n);

                if (n.IsLeaf)
                {
                    result.Leaf = n;
                    break;
                }

                double v;
                if (!values.TryGetValue(n.Feature, out v))
                {
                    result.Error = "No value for feature '" + n.Feature + "'.";
                    return result;
                }

                // scikit-learn casts the input to float32 before comparing with the threshold
                bool goLeft = (double)(float)v <= n.Threshold;

                result.Steps.Add(new Step { Node = n, Value = v, WentLeft = goLeft });
                n = goLeft ? n.Left : n.Right;
            }

            return result;
        }
    }
}