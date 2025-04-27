using System;
using System.Collections;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace PythonIntegration
{
    public class PythonTriggerHandler : MonoBehaviour
    {
        [Header("Server Configuration")]
        [SerializeField] private string serverUrl = "http://172.28.194.245:5000/run_python";
        [SerializeField] private float cooldownTime = 2.0f; // Prevent rapid firing

        [Header("UI References")]
        [SerializeField] private Text responseText; // Text component to display results
        [SerializeField] private GameObject loadingIndicator; // Optional loading spinner

        [Header("Debug")]
        [SerializeField] private bool debugMode = true;
        [SerializeField] private Text debugText;

        private bool _isCoolingDown = false;
        private HttpClient _httpClient;
        private bool _isProcessing = false;

        // Threshold for trigger press
        private const float TRIGGER_THRESHOLD = 0.7f;
        private bool _wasRightTriggerPressed = false;

        private void Awake()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30); // Longer timeout for LLM processing

            if (loadingIndicator != null)
                loadingIndicator.SetActive(false);

            LogDebug("Python Trigger Handler initialized");
        }

        private void Update()
        {
            // Check for right hand trigger press
            float rightTriggerValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger);
            bool isRightTriggerPressed = rightTriggerValue > TRIGGER_THRESHOLD;

            // Detect trigger press (not hold)
            if (isRightTriggerPressed && !_wasRightTriggerPressed && !_isCoolingDown && !_isProcessing)
            {
                LogDebug("Trigger pressed: " + rightTriggerValue);
                StartCoroutine(TriggerPythonExecution());
            }

            _wasRightTriggerPressed = isRightTriggerPressed;
        }

        private IEnumerator TriggerPythonExecution()
        {
            _isCoolingDown = true;
            _isProcessing = true;

            if (loadingIndicator != null)
                loadingIndicator.SetActive(true);

            if (responseText != null)
                responseText.text = "Processing request...";

            LogDebug("Sending request to server...");

            // Create request data - using anonymous type with proper naming
            string jsonData = JsonUtility.ToJson(new RequestData
            {
                action = "run_script",
                scriptName = "new_python_script.py",
                parameters = new ScriptParameters
                {
                    prompt = "What's the meaning of life according to different philosophies?"
                }
            });

            // Use Task to handle the HTTP request asynchronously
            Task<string> responseTask = SendRequest(jsonData);

            // Wait for the response without blocking the main thread
            while (!responseTask.IsCompleted)
            {
                yield return null;
            }

            // Process the response
            if (responseTask.IsFaulted)
            {
                LogDebug("Error: " + responseTask.Exception.Message);
                if (responseText != null)
                    responseText.text = "Error: Could not connect to server";
            }
            else
            {
                string response = responseTask.Result;
                LogDebug("Response received: " + (response.Length > 50 ? response.Substring(0, 50) + "..." : response));

                if (responseText != null)
                    responseText.text = response;
            }

            if (loadingIndicator != null)
                loadingIndicator.SetActive(false);

            _isProcessing = false;

            // Start cooldown timer
            yield return new WaitForSeconds(cooldownTime);
            _isCoolingDown = false;
        }

        private async Task<string> SendRequest(string jsonData)
        {
            try
            {
                var content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync(serverUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
                else
                {
                    return $"Error: {response.StatusCode}";
                }
            }
            catch (Exception ex)
            {
                return $"Exception: {ex.Message}";
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

    // Add these serializable classes for JSON conversion
    [Serializable]
    public class RequestData
    {
        public string action;
        public string scriptName;
        public ScriptParameters parameters;
    }

    [Serializable]
    public class ScriptParameters
    {
        public string prompt;
    }
}