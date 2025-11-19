using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

public enum SequenceEventType
{
    None, // No event registered (for audio-only sequencers)
    SequenceNext, // Normal step event
    SequenceStart, // Sequence initialization event
    SequenceEnd // Sequence completion event
}

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

    [TitleGroup("Settings", order: 0)] [SerializeField]
    SequencerMode mode = SequencerMode.AudioAndEvents;

    [SerializeField] TempoManager tempoManager;

    [ShowIf("@mode == SequencerMode.AudioOnly || mode == SequencerMode.AudioAndEvents")]
    [Tooltip("Default sound generator for steps that don't specify their own.")]
    [SerializeField]
    MonoBehaviour soundGenerator; // ISoundGenerator interface

    [ShowIf("@mode == SequencerMode.EventsOnly || mode == SequencerMode.AudioAndEvents")]
    [Tooltip("Default tag for all registered events. Can be overridden per step. Used for filtering events.")]
    [SerializeField]
    string defaultEventTag = "";

    [ShowIf("@mode == SequencerMode.AudioOnly || mode == SequencerMode.AudioAndEvents")] [SerializeField]
    bool triggerAudio = true;

    [SerializeField] bool playOnStart = true;
    [SerializeField] bool loop = true;
    [SerializeField] bool canStartTransport;
    [SerializeField] bool waitForTransport;
    [SerializeField] float standaloneBpm = 100f;
    [SerializeField] bool quantizeStart = true;
    [Min(0f)] [SerializeField] float startQuantizationBeats = 1f;

    [TitleGroup("Steps", order: 1)]
    [ListDrawerSettings(ShowIndexLabels = true, ListElementLabelName = "GetStepLabel", DefaultExpandedState = true)]
    [SerializeField]
    List<SequenceStep> steps = new() { SequenceStep.Default() };

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
    ISoundGenerator activeGenerator; // The generator currently playing the note

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
        if (triggerAudio && soundGenerator == null) soundGenerator = GetComponent<ISoundGenerator>() as MonoBehaviour;

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

        if (triggerAudio && soundGenerator == null)
        {
            soundGenerator = GetComponent<ISoundGenerator>() as MonoBehaviour;
            if (soundGenerator == null)
            {
                Debug.LogWarning("StepSequencer is set to trigger audio but has no ISoundGenerator component.",
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

        // Register event for lookahead systems (visual/gameplay) - skip if event type is None
        if (tempoManager != null && step.eventType != SequenceEventType.None)
        {
            string eventTypeString = step.eventType switch
            {
                SequenceEventType.SequenceStart => "sequence_start",
                SequenceEventType.SequenceEnd => "sequence_end",
                _ => "sequence_next"
            };

            // Use per-step tag if specified, otherwise use default tag
            string eventTag = !string.IsNullOrEmpty(step.eventTag) ? step.eventTag : defaultEventTag;

            tempoManager.RegisterScheduledEvent(stepStartBeat, this, eventTypeString, stepIndexInSequence, eventTag);
            Debug.Log(
                $"[StepSequencer] Registered {eventTypeString} {stepIndexInSequence} at beat {stepStartBeat:F2} with tag '{eventTag}'");
        }

        // Schedule the step to trigger at the calculated beat (this handles audio)
        var handle = tempoManager.ScheduleAtBeat(stepStartBeat, () => { OnStepTriggered(stepIndexInSequence); });

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


    /**
     * Compiles design-time SequenceSteps into optimized RuntimeSteps with validated values.
     */
    void PrepareRuntimeSteps()
    {
        runtimeSteps.Clear();
        if (steps == null || steps.Count == 0) return;

        foreach (SequenceStep step in steps)
        {
            if (runtimeSteps.Count >= maxRuntimeSteps) break;
            if (step == null) continue;

            // Sanitize values
            float sanitizedDuration = Mathf.Max(minStepDurationBeats, step.durationBeats);
            bool sanitizedTie = !step.rest && step.tieFromPrevious;
            string sanitizedPitchName = string.IsNullOrWhiteSpace(step.pitchName) ? defaultNoteName : step.pitchName;
            float sanitizedPitchFrequency = Mathf.Max(minFrequencyHz, step.pitchFrequency);
            float sanitizedPitchSemitones = Mathf.Clamp(step.pitchSemitones, -48f, 48f);
            int sanitizedVelocity = Mathf.Clamp(step.velocity, 0, 127);

            // Resolve sound generator (step's generator or sequencer's default)
            MonoBehaviour generatorComponent = step.soundGenerator != null ? step.soundGenerator : soundGenerator;
            ISoundGenerator resolvedGenerator = generatorComponent as ISoundGenerator;

            if (resolvedGenerator == null && !step.rest && triggerAudio)
            {
                Debug.LogWarning($"Step {runtimeSteps.Count} has no valid ISoundGenerator. Skipping.", this);
                continue;
            }

            runtimeSteps.Add(new RuntimeStep(
                step,
                step.rest,
                sanitizedTie,
                sanitizedDuration,
                resolvedGenerator,
                step.usePitch,
                step.pitchMode,
                sanitizedPitchName,
                sanitizedPitchFrequency,
                sanitizedPitchSemitones,
                sanitizedVelocity,
                step.eventType,
                step.eventTag));

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

    /**
     * Applies pitch and velocity settings to the sound generator before triggering.
     */
    void ApplyStepPitch(RuntimeStep step)
    {
        if (!triggerAudio || step.generator == null) return;

        // Set velocity (always applied)
        step.generator.SetVelocity(step.velocity);

        // Set pitch (only if enabled for this step)
        if (step.usePitch)
        {
            switch (step.pitchMode)
            {
                case PitchMode.NoteName:
                    step.generator.SetPitchByName(step.pitchName);
                    break;
                case PitchMode.Frequency:
                    step.generator.SetPitch(step.pitchFrequency);
                    break;
                case PitchMode.Semitones:
                    step.generator.SetPitchOffset(step.pitchSemitones);
                    break;
            }
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

    /**
     * Determines when to begin playback based on quantization settings.
     * If quantization is enabled, aligns start to the next beat boundary; otherwise starts immediately.
     */
    void ScheduleOrStartImmediately()
    {
        // Fallback: Start immediately if no tempo manager is available
        if (tempoManager == null)
        {
            BeginPlaybackAtBeat(0d);
            return;
        }

        // Check if quantization is enabled and transport is running
        double currentBeat = tempoManager.CurrentBeat;
        bool canQuantize = quantizeStart && startQuantizationBeats > 0f && tempoManager.TransportRunning;
        if (!canQuantize)
        {
            BeginPlaybackAtBeat(currentBeat);
            return;
        }

        // Calculate the nearest quantization boundary (round down to current quantized beat)
        // quant: The quantization interval in beats (e.g., 1.0 = whole beats, 0.5 = half beats, 0.25 = quarter beats)
        //        Clamped to minimum 1e-6 to prevent division by zero
        double quant = Math.Max(1e-6, startQuantizationBeats);
        // cycles: How many complete quantization intervals have passed since beat 0
        //         Example: if currentBeat=2.7 and quant=1.0, then cycles=2 (we've completed 2 full beats)
        double cycles = Math.Floor(currentBeat / quant);
        // currentQuantizedBeat: The beat position of the most recent quantization boundary we've passed
        //                       Example: if currentBeat=2.7 and quant=1.0, then currentQuantizedBeat=2.0
        double currentQuantizedBeat = cycles * quant;

        // If we're already at (or very close to) a quantization boundary, start immediately
        if (Math.Abs(currentBeat - currentQuantizedBeat) < beatTolerance)
        {
            BeginPlaybackAtBeat(currentQuantizedBeat);
            return;
        }

        // Schedule playback to begin at the next quantization boundary
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

        [Tooltip("Duration of this step in beats.")]
        public float durationBeats = 1f;

        [Tooltip(
            "Event type for this step. None = no event registered, sequence_next = normal step, sequence_start = initialize visuals, sequence_end = cleanup visuals")]
        public SequenceEventType eventType = SequenceEventType.SequenceNext;

        [ShowIf("@$root.mode != SequencerMode.AudioOnly && eventType != SequenceEventType.None")]
        [Tooltip("Tag override for this step's event. If empty, uses the sequencer's default tag.")]
        public string eventTag = "";

        // --- Sound Generator ---
        [ShowIf("@$root.mode != SequencerMode.EventsOnly")]
        [Tooltip("Sound generator to use for this step. If null, uses the sequencer's default generator.")]
        public MonoBehaviour soundGenerator; // Will be cast to ISoundGenerator

        // --- Pitch Configuration ---
        [ShowIf("@$root.mode != SequencerMode.EventsOnly")]
        [Tooltip("Whether to set pitch for this step. Disable for unpitched drums.")]
        public bool usePitch = true;

        [ShowIf("@$root.mode != SequencerMode.EventsOnly && usePitch")]
        [Tooltip("How pitch is specified for this step.")]
        public PitchMode pitchMode = PitchMode.NoteName;

        [ShowIf("@$root.mode != SequencerMode.EventsOnly && usePitch && pitchMode == PitchMode.NoteName")]
        [Tooltip("Pitch as note name (e.g., 'C4', 'A#3').")]
        public string pitchName = defaultNoteName;

        [ShowIf("@$root.mode != SequencerMode.EventsOnly && usePitch && pitchMode == PitchMode.Frequency")]
        [Tooltip("Pitch as frequency in Hz.")]
        public float pitchFrequency = 440f;

        [ShowIf("@$root.mode != SequencerMode.EventsOnly && usePitch && pitchMode == PitchMode.Semitones")]
        [Tooltip("Pitch offset in semitones from base pitch (±48 = ±4 octaves).")]
        [Range(-48f, 48f)]
        public float pitchSemitones = 0f;

        // --- Velocity ---
        [ShowIf("@$root.mode != SequencerMode.EventsOnly")]
        [Tooltip("MIDI velocity (0-127). Default: 100 (forte).")]
        [Range(0, 127)]
        public int velocity = 100;

        // --- UnityEvents ---
        [ShowIf("@$root.mode != SequencerMode.AudioOnly")]
        [Tooltip("UnityEvents that fire immediately when this step begins.")]
        public UnityEvent onStepStart = new();

        [ShowIf("@$root.mode != SequencerMode.AudioOnly")] [Tooltip("UnityEvents that fire when this step completes.")]
        public UnityEvent onStepEnd = new();

        public static SequenceStep Default()
        {
            return new SequenceStep();
        }

        /// <summary>
        /// Provides a readable label for this step in the inspector list.
        /// </summary>
        public string GetStepLabel()
        {
            if (rest)
                return $"Rest ({durationBeats}♩)";

            if (tieFromPrevious)
                return $"Tie ({durationBeats}♩)";

            string label = "";

            // Add pitch info if relevant
            if (usePitch)
            {
                label += pitchMode switch
                {
                    PitchMode.NoteName => pitchName,
                    PitchMode.Frequency => $"{pitchFrequency}Hz",
                    PitchMode.Semitones => $"{pitchSemitones:+0;-0}st",
                    _ => ""
                };
            }
            else
            {
                label += "No pitch";
            }

            // Add duration
            label += $" ({durationBeats}♩)";

            // Add velocity if not default
            if (velocity != 100)
                label += $" vel:{velocity}";

            return label;
        }
    }

    /// <summary>
    /// Compiled runtime representation of a SequenceStep with all values validated and cached.
    /// </summary>
    readonly struct RuntimeStep
    {
        public readonly SequenceStep source; // Original step (for UnityEvents)
        public readonly bool rest; // Is this a rest (no sound)?
        public readonly bool tieFromPrevious; // Does this tie from the previous note?
        public readonly float durationBeats; // How long this step lasts
        public readonly ISoundGenerator generator; // The sound generator to use
        public readonly bool usePitch; // Should pitch be set?
        public readonly PitchMode pitchMode; // How pitch is specified
        public readonly string pitchName; // Pitch as note name
        public readonly float pitchFrequency; // Pitch as Hz
        public readonly float pitchSemitones; // Pitch as semitone offset
        public readonly int velocity; // MIDI velocity (0-127)
        public readonly SequenceEventType eventType; // Custom event type for this step
        public readonly string eventTag; // Custom tag for this step's event

        public RuntimeStep(
            SequenceStep source,
            bool rest,
            bool tieFromPrevious,
            float durationBeats,
            ISoundGenerator generator,
            bool usePitch,
            PitchMode pitchMode,
            string pitchName,
            float pitchFrequency,
            float pitchSemitones,
            int velocity,
            SequenceEventType eventType,
            string eventTag)
        {
            this.source = source;
            this.rest = rest;
            this.tieFromPrevious = tieFromPrevious;
            this.durationBeats = durationBeats;
            this.generator = generator;
            this.usePitch = usePitch;
            this.pitchMode = pitchMode;
            this.pitchName = pitchName;
            this.pitchFrequency = pitchFrequency;
            this.pitchSemitones = pitchSemitones;
            this.velocity = velocity;
            this.eventType = eventType;
            this.eventTag = eventTag;
        }

        public RuntimeStep WithTieFromPrevious(bool tieFromPrevious)
        {
            return new RuntimeStep(
                source, rest, tieFromPrevious, durationBeats, generator,
                usePitch, pitchMode, pitchName, pitchFrequency, pitchSemitones, velocity, eventType, eventTag);
        }
    }
}

