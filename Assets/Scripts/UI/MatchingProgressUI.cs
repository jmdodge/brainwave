using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sirenix.OdinInspector;
using Gameplay;

namespace UI
{
    /// <summary>
    /// Displays progress for the molecule matching puzzle.
    /// Shows fill bar, text progress, and completion feedback.
    /// </summary>
    public class MatchingProgressUI : MonoBehaviour
    {
        [TitleGroup("References")]
        [Required]
        [Tooltip("The MatchingManager to track progress from")]
        [SerializeField] MatchingManager matchingManager;

        [TitleGroup("Progress Display")]
        [Tooltip("Image to use as a fill bar (set to Fill type in Image component)")]
        [SerializeField] Image progressFillBar;

        [TitleGroup("Progress Display")]
        [Tooltip("Text to display progress (e.g., '2/3 Correct')")]
        [SerializeField] TextMeshProUGUI progressText;

        [TitleGroup("Completion Display")]
        [Tooltip("Panel or GameObject to show when puzzle is complete")]
        [SerializeField] GameObject completionPanel;

        [TitleGroup("Completion Display")]
        [Tooltip("Text to display on completion")]
        [SerializeField] TextMeshProUGUI completionText;

        [TitleGroup("Visual Settings")]
        [Tooltip("Color for progress bar when incomplete")]
        [ColorUsage(false)]
        [SerializeField] Color incompleteColor = new Color(1f, 0.5f, 0f, 1f);

        [TitleGroup("Visual Settings")]
        [Tooltip("Color for progress bar when complete")]
        [ColorUsage(false)]
        [SerializeField] Color completeColor = new Color(0f, 1f, 0f, 1f);

        [TitleGroup("Animation")]
        [Tooltip("Animate progress bar changes")]
        [SerializeField] bool animateProgress = true;

        [TitleGroup("Animation")]
        [Tooltip("Speed of progress bar animation")]
        [Range(1f, 20f)]
        [SerializeField] float animationSpeed = 5f;

        float targetFillAmount;
        float currentFillAmount;

        void Awake()
        {
            if (completionPanel != null)
            {
                completionPanel.SetActive(false);
            }
        }

        void OnEnable()
        {
            if (matchingManager != null)
            {
                matchingManager.onProgressChanged.AddListener(OnProgressChanged);
                matchingManager.onPuzzleComplete.AddListener(OnPuzzleComplete);
            }
        }

        void OnDisable()
        {
            if (matchingManager != null)
            {
                matchingManager.onProgressChanged.RemoveListener(OnProgressChanged);
                matchingManager.onPuzzleComplete.RemoveListener(OnPuzzleComplete);
            }
        }

        void Start()
        {
            if (matchingManager == null)
            {
                matchingManager = FindAnyObjectByType<MatchingManager>();
                if (matchingManager == null)
                {
                    Debug.LogWarning("MatchingProgressUI: No MatchingManager assigned or found in scene.", this);
                    return;
                }
            }

            // Initialize display
            UpdateProgressDisplay(matchingManager.CorrectMatches, matchingManager.TotalSlots);
        }

        void Update()
        {
            if (animateProgress && Mathf.Abs(currentFillAmount - targetFillAmount) > 0.001f)
            {
                currentFillAmount = Mathf.Lerp(currentFillAmount, targetFillAmount, Time.deltaTime * animationSpeed);
                ApplyFillAmount(currentFillAmount);
            }
        }

        void OnProgressChanged(int correctCount, int totalCount)
        {
            UpdateProgressDisplay(correctCount, totalCount);
        }

        void UpdateProgressDisplay(int correctCount, int totalCount)
        {
            float progress = totalCount > 0 ? (float)correctCount / totalCount : 0f;

            // Update fill amount
            if (animateProgress)
            {
                targetFillAmount = progress;
            }
            else
            {
                currentFillAmount = progress;
                targetFillAmount = progress;
                ApplyFillAmount(progress);
            }

            // Update text
            if (progressText != null)
            {
                progressText.text = $"{correctCount}/{totalCount} Correct";
            }

            // Update color
            bool isComplete = correctCount == totalCount && totalCount > 0;
            Color barColor = isComplete ? completeColor : incompleteColor;

            if (progressFillBar != null)
            {
                progressFillBar.color = barColor;
            }
        }

        void ApplyFillAmount(float fillAmount)
        {
            if (progressFillBar != null)
            {
                progressFillBar.fillAmount = fillAmount;
            }
        }

        void OnPuzzleComplete()
        {
            if (completionPanel != null)
            {
                completionPanel.SetActive(true);
            }

            if (completionText != null)
            {
                completionText.text = "Puzzle Complete!";
            }

            Debug.Log("MatchingProgressUI: Puzzle completed!");
        }

        /// <summary>
        /// Hides the completion panel (useful for replay/reset).
        /// </summary>
        [Button("Hide Completion Panel")]
        [TitleGroup("Debug")]
        public void HideCompletionPanel()
        {
            if (completionPanel != null)
            {
                completionPanel.SetActive(false);
            }
        }

        void OnValidate()
        {
            if (matchingManager == null)
            {
                matchingManager = FindAnyObjectByType<MatchingManager>();
            }
        }
    }
}
