using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Sirenix.OdinInspector;

namespace UI
{
    /// <summary>
    /// Visualizes the TempoManager's transport state and current beat position.
    /// Shows bar number, beat number, and visual beat indicators.
    /// </summary>
    [AddComponentMenu("UI/Tempo Visualizer")]
    public class TempoVisualizer : MonoBehaviour
    {
        [Required]
        [SerializeField] TempoManager tempoManager;

        [TitleGroup("Text Displays")]
        [Tooltip("Text field to display transport status (Running/Stopped)")]
        [SerializeField] TextMeshProUGUI transportStatusText;

        [TitleGroup("Text Displays")]
        [Tooltip("Text field to display current bar number")]
        [SerializeField] TextMeshProUGUI barNumberText;

        [TitleGroup("Text Displays")]
        [Tooltip("Text field to display current beat in bar")]
        [SerializeField] TextMeshProUGUI beatNumberText;

        [TitleGroup("Text Displays")]
        [Tooltip("Text field to display time signature")]
        [SerializeField] TextMeshProUGUI timeSignatureText;

        [TitleGroup("Visual Beat Indicators")]
        [Tooltip("Array of images to represent each beat in the bar. Will highlight the current beat.")]
        [SerializeField] Image[] beatIndicators;

        [TitleGroup("Visual Beat Indicators")]
        [ColorUsage(false)]
        [SerializeField] Color activeBeatColor = Color.green;

        [TitleGroup("Visual Beat Indicators")]
        [ColorUsage(false)]
        [SerializeField] Color inactiveBeatColor = new Color(0.3f, 0.3f, 0.3f, 1f);

        [TitleGroup("Visual Beat Indicators")]
        [ColorUsage(false)]
        [SerializeField] Color downbeatColor = Color.cyan;

        [TitleGroup("Status Colors")]
        [ColorUsage(false)]
        [SerializeField] Color runningColor = Color.green;

        [TitleGroup("Status Colors")]
        [ColorUsage(false)]
        [SerializeField] Color stoppedColor = Color.red;

        int lastBeatInBar = -1;

        void OnValidate()
        {
            if (tempoManager == null)
            {
                tempoManager = FindAnyObjectByType<TempoManager>();
            }
        }

        void Update()
        {
            if (tempoManager == null) return;

            UpdateTransportStatus();
            UpdateBarNumber();
            UpdateBeatNumber();
            UpdateTimeSignature();
            UpdateBeatIndicators();
        }

        void UpdateTransportStatus()
        {
            if (transportStatusText == null) return;

            bool isRunning = tempoManager.TransportRunning;
            transportStatusText.text = isRunning ? "Running" : "Stopped";
            transportStatusText.color = isRunning ? runningColor : stoppedColor;
        }

        void UpdateBarNumber()
        {
            if (barNumberText == null) return;
            barNumberText.text = $"Bar: {tempoManager.CurrentBar}";
        }

        void UpdateBeatNumber()
        {
            if (beatNumberText == null) return;
            beatNumberText.text = $"Beat: {tempoManager.CurrentBeatInBar}";
        }

        void UpdateTimeSignature()
        {
            if (timeSignatureText == null) return;
            timeSignatureText.text = $"{tempoManager.BeatsPerBar}/{tempoManager.TimeSignatureDenominator}";
        }

        void UpdateBeatIndicators()
        {
            if (beatIndicators == null || beatIndicators.Length == 0) return;

            int currentBeat = tempoManager.TransportRunning ? tempoManager.CurrentBeatInBar : 0;
            int beatsPerBar = tempoManager.BeatsPerBar;

            // Update colors for all beat indicators
            for (int i = 0; i < beatIndicators.Length; i++)
            {
                if (beatIndicators[i] == null) continue;

                // Hide indicators beyond the current time signature
                if (i >= beatsPerBar)
                {
                    beatIndicators[i].gameObject.SetActive(false);
                    continue;
                }

                beatIndicators[i].gameObject.SetActive(true);

                // Determine color based on whether this is the current beat
                bool isCurrentBeat = (i + 1) == currentBeat && tempoManager.TransportRunning;
                bool isDownbeat = i == 0;

                if (isCurrentBeat)
                {
                    beatIndicators[i].color = isDownbeat ? downbeatColor : activeBeatColor;
                }
                else
                {
                    beatIndicators[i].color = inactiveBeatColor;
                }
            }

            lastBeatInBar = currentBeat;
        }
    }
}
