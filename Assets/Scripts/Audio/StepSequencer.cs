using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;
using UnityEngine.Serialization;

[AddComponentMenu("Audio/Step Sequencer")]
public sealed class StepSequencer : MonoBehaviour
{
    [TitleGroup("Editor Controls", order: -1)]
    [HorizontalGroup("Editor Controls/Buttons")]
    [Button(ButtonSizes.Medium), GUIColor(0.4f, 1f, 0.4f)]
    void StartSequenceButton() => StartSequence();

    [HorizontalGroup("Editor Controls/Buttons")]
    [Button(ButtonSizes.Medium), GUIColor(1f, 0.4f, 0.4f)]
    void StopSequenceButton() => StopSequence();

    [HorizontalGroup("Editor Controls/Buttons")]
    [Button(ButtonSizes.Medium), GUIColor(0.4f, 0.8f, 1f)]
    void StartTransportButton() => StartTransportManually();

    [TitleGroup("Settings", order: 0)]
    [SerializeField] TempoManager tempoManager;
    [FormerlySerializedAs("sineWaveGenerator")] [SerializeField] SoundWaveGenerator soundWaveGenerator;
    [SerializeField] bool triggerAudio = true;
    [SerializeField] bool playOnStart = true;
    [SerializeField] bool loop = true;
    [SerializeField] bool canStartTransport;
    [SerializeField] bool waitForTransport;
    [SerializeField] float standaloneBpm = 100f;
    [SerializeField] bool quantizeStart = true;
    [Min(0f)] [SerializeField] float startQuantizationBeats = 1f;
    [TitleGroup("Steps", order: 1)]
    [TableList(ShowIndexLabels = true, AlwaysExpanded = true)]
    [SerializeField] List<SequenceStep> steps = new() { SequenceStep.Default() };

    readonly List<RuntimeStep> runtimeSteps = new();

    const double beatTolerance = 1e-4; // 0.0001 beats ≈ 0.06ms at 100 BPM, ≈ 0.025ms at 240 BPM
    const float minStepDurationBeats = 0.015625f;
    const float minFrequencyHz = 20f;
    const int maxRuntimeSteps = 64;
    const string defaultNoteName = "A4";
    const int lookaheadSteps = 16; // How many steps to schedule in advance (buffer size)

    // --- Playback State ---
    bool isRunning; // True when sequence is actively playing
    bool waitingForTransport; // True when waiting for transport to start before playing
    bool noteActive; // True when a note is currently sounding (not a rest)
    SoundWaveGenerator activeGenerator; // The generator currently playing the note

    // --- Scheduling State ---
    // The "playhead" - which step in the sequence is currently playing
    int currentPlayingStepIndex;

    // How many steps we've scheduled ahead (absolute count, wraps for loops)
    int totalStepsScheduled;

    // Beat position where playback started (anchor point for calculating step timing)
    double playbackStartBeat;

    // List of scheduled event handles so we can cancel them if needed
    readonly List<TempoManager.TempoEventHandle> scheduledEventHandles = new();

    // --- Quantized Start State ---
    bool startScheduled; // True when waiting for a quantized start time
    double scheduledStartBeat; // The beat at which playback will begin
    TempoManager.TempoEventHandle startHandle; // Handle for the quantized start event

#if UNITY_EDITOR
    /**
     * Ensures newly added steps have their UnityEvents instantiated so the inspector displays them normally.
     */
    void OnValidate()
    {
        if (steps == null) return;
        foreach (SequenceStep step in steps)
        {
            EnsureUnityEvents(step);
        }
    }
#endif

    void OnEnable()
    {
        if (tempoManager == null) tempoManager = FindAnyObjectByType<TempoManager>();
        if (triggerAudio && soundWaveGenerator == null) soundWaveGenerator = GetComponent<SoundWaveGenerator>();

        if (tempoManager != null)
        {
            tempoManager.OnTransportStarted += OnTransportStarted;
        }
    }

    void Start()
    {
        if (playOnStart)
        {
            if (waitForTransport && (tempoManager == null || !tempoManager.TransportRunning))
            {
                waitingForTransport = true;
            }
            else
            {
                StartSequence();
            }
        }
    }

    /**
     * Maintains the lookahead buffer by scheduling steps in advance.
     * This ensures frame-independent timing - even if Update() stutters, scheduled steps play on time.
     */
    void Update()
    {
        if (!isRunning || tempoManager == null || runtimeSteps.Count == 0) return;

        // Keep the buffer filled with upcoming steps
        while (totalStepsScheduled < currentPlayingStepIndex + lookaheadSteps)
        {
            ScheduleNextStep();
        }
    }

    public void StartSequence()
    {
        StopSequence();

        if (tempoManager == null)
        {
            Debug.LogWarning("StepSequencer requires a TempoManager reference.", this);
            return;
        }

        if (triggerAudio && soundWaveGenerator == null)
        {
            soundWaveGenerator = GetComponent<SoundWaveGenerator>();
            if (soundWaveGenerator == null)
            {
                Debug.LogWarning("StepSequencer is set to trigger audio but has no SineWaveGenerator reference.",
                    this);
            }
        }

        PrepareRuntimeSteps();
        if (runtimeSteps.Count == 0)
        {
            Debug.LogWarning("StepSequencer has no valid steps to play.", this);
            return;
        }

        EnsureTransportRunning();

        ScheduleOrStartImmediately();
    }

    public void StopSequence()
    {
        // Cancel any pending quantized start
        if (startScheduled && tempoManager != null)
        {
            tempoManager.CancelScheduledEvent(startHandle);
            startScheduled = false;
        }

        // Cancel all scheduled steps in the lookahead buffer
        foreach (var handle in scheduledEventHandles)
        {
            if (tempoManager != null)
            {
                tempoManager.CancelScheduledEvent(handle);
            }
        }
        scheduledEventHandles.Clear();

        waitingForTransport = false;
        isRunning = false;

        // Stop any currently playing note
        if (noteActive && triggerAudio && activeGenerator != null)
        {
            activeGenerator.NoteOff();
            noteActive = false;
            activeGenerator = null;
        }
    }

    public void StartTransportManually()
    {
        if (tempoManager == null)
        {
            Debug.LogWarning("StepSequencer cannot start transport without a TempoManager.", this);
            return;
        }

        double dspNow = AudioSettings.dspTime;
        tempoManager.SetTempo(Mathf.Max(1f, standaloneBpm), dspNow);
        tempoManager.StartTransport(dspNow);
    }

    public void SetSteps(IEnumerable<SequenceStep> newSteps)
    {
        if (newSteps == null) return;
        steps = new List<SequenceStep>(newSteps);
        if (isRunning) StartSequence();
    }

    /**
     * Schedules the next step in the sequence to be triggered at its precise beat time.
     * This is called repeatedly by Update() to maintain the lookahead buffer.
     */
    void ScheduleNextStep()
    {
        if (runtimeSteps.Count == 0) return;

        // Calculate which step in the sequence to schedule (wraps for loops)
        int stepIndexInSequence = totalStepsScheduled % runtimeSteps.Count;
        RuntimeStep step = runtimeSteps[stepIndexInSequence];

        // Calculate when this step should trigger (in beats from start)
        double stepStartBeat = CalculateStepStartBeat(totalStepsScheduled);

        // Schedule the step to trigger at the calculated beat
        var handle = tempoManager.ScheduleAtBeat(stepStartBeat, () =>
        {
            OnStepTriggered(stepIndexInSequence);
        });

        scheduledEventHandles.Add(handle);
        totalStepsScheduled++;

        // If this is the last step and we're not looping, stop scheduling
        if (!loop && totalStepsScheduled >= runtimeSteps.Count)
        {
            // The final step's callback will set isRunning = false
            return;
        }
    }

    /**
     * Calculates the beat time when a step should start, based on how many steps have played.
     * @param absoluteStepIndex - The total number of steps that have been scheduled (may be > sequence length for loops)
     */
    double CalculateStepStartBeat(int absoluteStepIndex)
    {
        double beatOffset = 0;

        // Sum up the duration of all previous steps
        for (int i = 0; i < absoluteStepIndex; i++)
        {
            int stepIndex = i % runtimeSteps.Count;
            beatOffset += Math.Max(minStepDurationBeats, runtimeSteps[stepIndex].durationBeats);
        }

        return playbackStartBeat + beatOffset;
    }

    /**
     * Called when a scheduled step's beat time arrives (DSP callback).
     * Triggers the note on/off, fires UnityEvents, and advances the playhead.
     */
    void OnStepTriggered(int stepIndexInSequence)
    {
        if (!isRunning || runtimeSteps.Count == 0) return;

        RuntimeStep step = runtimeSteps[stepIndexInSequence];
        bool tiesFromPrevious = StepTiesFromPrevious(stepIndexInSequence);

        // Handle note state transitions
        if (step.rest)
        {
            // Rest: Stop any playing note
            if (noteActive && triggerAudio && activeGenerator != null)
            {
                activeGenerator.NoteOff();
                noteActive = false;
                activeGenerator = null;
            }
        }
        else if (!tiesFromPrevious && triggerAudio && step.generator != null)
        {
            // New note: Apply pitch and trigger
            ApplyStepPitch(step);
            step.generator.NoteOn();
            noteActive = true;
            activeGenerator = step.generator;
        }
        // else: tied note continues playing (no action needed)

        // Fire UnityEvents
        InvokeStepStartEvent(step);

        // Advance the playhead
        currentPlayingStepIndex++;

        // Check if we've reached the end of a non-looping sequence
        if (!loop && currentPlayingStepIndex >= runtimeSteps.Count)
        {
            isRunning = false;
            if (noteActive && triggerAudio && activeGenerator != null)
            {
                activeGenerator.NoteOff();
                noteActive = false;
                activeGenerator = null;
            }
        }

        // Remove this event handle from our tracking list (it's already fired)
        if (scheduledEventHandles.Count > 0)
        {
            scheduledEventHandles.RemoveAt(0);
        }
    }


    void PrepareRuntimeSteps()
    {
        runtimeSteps.Clear();
        if (steps == null || steps.Count == 0) return;

        foreach (SequenceStep step in steps)
        {
            if (runtimeSteps.Count >= maxRuntimeSteps) break;
            if (step == null) continue;

            float sanitizedDuration = Mathf.Max(minStepDurationBeats, step.durationBeats);
            float sanitizedFrequency = Mathf.Max(minFrequencyHz, step.frequencyHz);
            bool sanitizedTie = !step.rest && step.tieFromPrevious;
            bool sanitizedUseNoteName = step.useNoteName;
            string sanitizedNoteName = string.IsNullOrWhiteSpace(step.noteName) ? defaultNoteName : step.noteName;
            SoundWaveGenerator resolvedGenerator = step.soundWaveGenerator != null ? step.soundWaveGenerator : soundWaveGenerator;

            runtimeSteps.Add(new RuntimeStep(
                step,
                step.rest,
                sanitizedTie,
                sanitizedUseNoteName,
                sanitizedNoteName,
                sanitizedFrequency,
                sanitizedDuration,
                resolvedGenerator));

            EnsureUnityEvents(step);
        }

        if (runtimeSteps.Count == 0) return;

        if (!loop)
        {
            RuntimeStep first = runtimeSteps[0];
            if (first.tieFromPrevious)
            {
                runtimeSteps[0] = first.WithTieFromPrevious(false);
            }
        }

        for (int i = 0; i < runtimeSteps.Count; i++)
        {
            RuntimeStep current = runtimeSteps[i];
            if (!current.tieFromPrevious) continue;

            int prevIndex = i == 0 ? runtimeSteps.Count - 1 : i - 1;
            if (i == 0 && !loop)
            {
                runtimeSteps[i] = current.WithTieFromPrevious(false);
                continue;
            }

            if (runtimeSteps[prevIndex].rest)
            {
                runtimeSteps[i] = current.WithTieFromPrevious(false);
            }
        }
    }

    static void EnsureUnityEvents(SequenceStep step)
    {
        if (step == null) return;
        step.onStepStart ??= new UnityEvent();
        step.onStepEnd ??= new UnityEvent();
    }


    bool StepTiesFromPrevious(int stepIndex)
    {
        if (runtimeSteps.Count == 0) return false;

        RuntimeStep step = runtimeSteps[stepIndex];
        if (!step.tieFromPrevious) return false;

        if (stepIndex == 0)
        {
            if (!loop) return false;
            return !runtimeSteps[runtimeSteps.Count - 1].rest;
        }

        return !runtimeSteps[stepIndex - 1].rest;
    }

    void ApplyStepPitch(RuntimeStep step)
    {
        if (!triggerAudio || step.generator == null) return;
        if (step.useNoteName)
        {
            step.generator.SetNoteName(step.noteName);
        }
        else
        {
            step.generator.SetFrequency(step.frequencyHz);
        }
    }

    void EnsureTransportRunning()
    {
        if (tempoManager == null) return;
        if (tempoManager.TransportRunning || !canStartTransport) return;

        double dspNow = AudioSettings.dspTime;
        tempoManager.SetTempo(Mathf.Max(1f, standaloneBpm), dspNow);
        tempoManager.StartTransport(dspNow);
    }

    void OnDisable()
    {
        if (tempoManager != null)
        {
            tempoManager.OnTransportStarted -= OnTransportStarted;
        }
        StopSequence();
    }

    void OnTransportStarted()
    {
        if (waitingForTransport)
        {
            waitingForTransport = false;

            // When starting with transport, begin at beat 0 immediately
            // The quantization logic is only for mid-song starts
            if (tempoManager == null)
            {
                Debug.LogWarning("StepSequencer cannot start because TempoManager is null.", this);
                return;
            }

            PrepareRuntimeSteps();
            if (runtimeSteps.Count == 0)
            {
                Debug.LogWarning("StepSequencer has no valid steps to play.", this);
                return;
            }

            BeginPlaybackAtBeat(0d);
        }
    }

    void ScheduleOrStartImmediately()
    {
        if (tempoManager == null)
        {
            BeginPlaybackAtBeat(0d);
            return;
        }

        double currentBeat = tempoManager.CurrentBeat;
        bool canQuantize = quantizeStart && startQuantizationBeats > 0f && tempoManager.TransportRunning;
        if (!canQuantize)
        {
            BeginPlaybackAtBeat(currentBeat);
            return;
        }

        double quant = Math.Max(1e-6, startQuantizationBeats);
        double cycles = Math.Floor(currentBeat / quant);
        double currentQuantizedBeat = cycles * quant;

        // If we're already at (or very close to) a quantization boundary, start immediately
        if (Math.Abs(currentBeat - currentQuantizedBeat) < beatTolerance)
        {
            BeginPlaybackAtBeat(currentQuantizedBeat);
            return;
        }

        // Otherwise, wait for the next quantization boundary
        double targetBeat = (cycles + 1d) * quant;

        startScheduled = true;
        scheduledStartBeat = targetBeat;
        startHandle = tempoManager.ScheduleAtBeat(targetBeat, OnScheduledStart);
    }

    void OnScheduledStart()
    {
        startScheduled = false;
        BeginPlaybackAtBeat(scheduledStartBeat);
    }

    /**
     * Begins playback at the specified beat position.
     * Initializes scheduling state and starts filling the lookahead buffer.
     */
    void BeginPlaybackAtBeat(double startBeat)
    {
        playbackStartBeat = startBeat;
        currentPlayingStepIndex = 0;
        totalStepsScheduled = 0;
        isRunning = true;
        noteActive = false;
        activeGenerator = null;

        // Clear any leftover scheduled events
        scheduledEventHandles.Clear();

        // Update() will immediately start filling the lookahead buffer
    }

    void InvokeStepStartEvent(RuntimeStep step)
    {
        if (step.source == null) return;
        step.source.onStepStart?.Invoke();
    }

    void InvokeStepEndEvent(RuntimeStep step)
    {
        if (step.source == null) return;
        step.source.onStepEnd?.Invoke();
    }

    [Serializable]
    public sealed class SequenceStep
    {
        [Tooltip("Marks this step as a rest; no note is triggered.")]
        public bool rest;

        [Tooltip("When true, this step sustains the previous note instead of retriggering.")]
        public bool tieFromPrevious;

        [Tooltip("Use scientific pitch notation (e.g. C4). When false, frequencyHz is used.")]
        public bool useNoteName = true;

        [Tooltip("Scientific pitch notation for this step.")]
        public string noteName = defaultNoteName;

        [Tooltip("Raw frequency in hertz for this step when not using note names.")]
        public float frequencyHz = 440f;

        [Tooltip("Duration of this step in beats.")]
        public float durationBeats = 1f;

        [FormerlySerializedAs("sineWaveGenerator")] [Tooltip("Optional SineWaveGenerator to use for this step. If null, uses the default generator.")]
        public SoundWaveGenerator soundWaveGenerator;

        [HideInTables]
        [Tooltip("UnityEvents that fire immediately when this step begins.")]
        public UnityEvent onStepStart = new();

        [HideInTables]
        [Tooltip("UnityEvents that fire when this step completes.")]
        public UnityEvent onStepEnd = new();

        public static SequenceStep Default()
        {
            return new SequenceStep();
        }
    }

    readonly struct RuntimeStep
    {
        public readonly SequenceStep source;
        public readonly bool rest;
        public readonly bool tieFromPrevious;
        public readonly bool useNoteName;
        public readonly string noteName;
        public readonly float frequencyHz;
        public readonly float durationBeats;
        public readonly SoundWaveGenerator generator;

        public RuntimeStep(
            SequenceStep source,
            bool rest,
            bool tieFromPrevious,
            bool useNoteName,
            string noteName,
            float frequencyHz,
            float durationBeats,
            SoundWaveGenerator generator)
        {
            this.source = source;
            this.rest = rest;
            this.tieFromPrevious = tieFromPrevious;
            this.useNoteName = useNoteName;
            this.noteName = noteName;
            this.frequencyHz = frequencyHz;
            this.durationBeats = durationBeats;
            this.generator = generator;
        }

        public RuntimeStep WithTieFromPrevious(bool tieFromPrevious)
        {
            return new RuntimeStep(source, rest, tieFromPrevious, useNoteName, noteName, frequencyHz, durationBeats, generator);
        }
    }
}

