using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

public class BrainwaveControllerBeats : MonoBehaviour
{
    [SerializeField] private bool fixedPosition;
    [SerializeField] private List<GameObject> rings = new();

    [SerializeField, Min(0f)]
    private float ringScaleDurationBeats = 0.25f;

    [SerializeField, Min(0f)]
    private float ringShakeDurationBeats = 0.5f;

    [SerializeField] private float ringShakeStrength = 0.1f;

    [SerializeField, Min(0f)]
    private float controllerCollapseDurationBeats = 0.25f;

    [SerializeField]
    private UnityEvent onSequenceComplete;

    [SerializeField] private TempoManager tempoManager;

    [SerializeField, Tooltip("The StepSequencer to listen to for lookahead events (optional - will use any sequencer if null)")]
    private StepSequencer stepSequencer;

    [SerializeField, Min(0f), Tooltip("How many beats in the past to discard events as stale")]
    private float staleEventThresholdBeats = 1f;

    private readonly List<Vector3> ringInitialScales = new();
    private readonly List<Tween> activeRingTweens = new();
    private Tween controllerTween;
    private Vector3 controllerInitialScale;
    private int nextRingIndex;
    private bool sequenceActive;
    private int currentSequenceId; // Incremented each time BeginSequence is called

    /// <summary>
    /// Tracks which events we've already processed to prevent duplicates.
    /// Key is beat time. Will be cleaned up automatically when events become stale.
    /// </summary>
    private readonly HashSet<double> processedEventBeats = new();

    /// <summary>
    /// Tracks scheduled animation callbacks so we can cancel them
    /// </summary>
    private readonly List<TempoManager.TempoEventHandle> scheduledAnimationHandles = new();


    /**
     * Initializes the controller by caching initial scales and hiding it.
     */
    void Start()
    {
        CacheInitialScales();
        transform.localScale = Vector3.zero;
        nextRingIndex = 0;
        sequenceActive = false;
    }

    void OnEnable()
    {
        CacheInitialScales();
    }

    /**
     * Monitors for upcoming events and handles sequence lifecycle + ring animations.
     * Events are consumed from the queue - processed once and then tracked to prevent reprocessing.
     * Stale processed events (older than staleEventThresholdBeats) are automatically cleaned up.
     */
    void Update()
    {
        if (tempoManager == null || !tempoManager.TransportRunning) return;

        double currentBeat = tempoManager.CurrentBeat;

        // Look BACK in time as well as forward to catch events that were scheduled but we missed
        // We need to look back by at least the animation duration to catch events we should react to
        double lookbackBeats = ringScaleDurationBeats + ringShakeDurationBeats + 0.5;
        double lookaheadBeats = ringScaleDurationBeats + ringShakeDurationBeats + 1.0;

        double searchStartBeat = currentBeat - lookbackBeats;
        double searchWindowBeats = lookbackBeats + lookaheadBeats;

        // Get all events in the search window (past and future)
        var upcomingEvents = stepSequencer != null
            ? tempoManager.GetUpcomingEvents(searchStartBeat, searchWindowBeats, stepSequencer)
            : tempoManager.GetUpcomingEvents(searchStartBeat, searchWindowBeats);

        // Clean up stale processed events (older than staleEventThresholdBeats in the past)
        double staleThreshold = currentBeat - staleEventThresholdBeats;
        processedEventBeats.RemoveWhere(beatTime => beatTime < staleThreshold);

        // Process events in the queue
        foreach (var eventInfo in upcomingEvents)
        {
            // Skip if already processed
            if (processedEventBeats.Contains(eventInfo.BeatTime)) continue;

            // Discard stale events that haven't been processed yet
            if (eventInfo.BeatTime < staleThreshold)
            {
                Debug.LogWarning($"[BrainwaveController] Discarding stale event {eventInfo.EventType} at beat {eventInfo.BeatTime:F2} (current: {currentBeat:F2})");
                processedEventBeats.Add(eventInfo.BeatTime); // Mark as processed so we don't warn again
                continue;
            }

            // Consume and process the event
            switch (eventInfo.EventType)
            {
                case "sequence_start":
                    Debug.Log($"[BrainwaveController] Processing sequence_start at beat {eventInfo.BeatTime:F2}");
                    BeginSequence(); // Always begin, even if already active (resets for new sequence)

                    // Also schedule a ring animation for the start event
                    Debug.Log($"[BrainwaveController] Processing sequence_start, scheduling ring {nextRingIndex} for beat {eventInfo.BeatTime:F2}");
                    ScheduleRingAnimationForBeat(eventInfo.BeatTime);

                    processedEventBeats.Add(eventInfo.BeatTime);
                    break;

                case "sequence_end":
                    Debug.Log($"[BrainwaveController] Processing sequence_end at beat {eventInfo.BeatTime:F2}, current beat: {currentBeat:F2}");
                    // Capture the current sequence ID so the scheduled callback knows which sequence to complete
                    int sequenceIdToComplete = currentSequenceId;
                    double delay = Math.Max(0.001, eventInfo.BeatTime - currentBeat);
                    tempoManager.ScheduleBeatsFromNow(delay, () => CompleteSequence(sequenceIdToComplete));
                    Debug.Log($"[BrainwaveController] Scheduled CompleteSequence for sequence {sequenceIdToComplete} in {delay:F2} beats");
                    processedEventBeats.Add(eventInfo.BeatTime);
                    break;

                case "sequence_next":
                    if (!sequenceActive)
                    {
                        Debug.LogWarning($"[BrainwaveController] Ignoring sequence_next at beat {eventInfo.BeatTime:F2} - sequence not active");
                        processedEventBeats.Add(eventInfo.BeatTime);
                        break;
                    }

                    // Ensure we have enough rings
                    if (nextRingIndex >= rings.Count)
                    {
                        nextRingIndex = 0; // Loop back to first ring
                    }

                    Debug.Log($"[BrainwaveController] Processing sequence_next, scheduling ring {nextRingIndex} for beat {eventInfo.BeatTime:F2}");
                    ScheduleRingAnimationForBeat(eventInfo.BeatTime);
                    processedEventBeats.Add(eventInfo.BeatTime);
                    break;

                default:
                    // Unknown event type - ignore but mark as processed
                    processedEventBeats.Add(eventInfo.BeatTime);
                    break;
            }
        }
    }

    void OnDisable()
    {
        KillActiveTweens();
    }
    
    public void SetPosition(Vector3 worldPosition)
    {
        if(fixedPosition) return;
        Vector3 alignedPosition = new Vector3(worldPosition.x, worldPosition.y, transform.position.z);
        transform.position = alignedPosition;
    }

    /**
     * Prepares the controller for a new beat-driven sequence at the supplied world position.
     * Use this to align the controller before driving rings via NextRing().
     */
    public void BeginSequence(Vector3 worldPosition)
    {
        SetPosition(worldPosition);
        BeginSequence();
    }

    /**
     * Prepares the controller for a new beat-driven sequence without adjusting position.
     */
    public void BeginSequence()
    {
        // Cancel any pending animations from previous sequences
        if (tempoManager != null)
        {
            foreach (var handle in scheduledAnimationHandles)
            {
                tempoManager.CancelScheduledEvent(handle);
            }
        }
        scheduledAnimationHandles.Clear();

        KillActiveTweens();
        transform.localScale = controllerInitialScale;
        sequenceActive = true;
        nextRingIndex = 0;
        currentSequenceId++; // Increment so old CompleteSequence callbacks don't affect new sequence
        Debug.Log($"[BrainwaveController] BeginSequence - starting sequence {currentSequenceId}");
        // Note: We don't clear processedEventBeats here because sequences can overlap
        // Stale events are cleaned up automatically in Update()

        EnsureRingCaches();
        ResetRingsVisuals();
    }

    /**
     * Triggers the next ring in the list to animate using beat-synced durations.
     * Subsequent calls continue advancing through the list.
     */
    public void NextRing()
    {
        if (rings == null || rings.Count == 0) return;
        EnsureRingCaches();

        if (!sequenceActive)
        {
            BeginSequence();
        }

        while (nextRingIndex < rings.Count && rings[nextRingIndex] == null)
        {
            nextRingIndex++;
        }

        if (nextRingIndex >= rings.Count) return;

        int ringIndex = nextRingIndex;
        GameObject ring = rings[ringIndex];
        nextRingIndex++;

        Vector3 initialScale = ringInitialScales[ringIndex];

        ring.transform.DOKill();
        ring.transform.localScale = Vector3.zero;
        ring.SetActive(true);

        float scaleDuration = Mathf.Max(0f, BeatsToSeconds(ringScaleDurationBeats));
        Tween scaleTween = ring.transform
            .DOScale(initialScale, Mathf.Max(1e-4f, scaleDuration))
            .SetEase(Ease.OutBack);

        Tween shakeTween = null;
        float shakeDuration = Mathf.Max(0f, BeatsToSeconds(ringShakeDurationBeats));
        if (shakeDuration > 1e-4f && ringShakeStrength > 0f)
        {
            shakeTween = ring.transform.DOShakeScale(shakeDuration, ringShakeStrength);
        }

        Sequence ringSequence = DOTween.Sequence();
        ringSequence.Append(scaleTween);
        if (shakeTween != null) ringSequence.Append(shakeTween);

        activeRingTweens.Add(ringSequence);
        ringSequence.OnKill(() => { activeRingTweens.Remove(ringSequence); });
        ringSequence.Play();
    }

    /**
     * Scales the controller back to zero and hides all rings, invoking the completion event once finished.
     * Only completes if the sequenceId matches the current sequence (prevents old callbacks from completing new sequences).
     */
    public void CompleteSequence(int sequenceId)
    {
        if (sequenceId != currentSequenceId)
        {
            Debug.Log($"[BrainwaveController] Ignoring CompleteSequence for old sequence {sequenceId} (current: {currentSequenceId})");
            return;
        }

        if (!sequenceActive)
        {
            Debug.Log($"[BrainwaveController] CompleteSequence called but sequence {sequenceId} already inactive");
            return;
        }

        Debug.Log($"[BrainwaveController] CompleteSequence for sequence {sequenceId}");
        sequenceActive = false;
        nextRingIndex = 0;

        KillActiveTweens();

        float collapseDuration = Mathf.Max(0f, BeatsToSeconds(controllerCollapseDurationBeats));
        controllerTween = transform
            .DOScale(Vector3.zero, Mathf.Max(1e-4f, collapseDuration))
            .SetEase(Ease.InBack)
            .OnComplete(() =>
            {
                foreach (GameObject ring in rings)
                {
                    if (ring == null) continue;
                    ring.SetActive(false);
                    ring.transform.localScale = Vector3.zero;
                }

                onSequenceComplete?.Invoke();
            });
    }

    // Legacy version for manual calls
    public void CompleteSequence()
    {
        CompleteSequence(currentSequenceId);
    }

    /**
     * Immediately stops and clears the sequence without animations.
     * Use this to force-stop everything.
     */
    public void StopSequence()
    {
        Debug.Log($"[BrainwaveController] StopSequence - force stopping sequence {currentSequenceId}");

        sequenceActive = false;
        nextRingIndex = 0;

        // Cancel all scheduled animation callbacks
        if (tempoManager != null)
        {
            foreach (var handle in scheduledAnimationHandles)
            {
                tempoManager.CancelScheduledEvent(handle);
            }
        }
        scheduledAnimationHandles.Clear();

        KillActiveTweens();

        // Immediately hide everything
        transform.localScale = Vector3.zero;

        foreach (GameObject ring in rings)
        {
            if (ring == null) continue;
            ring.SetActive(false);
            ring.transform.localScale = Vector3.zero;
        }

        // Clear processed events so a new sequence can start fresh
        processedEventBeats.Clear();
    }

    /**
     * Caches the initial scales of the controller and all rings for animation reset purposes.
     * Rings are hidden after caching.
     */
    private void CacheInitialScales()
    {
        controllerInitialScale = transform.localScale;
        EnsureRingCaches();
        ResetRingsVisuals();
    }

    private void EnsureRingCaches()
    {
        if (rings == null) return;

        if (ringInitialScales.Count != rings.Count)
        {
            ringInitialScales.Clear();
            if (rings != null)
            {
                foreach (GameObject ring in rings)
                {
                    if (ring != null) ringInitialScales.Add(ring.transform.localScale);
                    else ringInitialScales.Add(Vector3.one);
                }
            }
        }
    }

    private void ResetRingsVisuals()
    {
        if (rings == null) return;

        for (int i = 0; i < rings.Count; i++)
        {
            GameObject ring = rings[i];
            if (ring == null) continue;

            ring.SetActive(false);
            ring.transform.localScale = Vector3.zero;
        }
    }

    /**
     * Schedules a ring animation to complete exactly at the specified beat time.
     * The animation starts early so it finishes on-beat, creating visual anticipation.
     *
     * <param name="targetBeatTime">The absolute beat time when the animation should FINISH (when audio plays)</param>
     *
     * How timing works:
     * - targetBeatTime: When the audio will trigger (e.g., beat 4.0)
     * - animationDurationBeats: Total animation time (scale + shake, e.g., 0.75 beats)
     * - startBeat: When to START the animation (targetBeatTime - animationDurationBeats, e.g., 3.25)
     * - delayBeats: How long to wait from NOW until startBeat
     *
     * Example:
     * - targetBeatTime = 4.0 (audio plays here)
     * - currentBeat = 3.5
     * - animationDurationBeats = 0.75
     * - startBeat = 4.0 - 0.75 = 3.25
     * - delayBeats = 3.25 - 3.5 = -0.25 (already past, start immediately)
     *
     * Another example:
     * - targetBeatTime = 8.0 (audio plays here)
     * - currentBeat = 7.0
     * - animationDurationBeats = 0.5
     * - startBeat = 8.0 - 0.5 = 7.5
     * - delayBeats = 7.5 - 7.0 = 0.5 (schedule to start in 0.5 beats)
     */
    private void ScheduleRingAnimationForBeat(double targetBeatTime)
    {
        if (rings == null || rings.Count == 0 || tempoManager == null) return;
        EnsureRingCaches();

        // Find the next available ring
        while (nextRingIndex < rings.Count && rings[nextRingIndex] == null)
        {
            nextRingIndex++;
        }

        if (nextRingIndex >= rings.Count) return;

        int ringIndex = nextRingIndex;
        GameObject ring = rings[ringIndex];
        nextRingIndex++;

        Vector3 initialScale = ringInitialScales[ringIndex];

        // Calculate when to start the animation so it finishes at targetBeatTime
        double currentBeat = tempoManager.CurrentBeat;
        double animationDurationBeats = ringScaleDurationBeats + ringShakeDurationBeats;
        double startBeat = targetBeatTime - animationDurationBeats;

        // Calculate delay from now until animation should start
        // If negative (we're past the start time), clamp to 0 and start immediately
        double delayBeats = Mathf.Max(0f, (float)(startBeat - currentBeat));
        float delaySeconds = (float)(delayBeats * tempoManager.SecondsPerBeat);

        // Schedule the animation to start at the calculated time
        if (delaySeconds > 0.001f)
        {
            var handle = tempoManager.ScheduleBeatsFromNow(delayBeats, () => TriggerRingAnimation(ring, ringIndex, initialScale));
            scheduledAnimationHandles.Add(handle);
        }
        else
        {
            // Start immediately if we're at or past the start time
            TriggerRingAnimation(ring, ringIndex, initialScale);
        }
    }

    /**
     * Triggers the actual ring animation (called either immediately or via scheduled callback).
     * This is the same animation logic as the old NextRing() method, but triggered automatically
     * based on lookahead timing.
     *
     * <param name="ring">The ring GameObject to animate</param>
     * <param name="ringIndex">Index of the ring (used for tracking, currently unused but available for future use)</param>
     * <param name="initialScale">The target scale to animate to (cached from the ring's original scale)</param>
     *
     * Animation sequence:
     * 1. Scale up from zero to initialScale (with OutBack easing for overshoot effect)
     * 2. Shake the scale for visual impact (optional, based on ringShakeStrength)
     */
    private void TriggerRingAnimation(GameObject ring, int ringIndex, Vector3 initialScale)
    {
        if (ring == null) return;

        ring.transform.DOKill();
        ring.transform.localScale = Vector3.zero;
        ring.SetActive(true);

        float scaleDuration = Mathf.Max(0f, BeatsToSeconds(ringScaleDurationBeats));
        Tween scaleTween = ring.transform
            .DOScale(initialScale, Mathf.Max(1e-4f, scaleDuration))
            .SetEase(Ease.OutBack);

        Tween shakeTween = null;
        float shakeDuration = Mathf.Max(0f, BeatsToSeconds(ringShakeDurationBeats));
        if (shakeDuration > 1e-4f && ringShakeStrength > 0f)
        {
            shakeTween = ring.transform.DOShakeScale(shakeDuration, ringShakeStrength);
        }

        Sequence ringSequence = DOTween.Sequence();
        ringSequence.Append(scaleTween);
        if (shakeTween != null) ringSequence.Append(shakeTween);

        activeRingTweens.Add(ringSequence);
        ringSequence.OnKill(() => { activeRingTweens.Remove(ringSequence); });
        ringSequence.Play();
    }

    private float BeatsToSeconds(float beatFraction)
    {
        if (tempoManager != null && tempoManager.SecondsPerBeat > double.Epsilon)
        {
            return Mathf.Max(0f, beatFraction) * (float)tempoManager.SecondsPerBeat;
        }

        return Mathf.Max(0f, beatFraction);
    }

    private void KillActiveTweens()
    {
        for (int i = activeRingTweens.Count - 1; i >= 0; i--)
        {
            Tween tween = activeRingTweens[i];
            tween?.Kill();
        }

        activeRingTweens.Clear();

        controllerTween?.Kill();
        controllerTween = null;

        if (rings != null)
        {
            foreach (GameObject ring in rings)
            {
                if (ring == null) continue;
                ring.transform.DOKill();
            }
        }

        transform.DOKill();
    }
}