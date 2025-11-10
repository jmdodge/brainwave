using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

public class BrainwaveController : MonoBehaviour
{
    [SerializeField] private List<GameObject> rings = new List<GameObject>();
    [SerializeField] private float ringAnimationDuration = 0.4f;
    [SerializeField] private float ringShakeDuration = 0.8f;
    [SerializeField] private float ringShakeStrength = 0.1f;
    [SerializeField] private float controllerCollapseDuration = 0.3f;
    [SerializeField] private UnityEvent onAnimationComplete;
    [Header("Quantized Trigger")]
    [SerializeField] private bool quantizeToBeat;
    [SerializeField] private float quantizationBeats = 1f;
    [SerializeField] private TempoManager tempoManager;
    private readonly List<Vector3> ringInitialScales = new List<Vector3>();
    private Sequence ringSequence;
    private Vector3 controllerInitialScale;
    private bool isClicking = false;
    private bool triggerScheduled;
    private TempoManager.TempoEventHandle triggerHandle;
    private Vector3 pendingClickPosition;

    /**
     * Initializes the controller by caching initial scales and hiding it.
     */
    void Start()
    {
        CacheInitialScales();
        transform.localScale = Vector3.zero;
    }

    /**
     * Entry point for click events. Handles quantization logic:
     * - If quantization is enabled and transport is running, schedules the animation for the next beat boundary.
     * - Otherwise, fires the animation immediately.
     * - If already clicking and a scheduled trigger exists, cancels it to reschedule.
     */
    public void OnClickEvent(Vector3 clickedPosition)
    {
        // If already animating, only allow rescheduling if quantization is enabled
        if (isClicking)
        {
            if (!quantizeToBeat || !triggerScheduled) return;
            CancelScheduledTrigger();
        }

        pendingClickPosition = clickedPosition;

        // Fire immediately if quantization is disabled or transport isn't running
        if (!quantizeToBeat || tempoManager == null || !tempoManager.TransportRunning)
        {
            FireClickSequence();
            return;
        }

        // Calculate the next quantization boundary (e.g., next beat or next bar)
        float quant = Mathf.Max(1e-6f, quantizationBeats);
        double currentBeat = tempoManager.CurrentBeat;
        double cycles = Mathf.Floor((float)(currentBeat / quant));
        double targetBeat = (cycles + 1d) * quant;
        if (targetBeat - currentBeat < 1e-4) targetBeat += quant;

        // Schedule the animation to fire at the target beat
        triggerScheduled = true;
        triggerHandle = tempoManager.ScheduleAtBeat(targetBeat, FireClickSequence);
    }

    /**
     * Executes the click animation sequence:
     * 1. Sets up the controller at the clicked position
     * 2. Builds a staggered ring animation sequence (rings scale up with overlap, then shake)
     * 3. Scales down the controller and triggers the completion event
     * 4. Cleans up rings and resets state when complete
     */
    private void FireClickSequence()
    {
        CancelScheduledTrigger();

        if (isClicking) return;
        isClicking = true;
        transform.localScale = controllerInitialScale;

        // Move our object to the clicked position, but get z from our current position
        Vector3 alignedPosition = new Vector3(pendingClickPosition.x, pendingClickPosition.y, transform.position.z);
        transform.position = alignedPosition;

        // Kill any existing animation sequence
        if (ringSequence != null && ringSequence.IsActive()) ringSequence.Kill();

        // Ensure initial scales are cached for all rings
        if (ringInitialScales.Count != rings.Count) CacheInitialScales();

        // Build the animation sequence with overlapping ring animations
        ringSequence = DOTween.Sequence();
        float ringTotalDuration = ringAnimationDuration;
        float overlapOffset = ringTotalDuration * 0.5f;
        float currentStartTime = 0f;

        // Add scale-up and shake animations for each ring with staggered timing
        for (int i = 0; i < rings.Count; i++)
        {
            GameObject ring = rings[i];
            if (ring == null) continue;

            // Cache scale if this ring wasn't initialized yet
            if (i >= ringInitialScales.Count)
            {
                ringInitialScales.Add(ring.transform.localScale);
                ring.SetActive(false);
            }

            Vector3 initialScale = ringInitialScales[i];

            // Scale up animation with bounce effect
            ring.transform.localScale = Vector3.zero;
            Tween scaleTween = ring.transform.DOScale(initialScale, ringAnimationDuration)
                .SetEase(Ease.OutBack)
                .OnStart(() => ring.SetActive(true));
            ringSequence.Insert(currentStartTime, scaleTween);

            // Shake animation after scale-up completes
            Tween shakeTween = ring.transform.DOShakeScale(ringShakeDuration, ringShakeStrength);
            ringSequence.Insert(currentStartTime + ringAnimationDuration, shakeTween);

            // Stagger the next ring's start time for overlap effect
            currentStartTime += overlapOffset;
        }

        // Scale down our main parent and trigger our event, which plays a sound at the moment.
        Tween scaleParentTween = transform.DOScale(Vector3.zero, controllerCollapseDuration)
            .SetEase(Ease.InBack)
            .OnStart(() => onAnimationComplete.Invoke());
        ringSequence.Append(scaleParentTween);

        // Cleanup: hide rings and reset state when animation completes
        ringSequence.OnComplete(() =>
        {
            foreach (GameObject ring in rings)
            {
                if (ring != null)
                {
                    ring.SetActive(false);
                }
            }

            isClicking = false;
        });
        ringSequence.Play();
    }

    /**
     * Cancels any pending scheduled trigger to prevent duplicate animations.
     */
    private void CancelScheduledTrigger()
    {
        if (!triggerScheduled || tempoManager == null) return;
        tempoManager.CancelScheduledEvent(triggerHandle);
        triggerScheduled = false;
    }

    /**
     * Caches the initial scales of the controller and all rings for animation reset purposes.
     * Rings are hidden after caching.
     */
    private void CacheInitialScales()
    {
        controllerInitialScale = transform.localScale;
        ringInitialScales.Clear();
        foreach (GameObject ring in rings)
        {
            if (ring != null)
            {
                ringInitialScales.Add(ring.transform.localScale);
                ring.SetActive(false);
            }
            else
            {
                ringInitialScales.Add(Vector3.one);
            }
        }
    }
}