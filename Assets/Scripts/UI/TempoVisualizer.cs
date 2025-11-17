using System;
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
        [Required] [SerializeField] TempoManager tempoManager;

        [TitleGroup("Text Displays")]
        [Tooltip("Text field to display transport status (Running/Stopped)")]
        [SerializeField]
        TextMeshProUGUI transportStatusText;

        [TitleGroup("Text Displays")] [Tooltip("Text field to display current bar number")] [SerializeField]
        TextMeshProUGUI barNumberText;

        [TitleGroup("Text Displays")] [Tooltip("Text field to display current beat in bar")] [SerializeField]
        TextMeshProUGUI beatNumberText;

        [TitleGroup("Text Displays")] [Tooltip("Text field to display time signature")] [SerializeField]
        TextMeshProUGUI timeSignatureText;

        [TitleGroup("Visual Beat Indicators")]
        [Tooltip("Array of images to represent each beat in the bar. Will highlight the current beat.")]
        [SerializeField]
        Image[] beatIndicators;

        [TitleGroup("Visual Beat Indicators")]
        [Tooltip("Array of images to represent each eighth note in the bar. Will highlight the current eighth note.")]
        [SerializeField]
        Image[] eighthNoteIndicators;

        [TitleGroup("Visual Beat Indicators")]
        [Tooltip(
            "Array of images to represent each sixteenth note in the bar. Will highlight the current sixteenth note.")]
        [SerializeField]
        Image[] sixteenthNoteIndicators;

        [TitleGroup("Visual Beat Indicators")] [ColorUsage(false)] [SerializeField]
        Color activeBeatColor = Color.green;

        [TitleGroup("Visual Beat Indicators")] [ColorUsage(false)] [SerializeField]
        Color inactiveBeatColor = new Color(0.3f, 0.3f, 0.3f, 1f);

        [TitleGroup("Visual Beat Indicators")] [ColorUsage(false)] [SerializeField]
        Color downbeatColor = Color.cyan;

        [TitleGroup("Visual Beat Indicators")] [ColorUsage(false)] [SerializeField]
        Color activeEighthNoteColor = new Color(0.4f, 0.8f, 0.4f, 1f);

        [TitleGroup("Visual Beat Indicators")] [ColorUsage(false)] [SerializeField]
        Color inactiveEighthNoteColor = new Color(0.25f, 0.25f, 0.25f, 1f);

        [TitleGroup("Visual Beat Indicators")] [ColorUsage(false)] [SerializeField]
        Color activeSixteenthNoteColor = new Color(0.3f, 0.6f, 0.3f, 1f);

        [TitleGroup("Visual Beat Indicators")] [ColorUsage(false)] [SerializeField]
        Color inactiveSixteenthNoteColor = new Color(0.2f, 0.2f, 0.2f, 1f);

        [TitleGroup("Status Colors")] [ColorUsage(false)] [SerializeField]
        Color runningColor = Color.green;

        [TitleGroup("Status Colors")] [ColorUsage(false)] [SerializeField]
        Color stoppedColor = Color.red;

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
            UpdateEighthNoteIndicators();
            UpdateSixteenthNoteIndicators();
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

        /**
         * Updates the visual indicators for eighth notes (subdivisions of beats).
         */
        void UpdateEighthNoteIndicators()
        {
            if (eighthNoteIndicators == null || eighthNoteIndicators.Length == 0) return;

            int beatsPerBar = tempoManager.BeatsPerBar;
            int eighthNotesPerBar = beatsPerBar * 2;

            // Calculate current eighth note position in the bar (0-indexed)
            int currentEighthNote = 0;
            if (tempoManager.TransportRunning)
            {
                double currentBeat = tempoManager.CurrentBeat;
                int absoluteEighthNote = (int)Math.Floor(currentBeat * 2.0);
                currentEighthNote = beatsPerBar > 0 ? Modulo(absoluteEighthNote, eighthNotesPerBar) : 0;
            }

            // Update colors for all eighth note indicators
            for (int i = 0; i < eighthNoteIndicators.Length; i++)
            {
                if (eighthNoteIndicators[i] == null) continue;

                // Hide indicators beyond the current time signature
                if (i >= eighthNotesPerBar)
                {
                    eighthNoteIndicators[i].gameObject.SetActive(false);
                    continue;
                }

                eighthNoteIndicators[i].gameObject.SetActive(true);

                // Determine color based on whether this is the current eighth note
                bool isCurrentEighthNote = i == currentEighthNote && tempoManager.TransportRunning;
                bool isOnBeat = (i % 2) == 0; // Eighth notes that align with beats

                if (isCurrentEighthNote)
                {
                    eighthNoteIndicators[i].color = isOnBeat ? activeBeatColor : activeEighthNoteColor;
                }
                else
                {
                    eighthNoteIndicators[i].color = isOnBeat ? inactiveBeatColor : inactiveEighthNoteColor;
                }
            }
        }

        /**
         * Updates the visual indicators for sixteenth notes (subdivisions of eighth notes).
         */
        void UpdateSixteenthNoteIndicators()
        {
            if (sixteenthNoteIndicators == null || sixteenthNoteIndicators.Length == 0) return;

            int beatsPerBar = tempoManager.BeatsPerBar;
            int sixteenthNotesPerBar = beatsPerBar * 4;

            // Calculate current sixteenth note position in the bar (0-indexed)
            int currentSixteenthNote = 0;
            if (tempoManager.TransportRunning)
            {
                double currentBeat = tempoManager.CurrentBeat;
                int absoluteSixteenthNote = (int)Math.Floor(currentBeat * 4.0);
                currentSixteenthNote = beatsPerBar > 0 ? Modulo(absoluteSixteenthNote, sixteenthNotesPerBar) : 0;
            }

            // Update colors for all sixteenth note indicators
            for (int i = 0; i < sixteenthNoteIndicators.Length; i++)
            {
                if (sixteenthNoteIndicators[i] == null) continue;

                // Hide indicators beyond the current time signature
                if (i >= sixteenthNotesPerBar)
                {
                    sixteenthNoteIndicators[i].gameObject.SetActive(false);
                    continue;
                }

                sixteenthNoteIndicators[i].gameObject.SetActive(true);

                // Determine color based on whether this is the current sixteenth note
                bool isCurrentSixteenthNote = i == currentSixteenthNote && tempoManager.TransportRunning;
                bool isOnBeat = (i % 4) == 0; // Sixteenth notes that align with beats
                bool isOnEighthNote = (i % 2) == 0; // Sixteenth notes that align with eighth notes

                if (isCurrentSixteenthNote)
                {
                    if (isOnBeat)
                        sixteenthNoteIndicators[i].color = activeBeatColor;
                    else if (isOnEighthNote)
                        sixteenthNoteIndicators[i].color = activeEighthNoteColor;
                    else
                        sixteenthNoteIndicators[i].color = activeSixteenthNoteColor;
                }
                else
                {
                    if (isOnBeat)
                        sixteenthNoteIndicators[i].color = inactiveBeatColor;
                    else if (isOnEighthNote)
                        sixteenthNoteIndicators[i].color = inactiveEighthNoteColor;
                    else
                        sixteenthNoteIndicators[i].color = inactiveSixteenthNoteColor;
                }
            }
        }

        /**
         * Returns a mathematically positive modulo result.
         */
        static int Modulo(int value, int modulus)
        {
            if (modulus == 0) return value;
            int result = value % modulus;
            return result < 0 ? result + modulus : result;
        }
    }
}
