using System;
using System.Collections.Generic;
using UnityEngine;

/**
 * Drives a SineWaveGenerator with a repeating beat pattern to create a simple rhythmic metronome.
 * Example:
 * rhythmicSynth.SetPitch("C4");
 * rhythmicSynth.StartPattern();
 */
[AddComponentMenu("Audio/Rhythmic Synthesizer")]
public sealed class RhythmicSynthesizer : MonoBehaviour
{
    [SerializeField] TempoManager tempoManager;
    [SerializeField] SineWaveGenerator sineWaveGenerator;
    [SerializeField] bool playOnStart = true;
    [SerializeField] bool useNoteName = true;
    [SerializeField] string noteName = "A4";
    [SerializeField] float frequency = 440f;
    [Min(0.25f)]
    [SerializeField] float cycleLengthBeats = 4f;
    /**
     * Beat positions inside the cycle (in beats) when the tone should trigger.
     * Examples:
     * - { 0f } plays once every cycle on the downbeat.
     * - { 0f, 2f } plays on beats 1 and 3 of a four-beat measure.
     * - { 0f, 0.5f, 1f, 1.5f } creates an eighth-note pattern.
     */
    [SerializeField] List<float> beatOffsets = new() { 0f };
    [SerializeField] bool canStartTransport;
    [SerializeField] float standaloneBpm = 100f;

    readonly List<double> normalizedOffsets = new();

    const double beatTolerance = 1e-4;
    bool isRunning;
    double currentCycleStartBeat;
    double nextTriggerBeat;
    int stepIndex;

    /**
     * Ensures references are resolved when the component is enabled.
     */
    void OnEnable()
    {
        if (tempoManager == null) tempoManager = FindAnyObjectByType<TempoManager>();
        if (sineWaveGenerator == null) sineWaveGenerator = GetComponent<SineWaveGenerator>();
    }

    /**
     * Starts the rhythmic pattern automatically when requested.
     */
    void Start()
    {
        if (!playOnStart) return;
        StartPattern();
    }

    /**
     * Advances timing and fires steps once the current beat passes the next trigger.
     */
    void Update()
    {
        if (!isRunning || tempoManager == null || normalizedOffsets.Count == 0) return;

        double currentBeat = tempoManager.CurrentBeat;
        while (currentBeat + beatTolerance >= nextTriggerBeat)
        {
            TriggerStep();
            AdvanceStep();
        }
    }

    /**
     * Applies a note name for future triggers and instantly synchronises the SineWaveGenerator.
     */
    public void SetPitch(string newNote)
    {
        useNoteName = true;
        noteName = newNote;
        ApplyPitch();
    }

    /**
     * Applies a raw frequency in hertz for future triggers and synchronises the SineWaveGenerator.
     */
    public void SetFrequency(float hz)
    {
        useNoteName = false;
        frequency = hz;
        ApplyPitch();
    }

    /**
     * Starts playback of the rhythmic pattern from the nearest cycle boundary.
     */
    public void StartPattern()
    {
        if (tempoManager == null)
        {
            Debug.LogWarning("RhythmicSynthesizer requires a TempoManager reference.", this);
            return;
        }

        if (sineWaveGenerator == null)
        {
            Debug.LogWarning("RhythmicSynthesizer requires a SineWaveGenerator reference.", this);
            return;
        }

        EnsureTransportRunning();

        if (cycleLengthBeats < 0.0001f)
        {
            Debug.LogWarning("RhythmicSynthesizer cycle length must be positive.", this);
            cycleLengthBeats = 1f;
        }

        BuildNormalizedOffsets();
        if (normalizedOffsets.Count == 0)
        {
            normalizedOffsets.Add(0d);
        }

        ApplyPitch();

        double currentBeat = tempoManager.CurrentBeat;
        currentCycleStartBeat = cycleLengthBeats > 0f
            ? Math.Floor(currentBeat / cycleLengthBeats) * cycleLengthBeats
            : currentBeat;

        stepIndex = 0;
        DetermineNextTrigger(currentBeat);
        isRunning = true;
    }

    /**
     * Stops playback, clearing the running flag.
     */
    public void StopPattern()
    {
        isRunning = false;
    }

    /**
     * Starts the global tempo transport using the configured standalone BPM.
     */
    public void StartTransportManually()
    {
        if (tempoManager == null)
        {
            Debug.LogWarning("RhythmicSynthesizer cannot start transport because no TempoManager is assigned.", this);
            return;
        }

        double dspNow = AudioSettings.dspTime;
        tempoManager.SetTempo(Mathf.Max(1f, standaloneBpm), dspNow);
        tempoManager.StartTransport(dspNow);
    }

    /**
     * Updates the beat offsets used for the pattern.
     */
    public void SetBeatOffsets(IEnumerable<float> offsets)
    {
        if (offsets == null) return;
        beatOffsets = new List<float>(offsets);
        if (isRunning) StartPattern();
    }

    /**
     * Applies the currently configured pitch to the SineWaveGenerator.
     */
    void ApplyPitch()
    {
        if (sineWaveGenerator == null) return;

        if (useNoteName)
        {
            sineWaveGenerator.SetNoteName(noteName);
        }
        else
        {
            sineWaveGenerator.SetFrequency(Mathf.Max(20f, frequency));
        }
    }

    /**
     * Ensures the tempo transport is running, starting it if allowed.
     */
    void EnsureTransportRunning()
    {
        if (tempoManager == null) return;
        if (tempoManager.TransportRunning || !canStartTransport) return;

        double dspNow = AudioSettings.dspTime;
        tempoManager.SetTempo(Mathf.Max(1f, standaloneBpm), dspNow);
        tempoManager.StartTransport(dspNow);
    }

    /**
     * Derives a sorted, normalized set of beat offsets within the configured cycle length.
     */
    void BuildNormalizedOffsets()
    {
        normalizedOffsets.Clear();

        if (beatOffsets == null || beatOffsets.Count == 0)
        {
            normalizedOffsets.Add(0d);
            return;
        }

        double cycle = Math.Max(0.0001f, cycleLengthBeats);

        foreach (float offset in beatOffsets)
        {
            /**
             * Offsets wrap into the current cycle length so values beyond the cycle (e.g. 5 in a 4-beat loop)
             * or negatives (e.g. -0.5 to hit just before the downbeat) still land correctly within the pattern.
             */
            double normalized = offset;
            normalized %= cycle;
            if (normalized < 0.0) normalized += cycle;
            normalizedOffsets.Add(normalized);
        }

        normalizedOffsets.Sort();
    }

    /**
     * Determines the next beat trigger based on the current step index and cycle.
     */
    void DetermineNextTrigger(double referenceBeat)
    {
        if (normalizedOffsets.Count == 0)
        {
            nextTriggerBeat = referenceBeat + beatTolerance;
            return;
        }

        while (true)
        {
            double candidate = currentCycleStartBeat + normalizedOffsets[stepIndex];
            if (candidate >= referenceBeat - beatTolerance)
            {
                nextTriggerBeat = candidate;
                break;
            }

            AdvanceStep();
        }
    }

    /**
     * Advances to the next step, rolling into the next cycle when the pattern completes.
     */
    void AdvanceStep()
    {
        if (normalizedOffsets.Count == 0)
        {
            nextTriggerBeat += cycleLengthBeats;
            return;
        }

        stepIndex++;
        if (stepIndex >= normalizedOffsets.Count)
        {
            stepIndex = 0;
            currentCycleStartBeat += cycleLengthBeats;
        }

        nextTriggerBeat = currentCycleStartBeat + normalizedOffsets[stepIndex];
    }

    /**
     * Triggers the SineWaveGenerator for the current step.
     */
    void TriggerStep()
    {
        ApplyPitch();
        sineWaveGenerator.TriggerOneShot();
    }
}

