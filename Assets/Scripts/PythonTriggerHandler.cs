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
            _httpClient.Timeout = TimeSpan.FromSeconds(300); // Longer timeout for LLM processing

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

            // Create the JSON payload manually to ensure correct structure
            string jsonPayload = @"{
                ""action"": ""run_script"",
                ""script_name"": ""new_python_script.py"",
                ""params"": {
                    ""prompt"": ""tell me a joke""
                }
            }";

            // Use Task to handle the HTTP request asynchronously
            Task<string> responseTask = SendRequest(jsonPayload);

            // Wait for the response without blocking the main thread
            while (!responseTask.IsCompleted)
            {
                yield return null;
            }

            // Process the response
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

                // Try to parse the JSON response
                try
                {
                    ResponseData responseData = JsonUtility.FromJson<ResponseData>(response);
                    string displayText = responseData.output;

                    if (responseText != null)
                        responseText.text = displayText;
                }
                catch (Exception ex)
                {
                    // If parsing fails, just display the raw response
                    if (responseText != null)
                        responseText.text = response;

                    LogDebug("Failed to parse response: " + ex.Message);
                }
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

                // Log the request for debugging
                LogDebug("Sending request to: " + serverUrl);
                LogDebug("Request body: " + jsonData);

                var response = await _httpClient.PostAsync(serverUrl, content);

                string responseContent = await response.Content.ReadAsStringAsync();
                LogDebug("Raw response: " + responseContent);

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

    // Add these serializable classes for JSON conversion
    [Serializable]
    public class ResponseData
    {
        public string status;
        public string output;
    }
}