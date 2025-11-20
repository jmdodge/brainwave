using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Gameplay
{
    /// <summary>
    /// Represents a molecule that can be picked up and placed on matching brainwave slots.
    /// Each molecule has a unique ID and plays its own step sequencer when picked up.
    /// </summary>
    [RequireComponent(typeof(Collider2D))]
    public class PickableMolecule : MonoBehaviour
    {
        [TitleGroup("Molecule Identity")]
        [Tooltip("Unique identifier for this molecule (e.g., 'alpha', 'beta', 'theta')")]
        [SerializeField] string moleculeId;

        [TitleGroup("Audio")]
        [Tooltip("Step sequencer to play when this molecule is picked up")]
        [SerializeField] StepSequencer stepSequencer;

        [TitleGroup("Visuals")]
        [Tooltip("Optional molecule visualizer for procedural animations")]
        [SerializeField] MoleculeVisualizer moleculeVisualizer;

        [TitleGroup("Movement")]
        [Tooltip("Speed at which molecule springs toward cursor")]
        [Range(1f, 20f)]
        [SerializeField] float followSpeed = 10f;

        [TitleGroup("Movement")]
        [Tooltip("Speed at which molecule returns to origin")]
        [Range(1f, 20f)]
        [SerializeField] float returnSpeed = 8f;

        [TitleGroup("Visual Feedback")]
        [Tooltip("Scale multiplier when molecule is picked up")]
        [Range(1f, 2f)]
        [SerializeField] float pickupScale = 1.2f;

        [TitleGroup("Visual Feedback")]
        [Tooltip("Color tint when molecule is picked up")]
        [ColorUsage(false)]
        [SerializeField] Color pickupTint = new Color(1f, 1f, 1f, 1f);

        [TitleGroup("Visual Feedback")]
        [Tooltip("Color tint when molecule is hovering over valid slot")]
        [ColorUsage(false)]
        [SerializeField] Color hoverTint = new Color(0.5f, 1f, 0.5f, 1f);

        [TitleGroup("Events")]
        [SerializeField] UnityEvent onPickup;

        [TitleGroup("Events")]
        [SerializeField] UnityEvent onDrop;

        [TitleGroup("Events")]
        [SerializeField] UnityEvent onPlacedCorrectly;

        Vector3 originalPosition;
        Vector3 targetPosition;
        Vector3 velocity;
        SpriteRenderer spriteRenderer;
        Color originalColor;
        Vector3 originalScale;
        bool isPickedUp;
        bool isReturning;
        bool isPlaced;

        public string MoleculeId => moleculeId;
        public bool IsPickedUp => isPickedUp;
        public bool IsPlaced => isPlaced;

        void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                originalColor = spriteRenderer.color;
            }

            originalScale = transform.localScale;
            originalPosition = transform.position;

            if (stepSequencer == null)
            {
                stepSequencer = GetComponent<StepSequencer>();
            }

            if (moleculeVisualizer == null)
            {
                moleculeVisualizer = GetComponentInChildren<MoleculeVisualizer>();
            }
        }

        void Update()
        {
            if (isPickedUp)
            {
                // Spring toward target position
                transform.position = Vector3.SmoothDamp(
                    transform.position,
                    targetPosition,
                    ref velocity,
                    1f / followSpeed);
            }
            else if (isReturning)
            {
                // Return to original position
                transform.position = Vector3.MoveTowards(
                    transform.position,
                    originalPosition,
                    returnSpeed * Time.deltaTime);

                if (Vector3.Distance(transform.position, originalPosition) < 0.01f)
                {
                    transform.position = originalPosition;
                    isReturning = false;
                }
            }
        }

        /// <summary>
        /// Called when the molecule is picked up by the player.
        /// </summary>
        public void Pickup()
        {
            if (isPickedUp) return;

            isPickedUp = true;
            isReturning = false;
            isPlaced = false;

            // Visual feedback
            if (spriteRenderer != null)
            {
                spriteRenderer.color = pickupTint;
            }
            transform.localScale = originalScale * pickupScale;

            // Start step sequencer
            if (stepSequencer != null)
            {
                stepSequencer.StartSequence();
            }

            // Notify visualizer to accelerate animations
            if (moleculeVisualizer != null)
            {
                moleculeVisualizer.SetPickedUpState(true);
            }

            onPickup?.Invoke();
        }

        /// <summary>
        /// Updates the target position for the molecule to follow.
        /// </summary>
        public void UpdateTargetPosition(Vector3 position)
        {
            targetPosition = position;
        }

        /// <summary>
        /// Called when the molecule is dropped on a valid slot.
        /// </summary>
        public void PlaceOnSlot(Vector3 slotPosition)
        {
            isPickedUp = false;
            isPlaced = true;
            transform.position = slotPosition;

            // Reset visual feedback
            if (spriteRenderer != null)
            {
                spriteRenderer.color = originalColor;
            }
            transform.localScale = originalScale;

            // Keep sequencer playing but reset visualizer state
            if (moleculeVisualizer != null)
            {
                moleculeVisualizer.SetPickedUpState(false);
            }

            onDrop?.Invoke();
        }

        /// <summary>
        /// Called when the molecule is placed correctly and validated.
        /// </summary>
        public void MarkAsCorrect()
        {
            onPlacedCorrectly?.Invoke();
        }

        /// <summary>
        /// Called when the molecule is dropped in an invalid location.
        /// Returns to original position.
        /// </summary>
        public void ReturnToOrigin()
        {
            isPickedUp = false;
            isReturning = true;
            isPlaced = false;

            // Reset visual feedback
            if (spriteRenderer != null)
            {
                spriteRenderer.color = originalColor;
            }
            transform.localScale = originalScale;

            // Stop step sequencer
            if (stepSequencer != null)
            {
                stepSequencer.StopSequence();
            }

            // Reset visualizer state
            if (moleculeVisualizer != null)
            {
                moleculeVisualizer.SetPickedUpState(false);
            }

            onDrop?.Invoke();
        }

        /// <summary>
        /// Called when the molecule needs to be removed from its current slot.
        /// Used when swapping molecules.
        /// </summary>
        public void RemoveFromSlot()
        {
            isPlaced = false;
            ReturnToOrigin();
        }

        /// <summary>
        /// Visual feedback when hovering over a valid slot.
        /// </summary>
        public void SetHoverState(bool hovering)
        {
            if (!isPickedUp || spriteRenderer == null) return;

            spriteRenderer.color = hovering ? hoverTint : pickupTint;
        }

        void OnValidate()
        {
            // Auto-find step sequencer if not assigned
            if (stepSequencer == null)
            {
                stepSequencer = GetComponent<StepSequencer>();
            }

            // Auto-find molecule visualizer if not assigned
            if (moleculeVisualizer == null)
            {
                moleculeVisualizer = GetComponentInChildren<MoleculeVisualizer>();
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            // Show original position in editor
            Gizmos.color = Color.yellow;
            Vector3 origin = Application.isPlaying ? originalPosition : transform.position;
            Gizmos.DrawWireSphere(origin, 0.2f);
        }
#endif
    }
}
