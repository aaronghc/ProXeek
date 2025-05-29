// Copyright (c) Meta Platforms, Inc. and affiliates.

using System;
using System.Collections.Generic;
using System.IO;
using Meta.XR.Samples;
using UnityEngine;
using UnityEngine.UI;

using Meta.XR.EnvironmentDepth;
using System.Collections;
using UnityEngine.Rendering;
using System.Linq;

namespace PassthroughCameraSamples.CameraToWorld
{
    [MetaCodeSample("PassthroughCameraApiSamples-CameraToWorld")]
    public class SnapshotManager : MonoBehaviour
    {
        [SerializeField] private WebCamTextureManager m_webCamTextureManager;
        [SerializeField] private GameObject m_cameraCanvasPrefab; // Reference to the existing CameraToWorldCameraCanvas prefab
        [SerializeField] private Transform m_snapshotParent;
        [SerializeField] private int m_maxSnapshots = 10;

        // Add new fields for depth capture
        [Header("Depth Capture Settings")]
        [SerializeField] private EnvironmentDepthManager m_environmentDepthManager;
        [SerializeField] private bool m_captureDepthData = true;

        private List<GameObject> m_snapshotCanvases = new List<GameObject>();
        private List<Texture2D> m_snapshotTextures = new List<Texture2D>();

        // Store depth data alongside snapshots
        private List<DepthFrameData> m_snapshotDepthData = new List<DepthFrameData>();

        // Add coroutine tracking
        private bool m_isCapturingDepth = false;

        // Add this near the top of the SnapshotManager class
        public delegate void SnapshotCreatedHandler(Transform snapshotTransform);
        public event SnapshotCreatedHandler OnSnapshotCreated;

        // Structure to hold depth frame data
        [System.Serializable]
        public struct DepthFrameData
        {
            public float[] depthValues;
            public int width;
            public int height;
            public Vector3 cameraPosition;
            public Quaternion cameraRotation;
            public Matrix4x4[] reprojectionMatrices;
            public Vector4 zBufferParams;
            public bool isValid;
        }

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
            // Find EnvironmentDepthManager if not assigned
            if (m_environmentDepthManager == null)
            {
                m_environmentDepthManager = FindObjectOfType<EnvironmentDepthManager>();
                if (m_environmentDepthManager == null && m_captureDepthData)
                {
                    Debug.LogWarning("PCA: EnvironmentDepthManager not found. Depth capture will be disabled.");
                    m_captureDepthData = false;
                }
            }

            // Ensure EnvironmentDepthManager is enabled
            if (m_environmentDepthManager != null && !m_environmentDepthManager.enabled)
            {
                Debug.Log("PCA: Enabling EnvironmentDepthManager for depth capture");
                m_environmentDepthManager.enabled = true;
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

                // Capture depth data directly (no coroutine)
                if (m_captureDepthData && m_environmentDepthManager != null)
                {
                    if (m_environmentDepthManager.IsDepthAvailable)
                    {
                        CaptureDepthDataSimplified(position, rotation);
                    }
                    else
                    {
                        Debug.LogWarning("PCA: Depth not available yet");
                        m_snapshotDepthData.Add(new DepthFrameData { isValid = false });
                    }
                }

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

        private void CaptureDepthDataSimplified(Vector3 position, Quaternion rotation)
        {
            Debug.Log("PCA: Trying simplified depth capture...");

            DepthFrameData depthFrame = new DepthFrameData
            {
                isValid = false,
                cameraPosition = position,
                cameraRotation = rotation
            };

            try
            {
                // Try different depth texture sources
                RenderTexture depthSource = null;
                string sourceName = "";

                // Option 1: Try preprocessed depth texture (might have better data)
                var preprocessedDepth = Shader.GetGlobalTexture("_PreprocessedEnvironmentDepthTexture") as RenderTexture;
                if (preprocessedDepth != null)
                {
                    depthSource = preprocessedDepth;
                    sourceName = "Preprocessed";
                    Debug.Log($"PCA: Using preprocessed depth texture");
                }
                else
                {
                    // Option 2: Use regular depth texture
                    depthSource = Shader.GetGlobalTexture("_EnvironmentDepthTexture") as RenderTexture;
                    sourceName = "Regular";
                    Debug.Log($"PCA: Using regular depth texture");
                }

                if (depthSource == null)
                {
                    Debug.LogError("PCA: No depth texture found");
                    m_snapshotDepthData.Add(depthFrame);
                    return;
                }

                Debug.Log($"PCA: {sourceName} depth - Size: {depthSource.width}x{depthSource.height}, Format: {depthSource.format}");

                // Get shader parameters
                depthFrame.zBufferParams = Shader.GetGlobalVector("_EnvironmentDepthZBufferParams");

                // For preprocessed texture, we might be able to read it directly
                Texture2D depthTex2D = new Texture2D(depthSource.width, depthSource.height, TextureFormat.RGBAFloat, false);

                RenderTexture.active = depthSource;
                depthTex2D.ReadPixels(new Rect(0, 0, depthSource.width, depthSource.height), 0, 0);
                depthTex2D.Apply();
                RenderTexture.active = null;

                // Analyze the data
                Color[] pixels = depthTex2D.GetPixels();
                float[] depthValues = new float[pixels.Length];

                // Check what data we have in each channel
                Debug.Log($"PCA: First 5 pixels - R:{pixels[0].r}, G:{pixels[0].g}, B:{pixels[0].b}, A:{pixels[0].a}");
                for (int i = 1; i < Mathf.Min(5, pixels.Length); i++)
                {
                    Debug.Log($"PCA: Pixel {i} - R:{pixels[i].r}, G:{pixels[i].g}, B:{pixels[i].b}, A:{pixels[i].a}");
                }

                // Process based on which channel has varying data
                int validCount = 0;
                float minDepth = float.MaxValue;
                float maxDepth = float.MinValue;

                for (int i = 0; i < pixels.Length; i++)
                {
                    // For preprocessed texture, depth might be in different channels
                    float depth = pixels[i].r; // Try red channel first

                    // If using preprocessed texture, it might store additional data
                    if (sourceName == "Preprocessed" && pixels[i].g > 0)
                    {
                        depth = pixels[i].g; // Might be linear depth
                    }

                    depthValues[i] = depth;

                    if (depth > 0.001f && depth < 100.0f) // Wider range for linear depth
                    {
                        validCount++;
                        minDepth = Mathf.Min(minDepth, depth);
                        maxDepth = Mathf.Max(maxDepth, depth);
                    }
                }

                Debug.Log($"PCA: Valid pixels: {validCount}/{pixels.Length}, Range: {minDepth} to {maxDepth}");

                // Store the data
                depthFrame.depthValues = depthValues;
                depthFrame.width = depthTex2D.width;
                depthFrame.height = depthTex2D.height;
                depthFrame.isValid = validCount > 0 && minDepth != maxDepth;

                Destroy(depthTex2D);
            }
            catch (Exception e)
            {
                Debug.LogError($"PCA: Error in simplified depth capture: {e.Message}");
            }

            m_snapshotDepthData.Add(depthFrame);
        }

        // Simplified save method
        private void SaveDepthData(string folderPath, string timestamp, int index, DepthFrameData depthData)
        {
            try
            {
                if (!depthData.isValid)
                {
                    Debug.LogWarning($"PCA: Skipping invalid depth data for snapshot {index}");
                    return;
                }

                // Save a simple text file with depth info for debugging
                string debugPath = Path.Combine(folderPath, $"DepthDebug_{timestamp}_{index:D2}.txt");
                using (StreamWriter writer = new StreamWriter(debugPath))
                {
                    writer.WriteLine($"Width: {depthData.width}");
                    writer.WriteLine($"Height: {depthData.height}");
                    writer.WriteLine($"Total pixels: {depthData.depthValues.Length}");

                    // Sample some values
                    writer.WriteLine("\nSample values (first 100):");
                    for (int i = 0; i < Math.Min(100, depthData.depthValues.Length); i++)
                    {
                        writer.WriteLine($"Pixel {i}: {depthData.depthValues[i]}");
                    }

                    // Statistics
                    var nonZeroValues = depthData.depthValues.Where(v => v > 0.0001f).ToArray();
                    if (nonZeroValues.Length > 0)
                    {
                        writer.WriteLine($"\nNon-zero values: {nonZeroValues.Length}");
                        writer.WriteLine($"Min: {nonZeroValues.Min()}");
                        writer.WriteLine($"Max: {nonZeroValues.Max()}");
                        writer.WriteLine($"Average: {nonZeroValues.Average()}");
                    }
                }

                // Save raw binary data
                string depthPath = Path.Combine(folderPath, $"DepthRaw_{timestamp}_{index:D2}.raw");
                using (BinaryWriter writer = new BinaryWriter(File.Open(depthPath, FileMode.Create)))
                {
                    foreach (float depth in depthData.depthValues)
                    {
                        writer.Write(depth);
                    }
                }

                // Save simple visualization
                SaveSimpleDepthVisualization(folderPath, timestamp, index, depthData);

                Debug.Log($"PCA: Saved depth data to {folderPath}");
            }
            catch (Exception e)
            {
                Debug.LogError($"PCA: Error saving depth data: {e.Message}");
            }
        }

        private void SaveSimpleDepthVisualization(string folderPath, string timestamp, int index, DepthFrameData depthData)
        {
            try
            {
                Texture2D depthVis = new Texture2D(depthData.width, depthData.height, TextureFormat.RGB24, false);
                Color[] colors = new Color[depthData.depthValues.Length];

                // Simple visualization - just show raw values
                for (int i = 0; i < depthData.depthValues.Length; i++)
                {
                    float val = depthData.depthValues[i];
                    // Clamp to 0-1 range
                    val = Mathf.Clamp01(val);
                    colors[i] = new Color(val, val, val);
                }

                depthVis.SetPixels(colors);
                depthVis.Apply();

                string visPath = Path.Combine(folderPath, $"DepthVis_{timestamp}_{index:D2}.png");
                byte[] visBytes = depthVis.EncodeToPNG();
                File.WriteAllBytes(visPath, visBytes);

                Destroy(depthVis);
            }
            catch (Exception e)
            {
                Debug.LogError($"PCA: Error saving depth visualization: {e.Message}");
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
                string depthFolderPath = Path.Combine(Application.persistentDataPath, "DepthData");

                // Create directories
                if (!Directory.Exists(folderPath))
                    Directory.CreateDirectory(folderPath);

                if (!Directory.Exists(depthFolderPath))
                    Directory.CreateDirectory(depthFolderPath);

                // Clear previous files
                ClearPreviousFiles(folderPath, "*.png");
                ClearPreviousFiles(depthFolderPath, "*.json");
                ClearPreviousFiles(depthFolderPath, "*.raw");

                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                int savedCount = 0;

                for (int i = 0; i < m_snapshotTextures.Count; i++)
                {
                    try
                    {
                        // Save RGB image
                        string imagePath = Path.Combine(folderPath, $"Snapshot_{timestamp}_{i:D2}.png");
                        byte[] imageBytes = m_snapshotTextures[i].EncodeToPNG();
                        File.WriteAllBytes(imagePath, imageBytes);

                        // Save depth data if available
                        if (m_captureDepthData && i < m_snapshotDepthData.Count)
                        {
                            SaveDepthData(depthFolderPath, timestamp, i, m_snapshotDepthData[i]);
                        }

                        savedCount++;
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"PCA: Failed to save snapshot {i}: {e.Message}");
                    }
                }

                Debug.Log($"PCA: Saved {savedCount} snapshots to {folderPath}");
                if (m_captureDepthData)
                {
                    Debug.Log($"PCA: Saved depth data to {depthFolderPath}");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"PCA: Error saving snapshots: {e.Message}\n{e.StackTrace}");
            }
        }


        private void ClearPreviousFiles(string folderPath, string pattern)
        {
            try
            {
                string[] files = Directory.GetFiles(folderPath, pattern);
                foreach (string file in files)
                {
                    File.Delete(file);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"PCA: Error clearing files: {e.Message}");
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
                m_snapshotDepthData.Clear();

                Debug.Log("PCA: All snapshots and depth data cleared.");
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

                // Remove oldest depth data
                if (m_snapshotDepthData.Count > 0)
                {
                    m_snapshotDepthData.RemoveAt(0);
                }
            }
        }
    }
}