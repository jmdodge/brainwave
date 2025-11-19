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

    [SerializeField, Min(0f), Tooltip("How early (in beats) to start the ring animation before a next event's beat time")]
    private float stepNextAnimationLookahead = 0.125f;
    
    [SerializeField, Min(0f), Tooltip("How early (in beats) to start the ring animation before the last event's beat time")]
    private float stepEndAnimationLookahead = 0.125f;
    
    [SerializeField, Min(0f)]
    private float ringScaleDurationBeats = 0.25f;

    [SerializeField, Min(0f)]
    private float ringShakeDurationBeats = 0.25f;

    [SerializeField] private float ringShakeStrength = 0.1f;

    [SerializeField, Min(0f)]
    private float controllerCollapseDurationBeats = 0.25f;

    [SerializeField]
    private UnityEvent onSequenceComplete;
    
    [SerializeField, Tooltip("Turn this off if you want to have multiple sources reading sequencer events")] 
    private bool doCleanupOwnEvents = true;

    [SerializeField] private TempoManager tempoManager;

    [SerializeField, Tooltip("The StepSequencer to listen to for lookahead events (optional - will use any sequencer if null)")]
    private StepSequencer stepSequencer;

    [SerializeField, Tooltip("Filter events by tag (optional - leave empty to receive all events)")]
    private string eventTagFilter = "";

    private readonly List<Vector3> ringInitialScales = new();
    private readonly List<Tween> activeRingTweens = new();
    private Tween controllerTween;
    private Vector3 controllerInitialScale;
    private int nextRingIndex;
    private bool sequenceActive;
    private int currentSequenceId; // Incremented each time BeginSequence is called

    /// <summary>
    /// Tracks which events we've already processed to prevent duplicates.
    /// Key is event ID. Will be cleaned up automatically when events become stale.
    /// </summary>
    private readonly HashSet<Guid> processedEventIds = new();


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
        double lookbackBeats = ringScaleDurationBeats + ringShakeDurationBeats + 2.0;
        double lookaheadBeats = ringScaleDurationBeats + ringShakeDurationBeats + 2.0;

        double searchStartBeat = currentBeat - lookbackBeats;
        double searchWindowBeats = lookbackBeats + lookaheadBeats;

        // Get all events in the search window (past and future), applying filters
        object sourceFilter = stepSequencer; // null means all sources
        string tagFilter = !string.IsNullOrEmpty(eventTagFilter) ? eventTagFilter : null;

        var upcomingEvents = tempoManager.GetUpcomingEvents(
            searchStartBeat,
            searchWindowBeats,
            sourceFilter: sourceFilter,
            tagFilter: tagFilter);
        
        // Collect IDs to clean up from TempoManager after processing
        var idsToCleanup = new List<Guid>();

        // Process events in the queue
        foreach (var eventInfo in upcomingEvents)
        {
            // Skip if already processed
            if (processedEventIds.Contains(eventInfo.Id)) continue;
            
            // Capture the current sequence ID so the scheduled callback knows which sequence to complete
            int sequenceIdToComplete = currentSequenceId;

            // Consume and process the event
            switch (eventInfo.EventType)
            {
                case "sequence_start":
                    // Calculate when to start the animation so it finishes on-beat
                    double startAnimStartBeat = eventInfo.BeatTime - stepNextAnimationLookahead;
                    double startDelay = Math.Max(0.001, startAnimStartBeat - currentBeat);

                    tempoManager.ScheduleBeatsFromNow(startDelay, () =>
                    {
                        BeginSequence(); // Start the sequence
                        TriggerNextRingAnimation(); // Start ring animation (will finish at eventInfo.BeatTime)
                    });
                    Debug.Log($"[BrainwaveController] Current beat: {currentBeat:F2} | Scheduled BeginSequence + ring animation in {startDelay:F2} beats (animation will complete at beat {eventInfo.BeatTime:F2}) | Seq: {sequenceIdToComplete}");
                    processedEventIds.Add(eventInfo.Id);
                    idsToCleanup.Add(eventInfo.Id);
                    break;

                case "sequence_end":
                    
                    // Calculate when to start the animation so it finishes on-beat
                    double endAnimStartBeat = eventInfo.BeatTime - stepEndAnimationLookahead;
                    double endDelay = Math.Max(0.001, endAnimStartBeat - currentBeat);
                    
                    tempoManager.ScheduleBeatsFromNow(endDelay, () => CompleteSequence(sequenceIdToComplete));
                    Debug.Log($"[BrainwaveController] Current beat: {currentBeat:F2} | Scheduled CompleteSequence + ring animation in {endDelay:F2} beats (animation will complete at beat {eventInfo.BeatTime:F2}) | Seq: {sequenceIdToComplete}");
                    processedEventIds.Add(eventInfo.Id);
                    idsToCleanup.Add(eventInfo.Id);
                    break;

                case "sequence_next":
                    // Calculate when to start the animation so it finishes on-beat
                    // State checks (sequenceActive, nextRingIndex) happen when the callback executes
                    double nextAnimStartBeat = eventInfo.BeatTime - stepNextAnimationLookahead;
                    double nextDelay = Math.Max(0.001, nextAnimStartBeat - currentBeat);

                    tempoManager.ScheduleBeatsFromNow(nextDelay, () =>
                    {
                        // Check state at execution time, not lookahead time
                        if (!sequenceActive)
                        {
                            Debug.LogWarning($"[BrainwaveController] Skipping sequence_next - sequence not active at beat {tempoManager.CurrentBeat:F2}");
                            return;
                        }

                        // Start ring animation now (will finish at eventInfo.BeatTime)
                        TriggerNextRingAnimation();
                    });
                    Debug.Log($"[BrainwaveController] Current beat: {currentBeat:F2} | Scheduled sequence_next + ring animation in {nextDelay:F2} beats (animation will complete at beat {eventInfo.BeatTime:F2})  | Seq: {sequenceIdToComplete}");
                    
                    processedEventIds.Add(eventInfo.Id);
                    idsToCleanup.Add(eventInfo.Id);
                    break;

                default:
                    // Unknown event type - ignore but mark as processed
                    processedEventIds.Add(eventInfo.Id);
                    idsToCleanup.Add(eventInfo.Id);
                    break;

            }
        }

        // Clean up processed events from TempoManager, if configured to do so.
        if(!doCleanupOwnEvents) idsToCleanup.Clear();
        if (idsToCleanup.Count > 0)
        {
            tempoManager.CleanupEventsByIds(idsToCleanup);
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
        KillActiveTweens();
        transform.localScale = controllerInitialScale;
        sequenceActive = true;
        nextRingIndex = 0;
        currentSequenceId++; // Increment so old CompleteSequence callbacks don't affect new sequence
        Debug.Log($"[BrainwaveController] BeginSequence - starting sequence {currentSequenceId}");
        // Note: We don't clear processedEventIds here because sequences can overlap
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
        // Commenting out - I don't think we need to track sequence ID like this.
        // if (sequenceId != currentSequenceId)
        // {
            // Debug.Log($"[BrainwaveController] Ignoring CompleteSequence for old sequence {sequenceId} (current: {currentSequenceId})");
            // return;
        // }

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
        processedEventIds.Clear();
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
     * Triggers an animation for the next available ring, advancing nextRingIndex.
     * This should be called at the actual event time, not during lookahead.
     */
    private void TriggerNextRingAnimation()
    {
        if (rings == null || rings.Count == 0) return;
        EnsureRingCaches();

        // Wrap around if we've run out of rings
        if (nextRingIndex >= rings.Count)
        {
            nextRingIndex = 0;
        }

        // Find the next available ring
        while (nextRingIndex < rings.Count && rings[nextRingIndex] == null)
        {
            nextRingIndex++;
        }

        if (nextRingIndex >= rings.Count)
        {
            Debug.LogWarning("[BrainwaveController] No valid rings available for animation");
            return;
        }

        int ringIndex = nextRingIndex;
        GameObject ring = rings[ringIndex];
        nextRingIndex++; // Advance for next call

        Vector3 initialScale = ringInitialScales[ringIndex];
        TriggerRingAnimation(ring, ringIndex, initialScale);
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