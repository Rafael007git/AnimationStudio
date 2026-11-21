using UnityEngine;

namespace AnimationStudio
{
    public class AnchorGizmoMarker
    {
        private GameObject _rootObj;
        private LineRenderer _crossX;
        private LineRenderer _crossZ;
        private LineRenderer _verticalPin;
        private LineRenderer _arrowHead;

        public bool IsVisible => _rootObj != null && _rootObj.activeSelf;

        public void UpdateTransform(Vector3 worldPos, Quaternion worldRot, bool visible)
        {
            if (!visible)
            {
                if (_rootObj != null && _rootObj.activeSelf)
                    _rootObj.SetActive(false);
                return;
            }

            EnsureCreated();

            if (!_rootObj.activeSelf)
                _rootObj.SetActive(true);

            // Slightly elevated above floor/Animation Grid to prevent Z-fighting
            Vector3 basePos = worldPos + Vector3.up * 0.005f;
            Vector3 right = worldRot * Vector3.right;
            Vector3 forward = worldRot * Vector3.forward;

            // Cross X axis (0.6m total width)
            _crossX.SetPosition(0, basePos - right * 0.30f);
            _crossX.SetPosition(1, basePos + right * 0.30f);

            // Cross Z axis + Forward ray (-0.30m back, +0.45m forward)
            Vector3 tip = basePos + forward * 0.45f;
            _crossZ.SetPosition(0, basePos - forward * 0.30f);
            _crossZ.SetPosition(1, tip);

            // Arrowhead V-shape at forward tip
            _arrowHead.SetPosition(0, tip - forward * 0.09f - right * 0.06f);
            _arrowHead.SetPosition(1, tip);
            _arrowHead.SetPosition(2, tip - forward * 0.09f + right * 0.06f);

            // Small vertical center pin (6cm)
            _verticalPin.SetPosition(0, basePos);
            _verticalPin.SetPosition(1, basePos + Vector3.up * 0.06f);
        }

        public void Destroy()
        {
            if (_rootObj != null)
            {
                Object.Destroy(_rootObj);
                _rootObj = null;
            }
        }

        private void EnsureCreated()
        {
            if (_rootObj != null) return;

            _rootObj = new GameObject("AnimationStudio_AnchorMarker");
            Object.DontDestroyOnLoad(_rootObj);

            Shader shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
            Material mat = new Material(shader);
            mat.color = new Color(0.15f, 0.95f, 0.85f, 0.90f); // Cyan-mint high contrast

            _crossX = CreateLine("CrossX", mat, 2, 0.006f);
            _crossZ = CreateLine("CrossZ", mat, 2, 0.006f);
            _arrowHead = CreateLine("ArrowHead", mat, 3, 0.006f);
            _verticalPin = CreateLine("VerticalPin", mat, 2, 0.004f);
        }

        private LineRenderer CreateLine(string name, Material mat, int points, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_rootObj.transform, false);

            var lr = go.AddComponent<LineRenderer>();
            lr.material = mat;
            lr.positionCount = points;
            lr.startWidth = width;
            lr.endWidth = width;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }
    }
}
