using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

namespace Gameplay
{
    /// <summary>
    /// Represents a target zone where a specific molecule can be placed.
    /// Each slot accepts only one specific molecule ID and provides visual feedback.
    /// </summary>
    [RequireComponent(typeof(CircleCollider2D))]
    public class BrainwaveSlot : MonoBehaviour
    {
        [TitleGroup("Slot Identity")]
        [Tooltip("The molecule ID that this slot accepts")]
        [SerializeField] string acceptedMoleculeId;

        [TitleGroup("Visual Feedback")]
        [Tooltip("Sprite renderer for this slot (optional, for visual feedback)")]
        [SerializeField] SpriteRenderer slotRenderer;

        [TitleGroup("Visual Feedback")]
        [Tooltip("Scale multiplier when molecule is hovering over this slot")]
        [Range(1f, 1.5f)]
        [SerializeField] float hoverScale = 1.1f;

        [TitleGroup("Visual Feedback")]
        [Tooltip("Color tint when molecule is hovering over this slot")]
        [ColorUsage(false)]
        [SerializeField] Color hoverColor = new Color(0.5f, 1f, 0.5f, 0.5f);

        [TitleGroup("Visual Feedback")]
        [Tooltip("Color tint when correctly filled")]
        [ColorUsage(false)]
        [SerializeField] Color filledColor = new Color(0f, 1f, 0f, 0.7f);

        [TitleGroup("Visual Feedback")]
        [Tooltip("Color tint when incorrectly filled")]
        [ColorUsage(false)]
        [SerializeField] Color incorrectColor = new Color(1f, 0.3f, 0.3f, 0.5f);

        [TitleGroup("Events")]
        [SerializeField] UnityEvent onMoleculePlaced;

        [TitleGroup("Events")]
        [SerializeField] UnityEvent onMoleculeRemoved;

        [TitleGroup("Events")]
        [SerializeField] UnityEvent onCorrectMatch;

        [TitleGroup("Debug")]
        [ReadOnly]
        [ShowInInspector]
        PickableMolecule currentMolecule;

        [TitleGroup("Debug")]
        [ReadOnly]
        [ShowInInspector]
        bool isCorrectlyFilled;

        CircleCollider2D triggerZone;
        Color originalColor;
        Vector3 originalScale;

        public string AcceptedMoleculeId => acceptedMoleculeId;
        public PickableMolecule CurrentMolecule => currentMolecule;
        public bool IsCorrectlyFilled => isCorrectlyFilled;
        public bool IsEmpty => currentMolecule == null;

        void Awake()
        {
            triggerZone = GetComponent<CircleCollider2D>();
            triggerZone.isTrigger = true;

            if (slotRenderer != null)
            {
                originalColor = slotRenderer.color;
            }

            originalScale = transform.localScale;
        }

        /// <summary>
        /// Attempts to place a molecule in this slot.
        /// Returns true if the molecule matches the accepted ID.
        /// </summary>
        public bool TryPlaceMolecule(PickableMolecule molecule)
        {
            if (molecule == null) return false;

            // Remove any existing molecule first
            if (currentMolecule != null)
            {
                RemoveMolecule();
            }

            currentMolecule = molecule;
            bool isCorrect = molecule.MoleculeId == acceptedMoleculeId;
            isCorrectlyFilled = isCorrect;

            // Position molecule at slot center
            molecule.PlaceOnSlot(transform.position);

            // Update visual feedback
            UpdateVisuals();

            // Fire events
            onMoleculePlaced?.Invoke();
            if (isCorrect)
            {
                molecule.MarkAsCorrect();
                onCorrectMatch?.Invoke();
            }

            return isCorrect;
        }

        /// <summary>
        /// Removes the current molecule from this slot.
        /// </summary>
        public void RemoveMolecule()
        {
            if (currentMolecule == null) return;

            currentMolecule = null;
            isCorrectlyFilled = false;

            UpdateVisuals();
            onMoleculeRemoved?.Invoke();
        }

        /// <summary>
        /// Visual feedback when a molecule is being dragged over this slot.
        /// </summary>
        public void SetHoverState(bool hovering)
        {
            if (slotRenderer == null) return;

            if (hovering)
            {
                slotRenderer.color = hoverColor;
                transform.localScale = originalScale * hoverScale;
            }
            else
            {
                UpdateVisuals();
            }
        }

        void UpdateVisuals()
        {
            if (slotRenderer == null) return;

            // Reset scale
            transform.localScale = originalScale;

            // Update color based on state
            if (currentMolecule == null)
            {
                slotRenderer.color = originalColor;
            }
            else if (isCorrectlyFilled)
            {
                slotRenderer.color = filledColor;
            }
            else
            {
                slotRenderer.color = incorrectColor;
            }
        }

        void OnValidate()
        {
            // Ensure we have a circle collider
            CircleCollider2D collider = GetComponent<CircleCollider2D>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            // Always show trigger zone in play mode for debugging
            if (Application.isPlaying)
            {
                CircleCollider2D collider = GetComponent<CircleCollider2D>();
                if (collider != null)
                {
                    // Color based on state
                    if (isCorrectlyFilled)
                        Gizmos.color = new Color(0f, 1f, 0f, 0.5f);
                    else if (currentMolecule != null)
                        Gizmos.color = new Color(1f, 0f, 0f, 0.5f);
                    else
                        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);

                    Gizmos.DrawWireSphere(transform.position, collider.radius);
                }
            }
        }

        void OnDrawGizmosSelected()
        {
            // Show trigger zone when selected in edit mode
            CircleCollider2D collider = GetComponent<CircleCollider2D>();
            if (collider != null)
            {
                Gizmos.color = new Color(0f, 1f, 0f, 0.3f);
                Gizmos.DrawWireSphere(transform.position, collider.radius);
                Gizmos.DrawSphere(transform.position, 0.1f);
            }

            // Show accepted molecule ID
            UnityEditor.Handles.Label(
                transform.position + Vector3.up * 0.5f,
                $"Accepts: {acceptedMoleculeId}");
        }
#endif
    }
}
