using UnityEngine;
using Sirenix.OdinInspector;

namespace Gameplay
{
    /// <summary>
    /// Handles drag and drop input for PickableMolecule objects.
    /// Detects clicks, manages currently held molecule, and handles drop logic.
    /// </summary>
    public class DragDropController : MonoBehaviour
    {
        [TitleGroup("References")]
        [Tooltip("Camera used for raycasting. If null, uses Camera.main")]
        [SerializeField] Camera raycastCamera;

        [TitleGroup("Layers")]
        [Tooltip("Layer mask for molecules that can be picked up")]
        [SerializeField] LayerMask moleculeLayer = -1;

        [TitleGroup("Settings")]
        [Tooltip("Z-depth for molecule position when dragging (for 2D)")]
        [SerializeField] float dragDepth = 0f;

        [TitleGroup("Debug")]
        [ReadOnly]
        [ShowInInspector]
        PickableMolecule currentlyHeldMolecule;

        [TitleGroup("Debug")]
        [ReadOnly]
        [ShowInInspector]
        BrainwaveSlot hoveredSlot;

        MatchingManager matchingManager;

        void Awake()
        {
            if (raycastCamera == null)
            {
                raycastCamera = Camera.main;
            }
        }

        void Start()
        {
            matchingManager = FindAnyObjectByType<MatchingManager>();
            if (matchingManager == null)
            {
                Debug.LogWarning("DragDropController: No MatchingManager found in scene.", this);
            }
        }

        void Update()
        {
            HandleInput();
            UpdateHeldMolecule();
            UpdateHoverFeedback();
        }

        void HandleInput()
        {
            // Mouse down - try to pick up molecule
            if (Input.GetMouseButtonDown(0))
            {
                TryPickupMolecule();
            }

            // Mouse up - drop held molecule
            if (Input.GetMouseButtonUp(0))
            {
                TryDropMolecule();
            }
        }

        void TryPickupMolecule()
        {
            if (currentlyHeldMolecule != null) return;

            Vector2 mousePos = raycastCamera.ScreenToWorldPoint(Input.mousePosition);
            RaycastHit2D hit = Physics2D.Raycast(mousePos, Vector2.zero, Mathf.Infinity, moleculeLayer);

            if (hit.collider != null)
            {
                PickableMolecule molecule = hit.collider.GetComponent<PickableMolecule>();
                if (molecule != null)
                {
                    // If molecule is already placed, notify manager to remove it from slot
                    if (molecule.IsPlaced && matchingManager != null)
                    {
                        matchingManager.RemoveMoleculeFromSlot(molecule);
                    }

                    currentlyHeldMolecule = molecule;
                    molecule.Pickup();
                }
            }
        }

        void TryDropMolecule()
        {
            if (currentlyHeldMolecule == null) return;

            // Check if we're over a valid slot
            if (hoveredSlot != null && matchingManager != null)
            {
                matchingManager.TryPlaceMolecule(currentlyHeldMolecule, hoveredSlot);
            }
            else
            {
                // Not over a slot, return to origin
                currentlyHeldMolecule.ReturnToOrigin();
            }

            currentlyHeldMolecule = null;
            hoveredSlot = null;
        }

        void UpdateHeldMolecule()
        {
            if (currentlyHeldMolecule == null) return;

            // Update target position to follow mouse
            Vector3 mouseWorldPos = raycastCamera.ScreenToWorldPoint(Input.mousePosition);
            mouseWorldPos.z = dragDepth;
            currentlyHeldMolecule.UpdateTargetPosition(mouseWorldPos);
        }

        void UpdateHoverFeedback()
        {
            if (currentlyHeldMolecule == null) return;

            // Check if molecule is over a slot
            BrainwaveSlot newHoveredSlot = GetSlotUnderMolecule();

            if (newHoveredSlot != hoveredSlot)
            {
                // Exited previous slot
                if (hoveredSlot != null)
                {
                    hoveredSlot.SetHoverState(false);
                    currentlyHeldMolecule.SetHoverState(false);
                }

                // Entered new slot
                hoveredSlot = newHoveredSlot;
                if (hoveredSlot != null)
                {
                    hoveredSlot.SetHoverState(true);
                    currentlyHeldMolecule.SetHoverState(true);
                }
            }
        }

        BrainwaveSlot GetSlotUnderMolecule()
        {
            if (currentlyHeldMolecule == null) return null;

            // Get the molecule's collider
            Collider2D moleculeCollider = currentlyHeldMolecule.GetComponent<Collider2D>();
            if (moleculeCollider == null)
            {
                Debug.LogWarning($"PickableMolecule {currentlyHeldMolecule.name} has no Collider2D!", this);
                return null;
            }

            // Use ContactFilter2D for overlap testing
            ContactFilter2D contactFilter = new ContactFilter2D();
            contactFilter.useTriggers = true; // Include trigger colliders (slots)
            contactFilter.useLayerMask = false;

            // Find all colliders overlapping with the molecule's collider
            Collider2D[] results = new Collider2D[10]; // Max 10 overlaps
            int count = moleculeCollider.Overlap(contactFilter, results);

            // Find the closest slot if multiple are detected
            BrainwaveSlot closestSlot = null;
            float closestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = results[i];
                if (hit == moleculeCollider) continue; // Skip self

                BrainwaveSlot slot = hit.GetComponent<BrainwaveSlot>();
                if (slot != null)
                {
                    float distance = Vector2.Distance(
                        moleculeCollider.transform.position,
                        hit.transform.position);

                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestSlot = slot;
                    }
                }
            }

            return closestSlot;
        }

        void OnValidate()
        {
            if (raycastCamera == null)
            {
                raycastCamera = Camera.main;
            }
        }

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            // Draw line to hovered slot when molecule is held
            if (currentlyHeldMolecule != null && hoveredSlot != null)
            {
                Gizmos.color = Color.green;
                Gizmos.DrawLine(
                    currentlyHeldMolecule.transform.position,
                    hoveredSlot.transform.position);

                // Draw connection indicator
                Gizmos.DrawWireSphere(hoveredSlot.transform.position, 0.2f);
            }
        }
#endif
    }
}
