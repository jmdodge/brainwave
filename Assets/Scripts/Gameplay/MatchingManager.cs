using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Gameplay
{
    /// <summary>
    /// Manages the molecule matching puzzle game logic.
    /// Tracks all slots and molecules, validates placements, and fires completion events.
    /// </summary>
    public class MatchingManager : MonoBehaviour
    {
        [TitleGroup("Setup")]
        [Tooltip("Auto-register all BrainwaveSlot components in scene on start")]
        [SerializeField] bool autoRegisterSlots = true;

        [TitleGroup("Setup")]
        [Tooltip("Auto-register all PickableMolecule components in scene on start")]
        [SerializeField] bool autoRegisterMolecules = true;

        [TitleGroup("Setup")]
        [Tooltip("Manual list of slots (used if autoRegisterSlots is false)")]
        [HideIf(nameof(autoRegisterSlots))]
        [SerializeField] List<BrainwaveSlot> slots = new();

        [TitleGroup("Setup")]
        [Tooltip("Manual list of molecules (used if autoRegisterMolecules is false)")]
        [HideIf(nameof(autoRegisterMolecules))]
        [SerializeField] List<PickableMolecule> molecules = new();

        [TitleGroup("Events")]
        [Tooltip("Fired when a molecule is placed correctly")]
        public UnityEvent<string> onCorrectPlacement;

        [TitleGroup("Events")]
        [Tooltip("Fired when a molecule is placed incorrectly")]
        public UnityEvent<string> onIncorrectPlacement;

        [TitleGroup("Events")]
        [Tooltip("Fired when progress changes")]
        public UnityEvent<int, int> onProgressChanged; // (correct count, total count)

        [TitleGroup("Events")]
        [Tooltip("Fired when all molecules are correctly placed")]
        public UnityEvent onPuzzleComplete;

        [TitleGroup("Debug")]
        [ReadOnly]
        [ShowInInspector]
        int correctMatches;

        [TitleGroup("Debug")]
        [ReadOnly]
        [ShowInInspector]
        int totalSlots;

        [TitleGroup("Debug")]
        [ShowInInspector]
        [ReadOnly]
        bool isPuzzleComplete;

        public int CorrectMatches => correctMatches;
        public int TotalSlots => totalSlots;
        public bool IsPuzzleComplete => isPuzzleComplete;

        void Start()
        {
            if (autoRegisterSlots)
            {
                slots = new List<BrainwaveSlot>(FindObjectsByType<BrainwaveSlot>(FindObjectsSortMode.None));
            }

            if (autoRegisterMolecules)
            {
                molecules = new List<PickableMolecule>(FindObjectsByType<PickableMolecule>(FindObjectsSortMode.None));
            }

            totalSlots = slots.Count;

            if (totalSlots == 0)
            {
                Debug.LogWarning("MatchingManager: No slots registered. Puzzle cannot be completed.", this);
            }

            UpdateProgress();
        }

        /// <summary>
        /// Attempts to place a molecule in a slot.
        /// Validates the match and updates progress.
        /// </summary>
        public void TryPlaceMolecule(PickableMolecule molecule, BrainwaveSlot slot)
        {
            if (molecule == null || slot == null) return;

            bool isCorrect = slot.TryPlaceMolecule(molecule);

            if (isCorrect)
            {
                onCorrectPlacement?.Invoke(molecule.MoleculeId);
            }
            else
            {
                onIncorrectPlacement?.Invoke(molecule.MoleculeId);
            }

            UpdateProgress();
        }

        /// <summary>
        /// Removes a molecule from its current slot (for swapping).
        /// </summary>
        public void RemoveMoleculeFromSlot(PickableMolecule molecule)
        {
            if (molecule == null) return;

            foreach (BrainwaveSlot slot in slots)
            {
                if (slot.CurrentMolecule == molecule)
                {
                    slot.RemoveMolecule();
                    UpdateProgress();
                    return;
                }
            }
        }

        /// <summary>
        /// Updates progress tracking and checks for puzzle completion.
        /// </summary>
        void UpdateProgress()
        {
            correctMatches = 0;

            foreach (BrainwaveSlot slot in slots)
            {
                if (slot.IsCorrectlyFilled)
                {
                    correctMatches++;
                }
            }

            onProgressChanged?.Invoke(correctMatches, totalSlots);

            // Check for completion
            bool wasComplete = isPuzzleComplete;
            isPuzzleComplete = correctMatches == totalSlots && totalSlots > 0;

            if (isPuzzleComplete && !wasComplete)
            {
                OnPuzzleCompleted();
            }
        }

        /// <summary>
        /// Called when the puzzle is completed successfully.
        /// </summary>
        void OnPuzzleCompleted()
        {
            Debug.Log("Puzzle Complete! All molecules correctly matched.");
            onPuzzleComplete?.Invoke();
        }

        /// <summary>
        /// Resets the puzzle to its initial state.
        /// </summary>
        [Button("Reset Puzzle", ButtonSizes.Medium)]
        [TitleGroup("Debug")]
        public void ResetPuzzle()
        {
            // Remove all molecules from slots
            foreach (BrainwaveSlot slot in slots)
            {
                if (!slot.IsEmpty)
                {
                    PickableMolecule molecule = slot.CurrentMolecule;
                    slot.RemoveMolecule();
                    molecule.RemoveFromSlot();
                }
            }

            isPuzzleComplete = false;
            UpdateProgress();
        }

        /// <summary>
        /// Gets progress as a normalized value (0-1).
        /// </summary>
        public float GetProgressNormalized()
        {
            return totalSlots > 0 ? (float)correctMatches / totalSlots : 0f;
        }

#if UNITY_EDITOR
        [Button("Auto-Setup")]
        [TitleGroup("Setup")]
        void EditorAutoSetup()
        {
            slots = new List<BrainwaveSlot>(FindObjectsByType<BrainwaveSlot>(FindObjectsSortMode.None));
            molecules = new List<PickableMolecule>(FindObjectsByType<PickableMolecule>(FindObjectsSortMode.None));
            Debug.Log($"Found {slots.Count} slots and {molecules.Count} molecules.");
        }
#endif
    }
}
