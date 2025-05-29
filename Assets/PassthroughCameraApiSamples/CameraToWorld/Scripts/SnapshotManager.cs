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
                        CaptureDepthDataDirect(position, rotation);
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

        private void CaptureDepthDataDirect(Vector3 position, Quaternion rotation)
        {
            Debug.Log("PCA: Capturing depth data directly...");

            DepthFrameData depthFrame = new DepthFrameData
            {
                isValid = false,
                cameraPosition = position,
                cameraRotation = rotation
            };

            try
            {
                // Get the depth texture from shader globals
                var depthTexture = Shader.GetGlobalTexture("_EnvironmentDepthTexture") as RenderTexture;

                if (depthTexture == null)
                {
                    Debug.LogError("PCA: Depth texture is null");
                    m_snapshotDepthData.Add(depthFrame);
                    return;
                }

                Debug.Log($"PCA: Depth texture - Size: {depthTexture.width}x{depthTexture.height}, Format: {depthTexture.format}, Dimension: {depthTexture.dimension}");

                // Get shader parameters
                depthFrame.zBufferParams = Shader.GetGlobalVector("_EnvironmentDepthZBufferParams");
                var reprojectionMatrices = Shader.GetGlobalMatrixArray("_EnvironmentDepthReprojectionMatrices");

                // Create a compute shader or use a special shader to properly read the depth
                // Meta Quest depth is encoded in a special way
                string depthDecodeShader = @"
            Shader ""Hidden/DecodeMetaDepth""
            {
                Properties
                {
                    _MainTex (""Texture"", 2DArray) = ""white"" {}
                }
                SubShader
                {
                    Pass
                    {
                        CGPROGRAM
                        #pragma vertex vert
                        #pragma fragment frag
                        #include ""UnityCG.cginc""

                        struct appdata
                        {
                            float4 vertex : POSITION;
                            float2 uv : TEXCOORD0;
                        };

                        struct v2f
                        {
                            float2 uv : TEXCOORD0;
                            float4 vertex : SV_POSITION;
                        };

                        UNITY_DECLARE_TEX2DARRAY(_MainTex);
                        float4 _EnvironmentDepthZBufferParams;

                        v2f vert (appdata v)
                        {
                            v2f o;
                            o.vertex = UnityObjectToClipPos(v.vertex);
                            o.uv = v.uv;
                            return o;
                        }

                        float LinearizeDepth(float depth)
                        {
                            // Convert from packed depth to linear depth
                            // Using the z-buffer parameters from Meta's depth API
                            float ndcDepth = depth * 2.0 - 1.0;
                            return _EnvironmentDepthZBufferParams.x / (ndcDepth + _EnvironmentDepthZBufferParams.y);
                        }

                        float4 frag (v2f i) : SV_Target
                        {
                            // Sample from the first eye (index 0)
                            float packedDepth = UNITY_SAMPLE_TEX2DARRAY(_MainTex, float3(i.uv, 0)).r;
                            
                            // The depth might be in a special format
                            // Try to linearize it
                            float linearDepth = LinearizeDepth(packedDepth);
                            
                            // Normalize to 0-1 range for visualization
                            // Assuming depth range 0.1 to 10 meters
                            float normalizedDepth = saturate(linearDepth / 10.0);
                            
                            return float4(packedDepth, linearDepth, normalizedDepth, 1.0);
                        }
                        ENDCG
                    }
                }
            }";

                // Try to find or create the decode shader
                Shader shader = Shader.Find("Hidden/DecodeMetaDepth");
                Material decodeMaterial = null;

                if (shader == null)
                {
                    Debug.LogWarning("PCA: Decode shader not found, using fallback method");

                    // Fallback: Try to read the preprocessed depth texture instead
                    var preprocessedDepth = Shader.GetGlobalTexture("_PreprocessedEnvironmentDepthTexture") as RenderTexture;
                    if (preprocessedDepth != null)
                    {
                        Debug.Log("PCA: Found preprocessed depth texture, using that instead");
                        depthTexture = preprocessedDepth;
                    }
                }
                else
                {
                    decodeMaterial = new Material(shader);
                    decodeMaterial.SetVector("_EnvironmentDepthZBufferParams", depthFrame.zBufferParams);
                }

                // Create temporary textures
                RenderTexture tempRT = RenderTexture.GetTemporary(
                    depthTexture.width,
                    depthTexture.height,
                    0,
                    RenderTextureFormat.ARGBFloat // Use float format to store multiple values
                );

                if (decodeMaterial != null)
                {
                    // Use the decode shader
                    Graphics.Blit(depthTexture, tempRT, decodeMaterial);
                    Destroy(decodeMaterial);
                }
                else
                {
                    // Direct copy
                    Graphics.Blit(depthTexture, tempRT);
                }

                // Read the texture
                Texture2D depthTex2D = new Texture2D(tempRT.width, tempRT.height, TextureFormat.RGBAFloat, false);
                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = tempRT;
                depthTex2D.ReadPixels(new Rect(0, 0, tempRT.width, tempRT.height), 0, 0);
                depthTex2D.Apply();
                RenderTexture.active = previousActive;

                // Process depth data
                Color[] pixels = depthTex2D.GetPixels();
                float[] depthValues = new float[pixels.Length];

                int validCount = 0;
                float minDepth = float.MaxValue;
                float maxDepth = float.MinValue;

                // Try different channels to see which contains valid depth
                Debug.Log($"PCA: Sample pixel values - R:{pixels[0].r}, G:{pixels[0].g}, B:{pixels[0].b}, A:{pixels[0].a}");

                for (int i = 0; i < pixels.Length; i++)
                {
                    // Try different interpretations
                    float depth = pixels[i].r; // Packed depth
                    float linearDepth = pixels[i].g; // Linear depth if shader worked
                    float normalizedDepth = pixels[i].b; // Normalized depth if shader worked

                    // Use whichever seems valid
                    if (linearDepth > 0 && linearDepth != pixels[0].g) // Check if it varies
                    {
                        depth = linearDepth;
                    }
                    else if (normalizedDepth > 0 && normalizedDepth != pixels[0].b)
                    {
                        depth = normalizedDepth;
                    }

                    depthValues[i] = depth;

                    if (depth > 0.001f && depth < 0.999f && depth != 0.2155762f) // Exclude the stuck value
                    {
                        validCount++;
                        minDepth = Mathf.Min(minDepth, depth);
                        maxDepth = Mathf.Max(maxDepth, depth);
                    }
                }

                // If all values are the same, try to get raw depth differently
                if (minDepth == maxDepth)
                {
                    Debug.LogWarning("PCA: All depth values are identical, trying alternative method");

                    // Try using Graphics.CopyTexture with specific slice
                    RenderTexture sliceRT = RenderTexture.GetTemporary(
                        depthTexture.width,
                        depthTexture.height,
                        0,
                        depthTexture.format
                    );

                    // Copy specific slice if it's a texture array
                    if (depthTexture.dimension == TextureDimension.Tex2DArray)
                    {
                        Graphics.CopyTexture(depthTexture, 0, 0, sliceRT, 0, 0);
                    }
                    else
                    {
                        Graphics.Blit(depthTexture, sliceRT);
                    }

                    // Try reading again
                    RenderTexture.active = sliceRT;
                    depthTex2D.ReadPixels(new Rect(0, 0, sliceRT.width, sliceRT.height), 0, 0);
                    depthTex2D.Apply();
                    RenderTexture.active = previousActive;

                    // Re-process
                    pixels = depthTex2D.GetPixels();
                    validCount = 0;
                    minDepth = float.MaxValue;
                    maxDepth = float.MinValue;

                    for (int i = 0; i < pixels.Length; i++)
                    {
                        float depth = pixels[i].r;
                        depthValues[i] = depth;

                        if (depth > 0.001f && depth < 0.999f)
                        {
                            validCount++;
                            if (depth < minDepth) minDepth = depth;
                            if (depth > maxDepth) maxDepth = depth;
                        }
                    }

                    RenderTexture.ReleaseTemporary(sliceRT);
                }

                Debug.Log($"PCA: Captured {validCount} valid depth pixels out of {pixels.Length}");
                Debug.Log($"PCA: Depth range: {minDepth} to {maxDepth}");
                Debug.Log($"PCA: Z-buffer params: {depthFrame.zBufferParams}");

                // Update depth frame
                depthFrame.depthValues = depthValues;
                depthFrame.width = depthTex2D.width;
                depthFrame.height = depthTex2D.height;
                depthFrame.isValid = validCount > 0 && minDepth != maxDepth;

                // Cleanup
                RenderTexture.ReleaseTemporary(tempRT);
                Destroy(depthTex2D);
            }
            catch (Exception e)
            {
                Debug.LogError($"PCA: Error in depth capture: {e.Message}\n{e.StackTrace}");
            }

            m_snapshotDepthData.Add(depthFrame);
        }

        private IEnumerator CaptureDepthWithDelay(Vector3 position, Quaternion rotation)
        {
            // Wait a few frames to ensure depth is available
            yield return new WaitForSeconds(0.1f);

            // Check if depth is available
            if (!m_environmentDepthManager.IsDepthAvailable)
            {
                Debug.LogWarning("PCA: Depth not available yet, waiting...");
                float waitTime = 0;
                while (!m_environmentDepthManager.IsDepthAvailable && waitTime < 2.0f)
                {
                    yield return new WaitForSeconds(0.1f);
                    waitTime += 0.1f;
                }

                if (!m_environmentDepthManager.IsDepthAvailable)
                {
                    Debug.LogError("PCA: Depth still not available after waiting");
                    m_snapshotDepthData.Add(new DepthFrameData { isValid = false });
                    yield break;
                }
            }

            // Now capture depth
            yield return StartCoroutine(CaptureDepthDataSimple(position, rotation));
        }

        private IEnumerator CaptureDepthDataSimple(Vector3 position, Quaternion rotation)
        {
            Debug.Log("PCA: Starting depth capture...");

            RenderTexture tempRT = null;
            DepthFrameData depthFrame = new DepthFrameData { isValid = false };
            bool shouldProcessDepth = false;
            Vector4 zBufferParams = Vector4.zero;

            // First try block - setup only, no yields
            try
            {
                // Get the depth texture from shader globals
                var depthTexture = Shader.GetGlobalTexture("_EnvironmentDepthTexture");

                if (depthTexture == null)
                {
                    Debug.LogError("PCA: Global depth texture is null");
                    m_snapshotDepthData.Add(depthFrame);
                    yield break;
                }

                Debug.Log($"PCA: Found depth texture: {depthTexture.name}");

                // Try to cast to RenderTexture
                RenderTexture depthRT = depthTexture as RenderTexture;
                if (depthRT == null)
                {
                    Debug.LogError("PCA: Depth texture is not a RenderTexture");
                    m_snapshotDepthData.Add(depthFrame);
                    yield break;
                }

                Debug.Log($"PCA: Depth RT info - Size: {depthRT.width}x{depthRT.height}, Format: {depthRT.format}, Dimension: {depthRT.dimension}");

                // Get z-buffer params
                zBufferParams = Shader.GetGlobalVector("_EnvironmentDepthZBufferParams");
                Debug.Log($"PCA: Z-buffer params: {zBufferParams}");

                // Create temporary render texture
                tempRT = RenderTexture.GetTemporary(
                    depthRT.width,
                    depthRT.height,
                    0,
                    RenderTextureFormat.RFloat,
                    RenderTextureReadWrite.Linear
                );

                // Try direct blit
                Graphics.Blit(depthRT, tempRT);

                // Setup depth frame data
                depthFrame.width = tempRT.width;
                depthFrame.height = tempRT.height;
                depthFrame.cameraPosition = position;
                depthFrame.cameraRotation = rotation;
                depthFrame.zBufferParams = zBufferParams;

                shouldProcessDepth = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"PCA: Exception in depth capture setup: {e.Message}\n{e.StackTrace}");
                shouldProcessDepth = false;
            }

            // Yield outside of any try block
            if (shouldProcessDepth)
            {
                yield return new WaitForEndOfFrame();

                // Process after yield
                try
                {
                    ProcessDepthTexture(tempRT, ref depthFrame);
                }
                catch (Exception e)
                {
                    Debug.LogError($"PCA: Exception processing depth: {e.Message}");
                    depthFrame.isValid = false;
                }
            }

            // Cleanup
            if (tempRT != null)
            {
                RenderTexture.ReleaseTemporary(tempRT);
            }

            // Add the depth frame data
            m_snapshotDepthData.Add(depthFrame);
            Debug.Log($"PCA: Depth capture completed - Valid: {depthFrame.isValid}");
        }

        private void ProcessDepthTexture(RenderTexture tempRT, ref DepthFrameData depthFrame)
        {
            try
            {
                // Create Texture2D and read pixels
                Texture2D depthTex2D = new Texture2D(tempRT.width, tempRT.height, TextureFormat.RFloat, false);

                RenderTexture previousActive = RenderTexture.active;
                RenderTexture.active = tempRT;

                try
                {
                    depthTex2D.ReadPixels(new Rect(0, 0, tempRT.width, tempRT.height), 0, 0);
                    depthTex2D.Apply();
                }
                catch (Exception e)
                {
                    Debug.LogError($"PCA: Failed to read pixels: {e.Message}");
                    RenderTexture.active = previousActive;
                    Destroy(depthTex2D);
                    return;
                }

                RenderTexture.active = previousActive;

                // Get pixel data
                Color[] pixels = depthTex2D.GetPixels();
                float[] depthValues = new float[pixels.Length];

                // Analyze the data
                float minVal = float.MaxValue;
                float maxVal = float.MinValue;
                int nonZeroCount = 0;

                for (int i = 0; i < pixels.Length; i++)
                {
                    float val = pixels[i].r;
                    depthValues[i] = val;

                    if (val > 0.0001f)
                    {
                        nonZeroCount++;
                        minVal = Mathf.Min(minVal, val);
                        maxVal = Mathf.Max(maxVal, val);
                    }
                }

                Debug.Log($"PCA: Depth data analysis - Non-zero pixels: {nonZeroCount}/{pixels.Length}, Range: {minVal} to {maxVal}");

                // Update depth frame data
                depthFrame.depthValues = depthValues;
                depthFrame.isValid = nonZeroCount > 0;

                // Cleanup
                Destroy(depthTex2D);
            }
            catch (Exception e)
            {
                Debug.LogError($"PCA: Error processing depth texture: {e.Message}");
                depthFrame.isValid = false;
            }
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