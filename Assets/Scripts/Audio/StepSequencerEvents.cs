using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Sirenix.OdinInspector;

[AddComponentMenu("Audio/Step Sequencer - Events")]
public sealed class StepSequencerEvents : MonoBehaviour
{
    [SerializeField] TempoManager tempoManager;
    [SerializeField] SineWaveGenerator sineWaveGenerator;
    [SerializeField] bool triggerAudio = true;
    [SerializeField] bool playOnStart = true;
    [SerializeField] bool loop = true;
    [SerializeField] bool canStartTransport;
    [SerializeField] float standaloneBpm = 100f;
    [SerializeField] bool quantizeStart = true;
    [Min(0f)] [SerializeField] float startQuantizationBeats = 1f;
    [TableList(ShowIndexLabels = true, AlwaysExpanded = true)]
    [SerializeField] List<SequenceStep> steps = new() { SequenceStep.Default() };

    readonly List<RuntimeStep> runtimeSteps = new();

    const double beatTolerance = 1e-4;
    const float minStepDurationBeats = 0.015625f;
    const float minFrequencyHz = 20f;
    const int maxRuntimeSteps = 64;
    const string defaultNoteName = "A4";

    double currentCycleStartBeat;
    double currentStepStartBeat;
    double currentStepEndBeat;
    double cycleLengthBeats;
    int currentStepIndex;
    bool isRunning;
    bool noteActive;
    SineWaveGenerator activeGenerator;
    bool startScheduled;
    double scheduledStartBeat;
    TempoManager.TempoEventHandle startHandle;

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
        if (triggerAudio && sineWaveGenerator == null) sineWaveGenerator = GetComponent<SineWaveGenerator>();
        if (!playOnStart) return;
        StartSequence();
    }

    void Update()
    {
        if (!isRunning || tempoManager == null || runtimeSteps.Count == 0) return;

        double currentBeat = tempoManager.CurrentBeat;
        while (isRunning && currentBeat + beatTolerance >= currentStepEndBeat)
        {
            CompleteCurrentStep();
        }
    }

    public void StartSequence()
    {
        StopSequence();

        if (tempoManager == null)
        {
            Debug.LogWarning("StepSequencerEvents requires a TempoManager reference.", this);
            return;
        }

        if (triggerAudio && sineWaveGenerator == null)
        {
            sineWaveGenerator = GetComponent<SineWaveGenerator>();
            if (sineWaveGenerator == null)
            {
                Debug.LogWarning("StepSequencerEvents is set to trigger audio but has no SineWaveGenerator reference.",
                    this);
            }
        }

        PrepareRuntimeSteps();
        if (runtimeSteps.Count == 0)
        {
            Debug.LogWarning("StepSequencerEvents has no valid steps to play.", this);
            return;
        }

        EnsureTransportRunning();

        ScheduleOrStartImmediately();
    }

    public void StopSequence()
    {
        if (startScheduled && tempoManager != null)
        {
            tempoManager.CancelScheduledEvent(startHandle);
            startScheduled = false;
        }

        isRunning = false;
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
            Debug.LogWarning("StepSequencerEvents cannot start transport without a TempoManager.", this);
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

    void CompleteCurrentStep()
    {
        if (runtimeSteps.Count == 0) return;

        int completedIndex = currentStepIndex;
        RuntimeStep completedStep = runtimeSteps[completedIndex];

        bool continueNote = ShouldContinueNote();
        if (noteActive && !continueNote && triggerAudio && activeGenerator != null)
        {
            activeGenerator.NoteOff();
            noteActive = false;
            activeGenerator = null;
        }

        InvokeStepEndEvent(completedStep);

        double nextStepStart = currentStepEndBeat;
        if (!AdvanceStepIndex())
        {
            isRunning = false;
            return;
        }

        currentStepStartBeat = currentStepIndex == 0 ? currentCycleStartBeat : nextStepStart;
        StartCurrentStep();
    }

    void StartCurrentStep()
    {
        if (!isRunning || runtimeSteps.Count == 0) return;

        RuntimeStep step = runtimeSteps[currentStepIndex];
        bool tiesFromPrevious = StepTiesFromPrevious(currentStepIndex);

        if (step.rest)
        {
            if (noteActive && triggerAudio && activeGenerator != null)
            {
                activeGenerator.NoteOff();
                noteActive = false;
                activeGenerator = null;
            }
        }
        else if (!tiesFromPrevious && triggerAudio && step.generator != null)
        {
            ApplyStepPitch(step);
            step.generator.NoteOn();
            noteActive = true;
            activeGenerator = step.generator;
        }

        InvokeStepStartEvent(step);

        currentStepEndBeat = currentStepStartBeat + Mathf.Max(minStepDurationBeats, step.durationBeats);
    }

    void CompleteCycleIfNeeded()
    {
        if (runtimeSteps.Count == 0) return;
        cycleLengthBeats = 0.0;
        for (int i = 0; i < runtimeSteps.Count; i++)
        {
            cycleLengthBeats += Math.Max((double)minStepDurationBeats, runtimeSteps[i].durationBeats);
        }

        cycleLengthBeats = Math.Max((double)minStepDurationBeats, cycleLengthBeats);
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
            SineWaveGenerator resolvedGenerator = step.sineWaveGenerator != null ? step.sineWaveGenerator : sineWaveGenerator;

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

        CompleteCycleIfNeeded();
    }

    static void EnsureUnityEvents(SequenceStep step)
    {
        if (step == null) return;
        step.onStepStart ??= new UnityEvent();
        step.onStepEnd ??= new UnityEvent();
    }

    bool AdvanceStepIndex()
    {
        if (runtimeSteps.Count == 0) return false;

        if (currentStepIndex + 1 >= runtimeSteps.Count)
        {
            if (!loop) return false;

            currentStepIndex = 0;
            currentCycleStartBeat += cycleLengthBeats;
            return true;
        }

        currentStepIndex++;
        return true;
    }

    bool ShouldContinueNote()
    {
        if (!noteActive || runtimeSteps.Count == 0) return false;

        int nextIndex = currentStepIndex + 1;
        if (nextIndex >= runtimeSteps.Count)
        {
            if (!loop) return false;
            nextIndex = 0;
        }

        RuntimeStep nextStep = runtimeSteps[nextIndex];
        if (nextStep.rest) return false;
        return StepTiesFromPrevious(nextIndex);
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
        StopSequence();
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
        double targetBeat = (cycles + 1d) * quant;
        if (targetBeat - currentBeat < beatTolerance) targetBeat += quant;

        startScheduled = true;
        scheduledStartBeat = targetBeat;
        startHandle = tempoManager.ScheduleAtBeat(targetBeat, OnScheduledStart);
    }

    void OnScheduledStart()
    {
        startScheduled = false;
        BeginPlaybackAtBeat(scheduledStartBeat);
    }

    void BeginPlaybackAtBeat(double startBeat)
    {
        currentCycleStartBeat = startBeat;
        currentStepIndex = 0;
        currentStepStartBeat = startBeat;
        isRunning = true;
        noteActive = false;
        activeGenerator = null;
        StartCurrentStep();
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

        [Tooltip("Optional SineWaveGenerator to use for this step. If null, uses the default generator.")]
        public SineWaveGenerator sineWaveGenerator;

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
        public readonly SineWaveGenerator generator;

        public RuntimeStep(
            SequenceStep source,
            bool rest,
            bool tieFromPrevious,
            bool useNoteName,
            string noteName,
            float frequencyHz,
            float durationBeats,
            SineWaveGenerator generator)
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

