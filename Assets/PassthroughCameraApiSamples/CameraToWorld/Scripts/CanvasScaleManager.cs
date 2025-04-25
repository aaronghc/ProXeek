// Copyright (c) Meta Platforms, Inc. and affiliates.

using System.Collections.Generic;
using Meta.XR.Samples;
using UnityEngine;

namespace PassthroughCameraSamples.CameraToWorld
{
    [MetaCodeSample("PassthroughCameraApiSamples-CameraToWorld")]
    public class CanvasScaleManager : MonoBehaviour
    {
        [Header("Scale Limits")]
        [SerializeField, Range(0.1f, 1.0f)] private float m_minScale = 0.5f;
        [SerializeField, Range(1.0f, 2.0f)] private float m_maxScale = 1.5f;
        [SerializeField] private float m_scaleSpeed = 2.0f;

        [Header("Initial Scale Settings")]
        [SerializeField, Range(0.1f, 2.0f)] private float m_initialLiveCanvasScale = 1.0f;
        [SerializeField, Range(0.1f, 2.0f)] private float m_initialSnapshotCanvasScale = 0.5f;

        //[Header("Canvas Types")]
        [SerializeField] private bool m_affectSnapshotCanvases = true;
        [SerializeField] private bool m_affectLiveCanvas = true;

        private List<Transform> m_canvasesToScale = new List<Transform>();
        private Dictionary<Transform, Vector3> m_originalScales = new Dictionary<Transform, Vector3>();
        private Dictionary<Transform, Vector3> m_initialScales = new Dictionary<Transform, Vector3>();
        private Dictionary<Transform, bool> m_isSnapshotCanvas = new Dictionary<Transform, bool>();
        private Dictionary<Transform, float> m_currentScaleFactors = new Dictionary<Transform, float>();

        private void Start()
        {
            // Find the live camera canvas
            CameraToWorldCameraCanvas liveCanvas = FindObjectOfType<CameraToWorldCameraCanvas>();
            if (liveCanvas != null)
            {
                RegisterCanvas(liveCanvas.transform, false);
            }

            // Find snapshot manager to register for new snapshots
            SnapshotManager snapshotManager = FindObjectOfType<SnapshotManager>();
            if (snapshotManager != null)
            {
                // Register for snapshot creation events
                snapshotManager.OnSnapshotCreated += (transform) => RegisterCanvas(transform, true);
            }
        }

        private void Update()
        {
            // Get right thumbstick vertical input
            float thumbstickY = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick).y;

            // Only process if there's significant input
            if (Mathf.Abs(thumbstickY) > 0.2f)
            {
                ScaleAllCanvases(thumbstickY);
            }
        }

        private void ScaleAllCanvases(float direction)
        {
            foreach (Transform canvas in m_canvasesToScale)
            {
                if (canvas == null) continue;

                bool isSnapshot = m_isSnapshotCanvas[canvas];

                // Skip canvases that shouldn't be affected based on settings
                if ((isSnapshot && !m_affectSnapshotCanvases) ||
                    (!isSnapshot && !m_affectLiveCanvas))
                    continue;

                // Get the initial scale that was applied at registration
                Vector3 initialScale = m_initialScales[canvas];

                // Get current scale factor relative to initial scale
                float currentFactor = m_currentScaleFactors[canvas];

                // Update the scale factor
                currentFactor += direction * Time.deltaTime * m_scaleSpeed;

                // Apply min/max constraints relative to initial scale
                currentFactor = Mathf.Clamp(currentFactor, m_minScale, m_maxScale);

                // Store the updated scale factor
                m_currentScaleFactors[canvas] = currentFactor;

                // Apply new scale
                canvas.localScale = initialScale * currentFactor;
            }
        }

        public void RegisterCanvas(Transform canvasTransform, bool isSnapshot)
        {
            if (!m_canvasesToScale.Contains(canvasTransform))
            {
                // Store the original scale before we modify it
                m_originalScales[canvasTransform] = canvasTransform.localScale;
                m_isSnapshotCanvas[canvasTransform] = isSnapshot;

                // Calculate initial scale based on canvas type
                float initialScaleFactor = isSnapshot ? m_initialSnapshotCanvasScale : m_initialLiveCanvasScale;
                Vector3 initialScale = m_originalScales[canvasTransform] * initialScaleFactor;

                // Store the initial scale
                m_initialScales[canvasTransform] = initialScale;

                // Start with scale factor of 1.0 (100% of initial scale)
                m_currentScaleFactors[canvasTransform] = 1.0f;

                // Apply initial scale
                canvasTransform.localScale = initialScale;

                // Add to our tracking list
                m_canvasesToScale.Add(canvasTransform);

                Debug.Log($"PCA: Registered {(isSnapshot ? "snapshot" : "live")} canvas for scaling: {canvasTransform.name} with initial scale {initialScaleFactor}");
            }
        }

        public void UnregisterCanvas(Transform canvasTransform)
        {
            if (m_canvasesToScale.Contains(canvasTransform))
            {
                m_canvasesToScale.Remove(canvasTransform);
                m_originalScales.Remove(canvasTransform);
                m_initialScales.Remove(canvasTransform);
                m_isSnapshotCanvas.Remove(canvasTransform);
                m_currentScaleFactors.Remove(canvasTransform);
            }
        }

        public void ResetCanvasToInitialScale(Transform canvasTransform)
        {
            if (m_initialScales.ContainsKey(canvasTransform))
            {
                // Reset to initial scale
                canvasTransform.localScale = m_initialScales[canvasTransform];

                // Reset scale factor to 1.0 (100% of initial scale)
                m_currentScaleFactors[canvasTransform] = 1.0f;
            }
        }

        public void ResetAllCanvasesToInitialScale()
        {
            foreach (Transform canvas in m_canvasesToScale)
            {
                if (canvas != null)
                {
                    ResetCanvasToInitialScale(canvas);
                }
            }

            Debug.Log("PCA: Reset all canvases to their initial scales");
        }

        private void OnDestroy()
        {
            // Clean up event subscription
            SnapshotManager snapshotManager = FindObjectOfType<SnapshotManager>();
            if (snapshotManager != null)
            {
                snapshotManager.OnSnapshotCreated -= (transform) => RegisterCanvas(transform, true);
            }
        }
    }
}