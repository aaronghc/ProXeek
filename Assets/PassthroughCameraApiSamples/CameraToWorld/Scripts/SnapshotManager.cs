// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections.Generic;
using System.IO;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.UI;

namespace PassthroughCameraSamples.CameraToWorld
{
    [MetaCodeSample("PassthroughCameraApiSamples-CameraToWorld")]
    public class SnapshotManager : MonoBehaviour
    {
        [SerializeField] private WebCamTextureManager m_webCamTextureManager;
        [SerializeField] private GameObject m_cameraCanvasPrefab; // Reference to the existing CameraToWorldCameraCanvas prefab
        [SerializeField] private Transform m_snapshotParent;
        [SerializeField] private int m_maxSnapshots = 10;

        private List<GameObject> m_snapshotCanvases = new List<GameObject>();
        private List<Texture2D> m_snapshotTextures = new List<Texture2D>();

        // Add this near the top of the SnapshotManager class
        public delegate void SnapshotCreatedHandler(Transform snapshotTransform);
        public event SnapshotCreatedHandler OnSnapshotCreated;

        public bool HasSnapshots => m_snapshotCanvases.Count > 0;

        private void Start()
        {
            // Create snapshot parent if not assigned
            if (m_snapshotParent == null)
            {
                GameObject parentObj = new GameObject("SnapshotParent");
                m_snapshotParent = parentObj.transform;
                Debug.Log("PCA: Created SnapshotParent GameObject");
            }

            // Validate required references
            if (m_webCamTextureManager == null)
            {
                Debug.LogError("PCA: WebCamTextureManager reference is missing in SnapshotManager");
            }

            if (m_cameraCanvasPrefab == null)
            {
                Debug.LogError("PCA: Camera Canvas Prefab reference is missing in SnapshotManager");
            }
        }

        public void TakeSnapshot(Vector3 position, Quaternion rotation)
        {
            Debug.Log("PCA: TakeSnapshot called at position: " + position);

            // Validate required components
            if (m_webCamTextureManager == null)
            {
                Debug.LogError("PCA: WebCamTextureManager is null");
                return;
            }

            if (m_cameraCanvasPrefab == null)
            {
                Debug.LogError("PCA: Camera Canvas Prefab is null");
                return;
            }

            var webCamTexture = m_webCamTextureManager.WebCamTexture;
            if (webCamTexture == null)
            {
                Debug.LogError("PCA: WebCamTexture is null");
                return;
            }

            if (!webCamTexture.isPlaying)
            {
                Debug.LogError("PCA: WebCamTexture is not playing");
                return;
            }

            try
            {
                // Create a new snapshot texture
                var snapshotTexture = new Texture2D(webCamTexture.width, webCamTexture.height, TextureFormat.RGBA32, false);
                var pixelsBuffer = new Color32[webCamTexture.width * webCamTexture.height];

                // Copy the current frame from WebCamTexture
                webCamTexture.GetPixels32(pixelsBuffer);
                snapshotTexture.SetPixels32(pixelsBuffer);
                snapshotTexture.Apply();

                // Store the texture
                m_snapshotTextures.Add(snapshotTexture);

                // Instantiate a new canvas for this snapshot
                var canvasGO = Instantiate(m_cameraCanvasPrefab, position, rotation, m_snapshotParent);
                canvasGO.name = "Snapshot_" + m_snapshotCanvases.Count;

                // Find the RawImage component in the canvas hierarchy
                var rawImage = canvasGO.GetComponentInChildren<RawImage>();
                if (rawImage != null)
                {
                    rawImage.texture = snapshotTexture;
                    Debug.Log("PCA: Set texture on RawImage");
                }
                else
                {
                    Debug.LogError("PCA: RawImage component not found in canvas");
                }

                // Disable any CameraToWorldCameraCanvas script to prevent it from updating
                var canvasScript = canvasGO.GetComponent<CameraToWorldCameraCanvas>();
                if (canvasScript != null)
                {
                    canvasScript.enabled = false;
                    Debug.Log("PCA: Disabled CameraToWorldCameraCanvas script");
                }

                // Add to our list of snapshots
                m_snapshotCanvases.Add(canvasGO);
                OnSnapshotCreated?.Invoke(canvasGO.transform); // Notify listeners about the new snapshot



                // If we've exceeded the maximum number of snapshots, remove the oldest one
                if (m_snapshotCanvases.Count > m_maxSnapshots)
                {
                    RemoveOldestSnapshot();
                }

                Debug.Log($"PCA: Snapshot taken successfully. Total snapshots: {m_snapshotCanvases.Count}");
            }
            catch (Exception e)
            {
                Debug.LogError($"PCA: Error taking snapshot: {e.Message}\n{e.StackTrace}");
            }
        }

        public void SaveAllSnapshots()
        {
            if (m_snapshotTextures.Count == 0)
            {
                Debug.Log("PCA: No snapshots to save.");
                return;
            }

            try
            {
                string folderPath = Path.Combine(Application.persistentDataPath, "Snapshots");
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                int savedCount = 0;

                for (int i = 0; i < m_snapshotTextures.Count; i++)
                {
                    try
                    {
                        string filePath = Path.Combine(folderPath, $"Snapshot_{timestamp}_{i:D2}.png");
                        byte[] bytes = m_snapshotTextures[i].EncodeToPNG();
                        File.WriteAllBytes(filePath, bytes);
                        savedCount++;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"PCA: Failed to save snapshot {i}: {e.Message}");
                    }
                }

                Debug.Log($"PCA: Saved {savedCount} snapshots to {folderPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"PCA: Error saving snapshots: {e.Message}\n{e.StackTrace}");
            }
        }

        public void ClearAllSnapshots()
        {
            try
            {
                foreach (var canvas in m_snapshotCanvases)
                {
                    if (canvas != null)
                    {
                        Destroy(canvas);
                    }
                }

                foreach (var texture in m_snapshotTextures)
                {
                    if (texture != null)
                    {
                        Destroy(texture);
                    }
                }

                m_snapshotCanvases.Clear();
                m_snapshotTextures.Clear();

                Debug.Log("PCA: All snapshots cleared.");
            }
            catch (Exception e)
            {
                Debug.LogError($"PCA: Error clearing snapshots: {e.Message}\n{e.StackTrace}");
            }
        }

        private void RemoveOldestSnapshot()
        {
            if (m_snapshotCanvases.Count > 0)
            {
                // Remove the oldest canvas
                if (m_snapshotCanvases[0] != null)
                {
                    Destroy(m_snapshotCanvases[0]);
                }
                m_snapshotCanvases.RemoveAt(0);

                // Remove the oldest texture
                if (m_snapshotTextures.Count > 0)
                {
                    if (m_snapshotTextures[0] != null)
                    {
                        Destroy(m_snapshotTextures[0]);
                    }
                    m_snapshotTextures.RemoveAt(0);
                }
            }
        }
    }
}