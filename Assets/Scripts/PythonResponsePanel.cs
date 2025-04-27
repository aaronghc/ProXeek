using UnityEngine;
using UnityEngine.UI;

namespace PythonIntegration
{
    public class PythonResponsePanel : MonoBehaviour
    {
        [SerializeField] private Text titleText;
        [SerializeField] private Text responseText;
        [SerializeField] private GameObject loadingSpinner;
        [SerializeField] private Button closeButton;

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Hide);

            // Start hidden
            gameObject.SetActive(false);
        }

        public void Show(string title = "AI Response")
        {
            gameObject.SetActive(true);

            if (titleText != null)
                titleText.text = title;

            if (responseText != null)
                responseText.text = "Waiting for response...";

            if (loadingSpinner != null)
                loadingSpinner.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        public void UpdateResponse(string response)
        {
            if (responseText != null)
                responseText.text = response;

            if (loadingSpinner != null)
                loadingSpinner.SetActive(false);
        }

        private void OnDestroy()
        {
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Hide);
        }
    }
}