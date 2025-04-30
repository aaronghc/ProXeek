using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using PassthroughCameraSamples.CameraToWorld;

namespace PythonIntegration
{
    public class PythonTriggerHandler : MonoBehaviour
    {
        [Header("Server Configuration")]
        [SerializeField] private string serverUrl = "http://172.28.194.245:5000/run_python";
        [SerializeField] private float cooldownTime = 2.0f;
        [SerializeField] private int maxImagesToSend = 5; // Limit number of images to send

        [Header("UI References")]
        [SerializeField] private Text responseText;
        [SerializeField] private GameObject loadingIndicator;

        [Header("Snapshot References")]
        [SerializeField] private SnapshotManager snapshotManager;

        [Header("Haptic Requirements")]
        [SerializeField, TextArea(3, 5)] private string hapticRequirements = "Find objects that could serve as haptic proxies for a virtual pistol. The pistol has a grip, trigger, and barrel.";

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
            _httpClient.Timeout = TimeSpan.FromSeconds(300);

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
                StartCoroutine(ProcessSnapshots());
            }

            _wasRightTriggerPressed = isRightTriggerPressed;
        }

        private IEnumerator ProcessSnapshots()
        {
            _isCoolingDown = true;
            _isProcessing = true;

            if (loadingIndicator != null)
                loadingIndicator.SetActive(true);

            if (responseText != null)
                responseText.text = "Processing snapshots...";

            // Get the snapshots
            string snapshotsFolder = Path.Combine(Application.persistentDataPath, "Snapshots");
            LogDebug("Looking for snapshots in: " + snapshotsFolder);

            if (!Directory.Exists(snapshotsFolder))
            {
                if (responseText != null)
                    responseText.text = "No snapshots folder found. Take some snapshots first.";

                _isProcessing = false;
                yield return new WaitForSeconds(cooldownTime);
                _isCoolingDown = false;
                yield break;
            }

            // Get all snapshot files
            string[] snapshotFiles = Directory.GetFiles(snapshotsFolder, "*.png");
            if (snapshotFiles.Length == 0)
            {
                if (responseText != null)
                    responseText.text = "No snapshots found. Take some snapshots first.";

                _isProcessing = false;
                yield return new WaitForSeconds(cooldownTime);
                _isCoolingDown = false;
                yield break;
            }

            // Sort by creation time (newest first)
            Array.Sort(snapshotFiles, (a, b) => File.GetCreationTime(b).CompareTo(File.GetCreationTime(a)));

            // Limit the number of images to send
            int imagesToSend = Mathf.Min(snapshotFiles.Length, maxImagesToSend);
            LogDebug($"Found {snapshotFiles.Length} snapshots, sending {imagesToSend}");

            // Convert images to base64
            List<string> base64Images = new List<string>();
            for (int i = 0; i < imagesToSend; i++)
            {
                byte[] imageArray = File.ReadAllBytes(snapshotFiles[i]);
                string base64Image = Convert.ToBase64String(imageArray);
                base64Images.Add(base64Image);
                LogDebug($"Converted image {i + 1}/{imagesToSend} to base64 (length: {base64Image.Length})");

                // Yield to prevent frame drops during processing
                yield return null;
            }

            // Create the JSON payload
            var payload = new SnapshotPayload
            {
                action = "run_script",
                script_name = "ProXeek.py",
                @params = new SnapshotParams
                {
                    hapticRequirements = hapticRequirements,
                    imageBase64List = base64Images
                }
            };

            string jsonPayload = JsonUtility.ToJson(payload);
            LogDebug($"Created JSON payload with {base64Images.Count} images");

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
    public class SnapshotPayload
    {
        public string action;
        public string script_name;
        public SnapshotParams @params;
    }

    [Serializable]
    public class SnapshotParams
    {
        public string hapticRequirements;
        public List<string> imageBase64List;
    }

    [Serializable]
    public class ResponseData
    {
        public string status;
        public string output;
    }
}