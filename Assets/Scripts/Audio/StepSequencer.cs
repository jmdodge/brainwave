using System;
using System.Collections.Generic;
using UnityEngine;

[AddComponentMenu("Audio/Step Sequencer")]
public sealed class StepSequencer : MonoBehaviour
{
    [SerializeField] TempoManager tempoManager;
    [SerializeField] SineWaveGenerator sineWaveGenerator;
    [SerializeField] bool playOnStart = true;
    [SerializeField] bool loop = true;
    [SerializeField] bool canStartTransport;
    [SerializeField] float standaloneBpm = 100f;
    [SerializeField] List<SequenceStep> steps = new() { SequenceStep.Default() };

    readonly List<SequenceStep> runtimeSteps = new();

    const double beatTolerance = 1e-4;
    const float minStepDurationBeats = 0.015625f; // 1/64 note
    const float minFrequencyHz = 20f;

    double currentCycleStartBeat;
    double currentStepStartBeat;
    double currentStepEndBeat;
    double cycleLengthBeats;
    int currentStepIndex;
    bool isRunning;
    bool noteActive;

    void OnEnable()
    {
        if (tempoManager == null) tempoManager = FindObjectOfType<TempoManager>();
        if (sineWaveGenerator == null) sineWaveGenerator = GetComponent<SineWaveGenerator>();
        if (!playOnStart) return;
        StartSequence();
    }

    void Start()
    {
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
            Debug.LogWarning("StepSequencer requires a TempoManager reference.", this);
            return;
        }

        if (sineWaveGenerator == null)
        {
            Debug.LogWarning("StepSequencer requires a SineWaveGenerator reference.", this);
            return;
        }

        PrepareRuntimeSteps();
        if (runtimeSteps.Count == 0)
        {
            Debug.LogWarning("StepSequencer has no valid steps to play.", this);
            return;
        }

        EnsureTransportRunning();

        double currentBeat = tempoManager.CurrentBeat;
        currentCycleStartBeat = currentBeat;
        currentStepIndex = 0;
        currentStepStartBeat = currentBeat;
        isRunning = true;
        noteActive = false;

        StartCurrentStep();
    }

    public void StopSequence()
    {
        isRunning = false;
        if (noteActive)
        {
            sineWaveGenerator.NoteOff();
            noteActive = false;
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

    void CompleteCurrentStep()
    {
        bool continueNote = ShouldContinueNote();
        if (noteActive && !continueNote)
        {
            sineWaveGenerator.NoteOff();
            noteActive = false;
        }

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

        SequenceStep step = runtimeSteps[currentStepIndex];
        bool tiesFromPrevious = StepTiesFromPrevious(currentStepIndex);

        if (step.rest)
        {
            if (noteActive)
            {
                sineWaveGenerator.NoteOff();
                noteActive = false;
            }
        }
        else if (!tiesFromPrevious)
        {
            ApplyStepPitch(step);
            sineWaveGenerator.NoteOn();
            noteActive = true;
        }

        currentStepEndBeat = currentStepStartBeat + Math.Max(minStepDurationBeats, step.durationBeats);
    }

    void CompleteCycleIfNeeded()
    {
        if (runtimeSteps.Count == 0) return;
        cycleLengthBeats = 0.0;
        for (int i = 0; i < runtimeSteps.Count; i++)
        {
            cycleLengthBeats += Math.Max(minStepDurationBeats, runtimeSteps[i].durationBeats);
        }
        cycleLengthBeats = Math.Max(minStepDurationBeats, cycleLengthBeats);
    }

    void PrepareRuntimeSteps()
    {
        runtimeSteps.Clear();
        if (steps == null || steps.Count == 0) return;

        foreach (SequenceStep step in steps)
        {
            if (runtimeSteps.Count >= 64) break;

            SequenceStep sanitized = step;
            sanitized.durationBeats = Math.Max(minStepDurationBeats, sanitized.durationBeats);
            sanitized.frequencyHz = Math.Max(minFrequencyHz, sanitized.frequencyHz);
            if (sanitized.rest) sanitized.tieFromPrevious = false;
            runtimeSteps.Add(sanitized);
        }

        if (runtimeSteps.Count == 0) return;

        if (!loop)
        {
            SequenceStep first = runtimeSteps[0];
            if (first.tieFromPrevious)
            {
                first.tieFromPrevious = false;
                runtimeSteps[0] = first;
            }
        }

        for (int i = 0; i < runtimeSteps.Count; i++)
        {
            if (!runtimeSteps[i].tieFromPrevious) continue;

            int prevIndex = i == 0 ? runtimeSteps.Count - 1 : i - 1;
            if (i == 0 && !loop)
            {
                SequenceStep step = runtimeSteps[i];
                step.tieFromPrevious = false;
                runtimeSteps[i] = step;
                continue;
            }

            if (runtimeSteps[prevIndex].rest)
            {
                SequenceStep step = runtimeSteps[i];
                step.tieFromPrevious = false;
                runtimeSteps[i] = step;
            }
        }

        CompleteCycleIfNeeded();
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

        SequenceStep nextStep = runtimeSteps[nextIndex];
        if (nextStep.rest) return false;
        return StepTiesFromPrevious(nextIndex);
    }

    bool StepTiesFromPrevious(int stepIndex)
    {
        if (runtimeSteps.Count == 0) return false;

        SequenceStep step = runtimeSteps[stepIndex];
        if (!step.tieFromPrevious) return false;

        if (stepIndex == 0)
        {
            if (!loop) return false;
            return !runtimeSteps[runtimeSteps.Count - 1].rest;
        }

        return !runtimeSteps[stepIndex - 1].rest;
    }

    void ApplyStepPitch(SequenceStep step)
    {
        if (sineWaveGenerator == null) return;
        if (step.useNoteName)
        {
            sineWaveGenerator.SetNoteName(step.noteName);
        }
        else
        {
            sineWaveGenerator.SetFrequency(Mathf.Max(minFrequencyHz, step.frequencyHz));
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

    [Serializable]
    public struct SequenceStep
    {
        [Tooltip("Marks this step as a rest; no note is triggered.")]
        public bool rest;

        [Tooltip("When true, this step sustains the previous note instead of retriggering.")]
        public bool tieFromPrevious;

        [Tooltip("Use scientific pitch notation (e.g. C4). When false, frequencyHz is used.")]
        public bool useNoteName;

        [Tooltip("Scientific pitch notation for this step.")]
        public string noteName;

        [Tooltip("Raw frequency in hertz for this step when not using note names.")]
        public float frequencyHz;

        [Tooltip("Duration of this step in beats.")]
        public float durationBeats;

        public static SequenceStep Default()
        {
            return new SequenceStep
            {
                rest = false,
                tieFromPrevious = false,
                useNoteName = true,
                noteName = "A4",
                frequencyHz = 440f,
                durationBeats = 1f
            };
        }
    }
}
