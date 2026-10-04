using TMPro;
using UnityEngine;

namespace DecisionTreeViz
{
    /// <summary>
    /// Added automatically to every node GameObject. Links the 3D object back to its TreeNode data,
    /// so the interaction system (hover / click / info panel) can ask "which node is this?".
    /// </summary>
    public class TreeNodeView : MonoBehaviour
    {
        public TreeNode Node;
        public Renderer ShapeRenderer;
        public TextMeshPro Label;
        public LineRenderer IncomingEdge;   // edge coming from the parent (null for root)
        public TextMeshPro EdgeLabel;       // text on that edge (null for root or when edge labels are off)
    }
}