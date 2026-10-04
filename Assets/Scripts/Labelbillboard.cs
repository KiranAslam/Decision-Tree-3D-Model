using UnityEngine;

namespace DecisionTreeViz
{
    /// <summary>Keeps a label facing the camera. Runs after the camera controller moves the camera.</summary>
    [DefaultExecutionOrder(1000)]
    public class LabelBillboard : MonoBehaviour
    {
        public Camera targetCamera;

        private void LateUpdate()
        {
            Camera cam = targetCamera != null ? targetCamera : Camera.main;
            if (cam == null) return;
            transform.rotation = cam.transform.rotation;
        }
    }
}