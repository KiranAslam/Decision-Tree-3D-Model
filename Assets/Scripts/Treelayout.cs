using UnityEngine;

namespace DecisionTreeViz
{
    /// <summary>
    /// Layer 4 - assigns a position to every node by recursive traversal.
    ///   Y : -depth * levelHeight            (root at top)
    ///   X : leaves take consecutive slots (left to right), each split sits at the midpoint of its children
    ///   Z : small deterministic offset (left child goes back, right child comes forward, damped per level)
    /// Result is centered on (0,0,0) so the camera can orbit the tree's middle.
    /// </summary>
    public static class TreeLayout
    {
        private sealed class Context
        {
            public int LeafCursor;
            public float LeafSpacing;
            public float LevelHeight;
            public float ZStep;
            public float ZDamping;
        }

        /// <returns>Bounds of node centers (local space, centered on origin).</returns>
        public static Bounds Compute(TreeModel model, float levelHeight, float leafSpacing, float zStep, float zDamping)
        {
            var ctx = new Context
            {
                LeafCursor = 0,
                LeafSpacing = leafSpacing,
                LevelHeight = levelHeight,
                ZStep = zStep,
                ZDamping = zDamping
            };

            Place(model.Root, 0f, ctx);

            var bounds = new Bounds(model.Root.Position, Vector3.zero);
            foreach (TreeNode n in model.AllNodes)
                bounds.Encapsulate(n.Position);

            Vector3 shift = bounds.center;
            foreach (TreeNode n in model.AllNodes)
                n.Position -= shift;

            bounds.center = Vector3.zero;
            return bounds;
        }

        private static void Place(TreeNode n, float parentZ, Context c)
        {
            float z = n.Parent == null
                ? 0f
                : parentZ * c.ZDamping + (n.IsLeftChild ? -c.ZStep : c.ZStep);
            float y = -n.Depth * c.LevelHeight;
            float x;

            if (n.IsLeaf)
            {
                x = c.LeafCursor * c.LeafSpacing;
                c.LeafCursor++;
            }
            else
            {
                Place(n.Left, z, c);
                Place(n.Right, z, c);
                x = (n.Left.Position.x + n.Right.Position.x) * 0.5f;
            }

            n.Position = new Vector3(x, y, z);
        }
    }
}