using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;
using PassthroughCameraSamples.CameraToWorld;

namespace PythonIntegration
{
    public class PythonTriggerHandler : MonoBehaviour
    {
        [Header("Server Configuration")]
        [SerializeField] private string serverUrl = "http://172.28.194.245:5000/run_python";
        [SerializeField] private float cooldownTime = 2.0f;
        [SerializeField] private int maxImagesToSend = 5; // Limit number of images to send per category

        [Header("UI References")]
        [SerializeField] private Text responseText;
        [SerializeField] private GameObject loadingIndicator;

        [Header("Snapshot References")]
        [SerializeField] private SnapshotManager snapshotManager;

        [Header("Debug")]
        [SerializeField] private bool debugMode = true;
        [SerializeField] private Text debugText;

        private bool _isCoolingDown = false;
        private HttpClient _httpClient;
        private bool _isProcessing = false;
        private const float TRIGGER_THRESHOLD = 0.7f;
        private bool _wasRightTriggerPressed = false;

        private void Awake()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(600);

            if (loadingIndicator != null)
                loadingIndicator.SetActive(false);

            if (snapshotManager == null)
                snapshotManager = FindObjectOfType<SnapshotManager>();

            LogDebug("Python Trigger Handler initialized");
        }

        private void Update()
        {
            float rightTriggerValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger);
            bool isRightTriggerPressed = rightTriggerValue > TRIGGER_THRESHOLD;

            if (isRightTriggerPressed && !_wasRightTriggerPressed && !_isCoolingDown && !_isProcessing)
            {
                LogDebug("Trigger pressed: " + rightTriggerValue);
                StartCoroutine(ProcessAllData());
            }

            _wasRightTriggerPressed = isRightTriggerPressed;
        }

        private IEnumerator ProcessAllData()
        {
            _isCoolingDown = true;
            _isProcessing = true;

            if (loadingIndicator != null)
                loadingIndicator.SetActive(true);

            if (responseText != null)
                responseText.text = "Processing data...";

            // Data containers
            List<string> hapticAnnotationJsons = new List<string>();
            List<string> environmentSnapshots = new List<string>();
            List<VirtualObjectSnapshot> virtualObjectSnapshots = new List<VirtualObjectSnapshot>();
            List<ArrangementSnapshot> arrangementSnapshots = new List<ArrangementSnapshot>();

            // 1. Get environment snapshots from the Snapshots folder
            yield return StartCoroutine(CollectEnvironmentSnapshots(environmentSnapshots));

            // 2. Get files from Export folder in StreamingAssets
            yield return StartCoroutine(CollectExportFolderData(
                hapticAnnotationJsons,
                virtualObjectSnapshots,
                arrangementSnapshots));

            // Check if we have the necessary data
            string hapticAnnotationJson = hapticAnnotationJsons.Count > 0 ? hapticAnnotationJsons[0] : "";

            if (string.IsNullOrEmpty(hapticAnnotationJson))
            {
                if (responseText != null)
                    responseText.text = "No haptic annotation JSON found in Export folder.";

                _isProcessing = false;
                yield return new WaitForSeconds(cooldownTime);
                _isCoolingDown = false;
                yield break;
            }

            if (environmentSnapshots.Count == 0)
            {
                if (responseText != null)
                    responseText.text = "No environment snapshots found. Take some snapshots first.";

                _isProcessing = false;
                yield return new WaitForSeconds(cooldownTime);
                _isCoolingDown = false;
                yield break;
            }

            // Create the payload
            var payload = new CompletePayload
            {
                action = "run_script",
                script_name = "ProXeek.py",
                @params = new CompleteParams
                {
                    hapticAnnotationJson = hapticAnnotationJson,
                    environmentImageBase64List = environmentSnapshots,
                    virtualObjectSnapshots = virtualObjectSnapshots,
                    arrangementSnapshots = arrangementSnapshots
                }
            };

            string jsonPayload = JsonUtility.ToJson(payload);
            LogDebug($"Created JSON payload with {environmentSnapshots.Count} environment images, " +
                     $"{virtualObjectSnapshots.Count} virtual object snapshots, and " +
                     $"{arrangementSnapshots.Count} arrangement snapshots");

            // Send to server
            Task<string> responseTask = SendRequest(jsonPayload);

            while (!responseTask.IsCompleted)
            {
                yield return null;
            }

            if (responseTask.IsFaulted)
            {
                string errorMessage = "Error: Could not connect to server";
                if (responseTask.Exception != null)
                {
                    errorMessage += "\n" + responseTask.Exception.Message;
                }

                LogDebug("Error: " + errorMessage);

                if (responseText != null)
                    responseText.text = errorMessage;
            }
            else
            {
                string response = responseTask.Result;
                LogDebug("Response received: " + (response.Length > 50 ? response.Substring(0, 50) + "..." : response));

                try
                {
                    ResponseData responseData = JsonUtility.FromJson<ResponseData>(response);
                    string displayText = responseData.output;

                    if (responseText != null)
                        responseText.text = displayText;
                }
                catch (Exception ex)
                {
                    if (responseText != null)
                        responseText.text = response;

                    LogDebug("Failed to parse response: " + ex.Message);
                }
            }

            if (loadingIndicator != null)
                loadingIndicator.SetActive(false);

            _isProcessing = false;
            yield return new WaitForSeconds(cooldownTime);
            _isCoolingDown = false;
        }

        private IEnumerator CollectEnvironmentSnapshots(List<string> environmentSnapshots)
        {
            string snapshotsFolder = Path.Combine(Application.persistentDataPath, "Snapshots");
            LogDebug("Looking for environment snapshots in: " + snapshotsFolder);

            if (!Directory.Exists(snapshotsFolder))
            {
                LogDebug("Snapshots folder not found");
                yield break;
            }

            // Get all snapshot files
            string[] snapshotFiles = Directory.GetFiles(snapshotsFolder, "*.png");
            if (snapshotFiles.Length == 0)
            {
                LogDebug("No environment snapshots found");
                yield break;
            }

            // Sort by creation time (newest first)
            Array.Sort(snapshotFiles, (a, b) => File.GetCreationTime(b).CompareTo(File.GetCreationTime(a)));

            // Limit the number of images to send
            int imagesToSend = Mathf.Min(snapshotFiles.Length, maxImagesToSend);
            LogDebug($"Found {snapshotFiles.Length} environment snapshots, sending {imagesToSend}");

            // Convert images to base64
            for (int i = 0; i < imagesToSend; i++)
            {
                byte[] imageArray = File.ReadAllBytes(snapshotFiles[i]);
                string base64Image = Convert.ToBase64String(imageArray);
                environmentSnapshots.Add(base64Image);
                LogDebug($"Converted environment image {i + 1}/{imagesToSend} to base64");

                // Yield to prevent frame drops during processing
                yield return null;
            }
        }

        private IEnumerator CollectExportFolderData(
            List<string> hapticAnnotationJsons,
            List<VirtualObjectSnapshot> virtualObjectSnapshots,
            List<ArrangementSnapshot> arrangementSnapshots)
        {
            // Use StreamingAssets/Export path
            string exportFolderPath = Path.Combine(Application.streamingAssetsPath, "Export");
            LogDebug("Looking for export data in: " + exportFolderPath);

            // Check if we're on Android (including Meta Quest)
            bool isAndroid = Application.platform == RuntimePlatform.Android;
            LogDebug($"Platform is Android: {isAndroid}");

            if (isAndroid)
            {
                // On Android, we need to use UnityWebRequest to access files in StreamingAssets
                yield return StartCoroutine(CollectExportFolderDataAndroid(
                    exportFolderPath,
                    hapticAnnotationJsons,
                    virtualObjectSnapshots,
                    arrangementSnapshots));
            }
            else
            {
                // On other platforms, we can access files directly
                yield return StartCoroutine(CollectExportFolderDataDirect(
                    exportFolderPath,
                    hapticAnnotationJsons,
                    virtualObjectSnapshots,
                    arrangementSnapshots));
            }
        }

        private IEnumerator CollectExportFolderDataDirect(
            string exportFolderPath,
            List<string> hapticAnnotationJsons,
            List<VirtualObjectSnapshot> virtualObjectSnapshots,
            List<ArrangementSnapshot> arrangementSnapshots)
        {
            if (!Directory.Exists(exportFolderPath))
            {
                LogDebug("Export folder not found in StreamingAssets");
                yield break;
            }

            // 1. Find haptic annotation JSON file
            string[] jsonFiles = Directory.GetFiles(exportFolderPath, "haptic_annotation*.json");
            if (jsonFiles.Length > 0)
            {
                // Use the most recent one if multiple exist
                Array.Sort(jsonFiles, (a, b) => File.GetCreationTime(b).CompareTo(File.GetCreationTime(a)));
                string jsonFilePath = jsonFiles[0];

                try
                {
                    string jsonContent = File.ReadAllText(jsonFilePath);
                    hapticAnnotationJsons.Add(jsonContent);
                    LogDebug($"Loaded haptic annotation JSON: {Path.GetFileName(jsonFilePath)}");
                }
                catch (Exception ex)
                {
                    LogDebug($"Error reading JSON file: {ex.Message}");
                }
            }
            else
            {
                LogDebug("No haptic annotation JSON file found");
            }

            yield return null;

            // 2. Find virtual object snapshots (GameObject_*.png)
            string[] virtualObjectFiles = Directory.GetFiles(exportFolderPath, "GameObject_*.png");
            LogDebug($"Found {virtualObjectFiles.Length} virtual object snapshots");

            foreach (string filePath in virtualObjectFiles)
            {
                try
                {
                    string fileName = Path.GetFileNameWithoutExtension(filePath);
                    // Extract object name from filename (format: GameObject_ObjectName_Date...)
                    string[] parts = fileName.Split('_');
                    // Skip the first part ("GameObject") and extract all parts until we hit a part that looks like a date
                    // Assuming date format contains numbers and is at least 8 characters long
                    List<string> objectNameParts = new List<string>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        // Check if this part looks like it might be the start of a timestamp
                        // (contains digits and is long enough to be a date)
                        if (parts[i].Length >= 8 && parts[i].Any(char.IsDigit))
                        {
                            break;
                        }
                        objectNameParts.Add(parts[i]);
                    }

                    // Join the object name parts back together with underscores
                    string objectName = objectNameParts.Count > 0
                        ? string.Join("_", objectNameParts)
                        : "Unknown";

                    byte[] imageArray = File.ReadAllBytes(filePath);
                    string base64Image = Convert.ToBase64String(imageArray);

                    virtualObjectSnapshots.Add(new VirtualObjectSnapshot
                    {
                        objectName = objectName,
                        imageBase64 = base64Image
                    });

                    LogDebug($"Added virtual object snapshot: {fileName}");
                }
                catch (Exception ex)
                {
                    LogDebug($"Error processing virtual object snapshot: {ex.Message}");
                }

                yield return null;
            }

            // 3. Find arrangement snapshots (Arrangement_*.png)
            string[] arrangementFiles = Directory.GetFiles(exportFolderPath, "Arrangement_*.png");

            // Group by arrangement name
            Dictionary<string, List<string>> arrangementGroups = new Dictionary<string, List<string>>();

            foreach (string filePath in arrangementFiles)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                string[] parts = fileName.Split('_');

                if (parts.Length >= 3)
                {
                    string arrangementName = parts[1];

                    if (!arrangementGroups.ContainsKey(arrangementName))
                    {
                        arrangementGroups[arrangementName] = new List<string>();
                    }

                    arrangementGroups[arrangementName].Add(filePath);
                }
            }

            LogDebug($"Found {arrangementGroups.Count} arrangement groups");

            // Process each arrangement group
            foreach (var group in arrangementGroups)
            {
                List<string> imageBase64List = new List<string>();

                foreach (string filePath in group.Value)
                {
                    try
                    {
                        byte[] imageArray = File.ReadAllBytes(filePath);
                        string base64Image = Convert.ToBase64String(imageArray);
                        imageBase64List.Add(base64Image);
                    }
                    catch (Exception ex)
                    {
                        LogDebug($"Error processing arrangement snapshot: {ex.Message}");
                    }

                    yield return null;
                }

                arrangementSnapshots.Add(new ArrangementSnapshot
                {
                    arrangementName = group.Key,
                    imageBase64List = imageBase64List
                });

                LogDebug($"Added arrangement group: {group.Key} with {imageBase64List.Count} images");
            }
        }

        private IEnumerator CollectExportFolderDataAndroid(
            string exportFolderPath,
            List<string> hapticAnnotationJsons,
            List<VirtualObjectSnapshot> virtualObjectSnapshots,
            List<ArrangementSnapshot> arrangementSnapshots)
        {
            LogDebug("Using Android-specific method to access StreamingAssets");

            // 1. First, try to load the manifest file that lists all available files
            string manifestUrl = Path.Combine(Application.streamingAssetsPath, "Export/manifest.json");
            LogDebug($"Trying to load manifest from: {manifestUrl}");

            UnityWebRequest manifestRequest = UnityWebRequest.Get(manifestUrl);
            yield return manifestRequest.SendWebRequest();

            List<string> jsonFiles = new List<string>();
            List<string> gameObjectFiles = new List<string>();
            Dictionary<string, List<string>> arrangementGroups = new Dictionary<string, List<string>>();

            if (manifestRequest.result == UnityWebRequest.Result.Success)
            {
                // Parse the manifest file
                try
                {
                    string manifestJson = manifestRequest.downloadHandler.text;
                    LogDebug($"Loaded manifest: {manifestJson}");

                    ManifestData manifest = JsonUtility.FromJson<ManifestData>(manifestJson);

                    // Process files from manifest
                    if (manifest != null && manifest.files != null)
                    {
                        foreach (string file in manifest.files)
                        {
                            if (file.StartsWith("haptic_annotation") && file.EndsWith(".json"))
                            {
                                jsonFiles.Add(file);
                            }
                            else if (file.StartsWith("GameObject_") && file.EndsWith(".png"))
                            {
                                gameObjectFiles.Add(file);
                            }
                            else if (file.StartsWith("Arrangement_") && file.EndsWith(".png"))
                            {
                                string fileName = Path.GetFileNameWithoutExtension(file);
                                string[] parts = fileName.Split('_');

                                if (parts.Length >= 3)
                                {
                                    string arrangementName = parts[1];

                                    if (!arrangementGroups.ContainsKey(arrangementName))
                                    {
                                        arrangementGroups[arrangementName] = new List<string>();
                                    }

                                    arrangementGroups[arrangementName].Add(file);
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogDebug($"Error parsing manifest: {ex.Message}");
                }
            }
            else
            {
                LogDebug($"Failed to load manifest: {manifestRequest.error}");
                LogDebug("Will try to load files directly");

                // If manifest doesn't exist, we'll try some common filenames
                jsonFiles.Add("haptic_annotation.json");

                // We can't list files in StreamingAssets on Android, so we'll have to guess filenames
                // This is a limitation of the Android platform
            }

            // 2. Load haptic annotation JSON
            if (jsonFiles.Count > 0)
            {
                foreach (string jsonFile in jsonFiles)
                {
                    string jsonUrl = Path.Combine(Application.streamingAssetsPath, "Export/" + jsonFile);
                    LogDebug($"Loading JSON from: {jsonUrl}");

                    UnityWebRequest jsonRequest = UnityWebRequest.Get(jsonUrl);
                    yield return jsonRequest.SendWebRequest();

                    if (jsonRequest.result == UnityWebRequest.Result.Success)
                    {
                        string jsonContent = jsonRequest.downloadHandler.text;
                        hapticAnnotationJsons.Add(jsonContent);
                        LogDebug($"Loaded haptic annotation JSON: {jsonFile}");
                        break; // Just use the first successful one
                    }
                    else
                    {
                        LogDebug($"Failed to load JSON {jsonFile}: {jsonRequest.error}");
                    }
                }
            }

            // 3. Load virtual object snapshots
            foreach (string gameObjectFile in gameObjectFiles)
            {
                string imageUrl = Path.Combine(Application.streamingAssetsPath, "Export/" + gameObjectFile);
                LogDebug($"Loading virtual object image from: {imageUrl}");

                UnityWebRequest imageRequest = UnityWebRequestTexture.GetTexture(imageUrl);
                yield return imageRequest.SendWebRequest();

                if (imageRequest.result == UnityWebRequest.Result.Success)
                {
                    Texture2D texture = ((DownloadHandlerTexture)imageRequest.downloadHandler).texture;
                    byte[] pngData = texture.EncodeToPNG();
                    string base64Image = Convert.ToBase64String(pngData);

                    string fileName = Path.GetFileNameWithoutExtension(gameObjectFile);
                    string[] parts = fileName.Split('_');
                    // Skip the first part ("GameObject") and extract all parts until we hit a part that looks like a date
                    // Assuming date format contains numbers and is at least 8 characters long
                    List<string> objectNameParts = new List<string>();
                    for (int i = 1; i < parts.Length; i++)
                    {
                        // Check if this part looks like it might be the start of a timestamp
                        // (contains digits and is long enough to be a date)
                        if (parts[i].Length >= 8 && parts[i].Any(char.IsDigit))
                        {
                            break;
                        }
                        objectNameParts.Add(parts[i]);
                    }

                    // Join the object name parts back together with underscores
                    string objectName = objectNameParts.Count > 0
                        ? string.Join("_", objectNameParts)
                        : "Unknown";

                    virtualObjectSnapshots.Add(new VirtualObjectSnapshot
                    {
                        objectName = objectName,
                        imageBase64 = base64Image
                    });

                    LogDebug($"Added virtual object snapshot: {fileName}");

                    // Clean up
                    Destroy(texture);
                }
                else
                {
                    LogDebug($"Failed to load image {gameObjectFile}: {imageRequest.error}");
                }

                yield return null;
            }

            // 4. Load arrangement snapshots
            foreach (var group in arrangementGroups)
            {
                List<string> imageBase64List = new List<string>();

                foreach (string arrangementFile in group.Value)
                {
                    string imageUrl = Path.Combine(Application.streamingAssetsPath, "Export/" + arrangementFile);
                    LogDebug($"Loading arrangement image from: {imageUrl}");

                    UnityWebRequest imageRequest = UnityWebRequestTexture.GetTexture(imageUrl);
                    yield return imageRequest.SendWebRequest();

                    if (imageRequest.result == UnityWebRequest.Result.Success)
                    {
                        Texture2D texture = ((DownloadHandlerTexture)imageRequest.downloadHandler).texture;
                        byte[] pngData = texture.EncodeToPNG();
                        string base64Image = Convert.ToBase64String(pngData);

                        imageBase64List.Add(base64Image);

                        // Clean up
                        Destroy(texture);
                    }
                    else
                    {
                        LogDebug($"Failed to load image {arrangementFile}: {imageRequest.error}");
                    }

                    yield return null;
                }

                if (imageBase64List.Count > 0)
                {
                    arrangementSnapshots.Add(new ArrangementSnapshot
                    {
                        arrangementName = group.Key,
                        imageBase64List = imageBase64List
                    });

                    LogDebug($"Added arrangement group: {group.Key} with {imageBase64List.Count} images");
                }
            }
        }

        private async Task<string> SendRequest(string jsonData)
        {
            try
            {
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                LogDebug("Sending request to: " + serverUrl);
                LogDebug("Request body length: " + jsonData.Length);

                var response = await _httpClient.PostAsync(serverUrl, content);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return responseContent;
                }
                else
                {
                    return $"{{\"status\":\"error\",\"output\":\"HTTP Error: {response.StatusCode}\"}}";
                }
            }
            catch (Exception ex)
            {
                return $"{{\"status\":\"error\",\"output\":\"Exception: {ex.Message}\"}}";
            }
        }

        private void LogDebug(string message)
        {
            if (debugMode)
            {
                Debug.Log($"PythonTrigger: {message}");
                if (debugText != null)
                    debugText.text = message;
            }
        }

        private void OnDestroy()
        {
            _httpClient.Dispose();
        }
    }

    [Serializable]
    public class ManifestData
    {
        public List<string> files;
    }

    [Serializable]
    public class CompletePayload
    {
        public string action;
        public string script_name;
        public CompleteParams @params;
    }

    [Serializable]
    public class CompleteParams
    {
        public string hapticAnnotationJson;
        public List<string> environmentImageBase64List;
        public List<VirtualObjectSnapshot> virtualObjectSnapshots;
        public List<ArrangementSnapshot> arrangementSnapshots;
    }

    [Serializable]
    public class VirtualObjectSnapshot
    {
        public string objectName;
        public string imageBase64;
    }

    [Serializable]
    public class ArrangementSnapshot
    {
        public string arrangementName;
        public List<string> imageBase64List;
    }

    [Serializable]
    public class ResponseData
    {
        public string status;
        public string output;
    }
}