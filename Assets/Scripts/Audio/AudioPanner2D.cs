using UnityEngine;
using Sirenix.OdinInspector;

namespace Audio
{
    /// <summary>
    /// Automatically adjusts stereo panning based on 2D horizontal position.
    /// Maps world X position to audio panStereo (-1 = left, 1 = right).
    /// Attach to any GameObject with an AudioSource for automatic stereo positioning.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class AudioPanner2D : MonoBehaviour
    {
        [TitleGroup("Panning Boundaries")]
        [Tooltip("World X position that maps to full left pan (-1)")]
        [SerializeField] float worldXMin = -5f;

        [TitleGroup("Panning Boundaries")]
        [Tooltip("World X position that maps to full right pan (1)")]
        [SerializeField] float worldXMax = 5f;

        [TitleGroup("Settings")]
        [Tooltip("Strength of panning effect (0 = center only, 1 = full stereo)")]
        [Range(0f, 1f)]
        [SerializeField] float panStrength = 1f;

        [TitleGroup("Settings")]
        [Tooltip("Smoothing factor for pan changes (0 = instant, higher = smoother)")]
        [Range(0f, 1f)]
        [SerializeField] float panSmoothing = 0.1f;

        [TitleGroup("Debug")]
        [ReadOnly]
        [ShowInInspector]
        float currentPan;

        AudioSource audioSource;
        float targetPan;

        /**
         * Initialize and ensure 2D audio mode.
         * Sets spatialBlend to 0 to disable 3D audio calculations.
         */
        void Awake()
        {
            audioSource = GetComponent<AudioSource>();

            // Force 2D audio mode for proper stereo panning
            if (audioSource != null)
            {
                audioSource.spatialBlend = 0f;
            }
        }

        /**
         * Update stereo panning based on current X position.
         * Maps horizontal position to panStereo range with optional smoothing.
         */
        void Update()
        {
            if (audioSource == null) return;

            // Calculate normalized position (0 = left boundary, 1 = right boundary)
            float normalizedX = Mathf.InverseLerp(worldXMin, worldXMax, transform.position.x);

            // Map to pan range: -1 (left) to 1 (right)
            targetPan = Mathf.Lerp(-1f, 1f, normalizedX);

            // Apply pan strength (allows for subtle panning if desired)
            targetPan *= panStrength;

            // Apply smoothing for gradual pan changes
            if (panSmoothing > 0f)
            {
                currentPan = Mathf.Lerp(currentPan, targetPan, Time.deltaTime / panSmoothing);
            }
            else
            {
                currentPan = targetPan;
            }

            // Update audio source
            audioSource.panStereo = currentPan;
        }

        /**
         * Visualize panning boundaries in Scene view.
         * Shows min/max X positions and current object position.
         */
#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            // Draw min boundary (left = red)
            Gizmos.color = Color.red;
            Vector3 minPos = new Vector3(worldXMin, transform.position.y, transform.position.z);
            Gizmos.DrawLine(minPos + Vector3.up * 0.5f, minPos - Vector3.up * 0.5f);
            UnityEditor.Handles.Label(minPos + Vector3.up * 0.7f, "Left (-1)");

            // Draw max boundary (right = green)
            Gizmos.color = Color.green;
            Vector3 maxPos = new Vector3(worldXMax, transform.position.y, transform.position.z);
            Gizmos.DrawLine(maxPos + Vector3.up * 0.5f, maxPos - Vector3.up * 0.5f);
            UnityEditor.Handles.Label(maxPos + Vector3.up * 0.7f, "Right (1)");

            // Draw center boundary (middle = yellow)
            Gizmos.color = Color.yellow;
            float centerX = (worldXMin + worldXMax) / 2f;
            Vector3 centerPos = new Vector3(centerX, transform.position.y, transform.position.z);
            Gizmos.DrawLine(centerPos + Vector3.up * 0.5f, centerPos - Vector3.up * 0.5f);
            UnityEditor.Handles.Label(centerPos + Vector3.up * 0.7f, "Center (0)");

            // Draw current position indicator
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, 0.2f);
        }
#endif

        /**
         * Validate boundaries when edited in inspector.
         * Ensures max is always greater than min.
         */
        void OnValidate()
        {
            if (worldXMax <= worldXMin)
            {
                worldXMax = worldXMin + 1f;
            }
        }
    }
}
