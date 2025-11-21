using System;
using System.Threading;
using UnityEngine;

/// <summary>
/// Base class for drum synthesizers providing common audio synthesis infrastructure.
/// Derived classes implement specific drum sound generation.
/// </summary>
[RequireComponent(typeof(AudioSource))]
public abstract class BaseDrumSynth : MonoBehaviour, ISoundGenerator
{
    [Header("Basic Controls")]
    [Range(0f, 1f)]
    [Tooltip("Master volume for this drum sound")]
    public float amplitude = 0.5f;

    [Header("Advanced Envelope Override")]
    [Min(0f)]
    [Tooltip("Attack time in seconds (overrides simple controls if changed)")]
    public float attack = 0.001f;

    [Min(0f)]
    [Tooltip("Decay/Release time in seconds (overrides simple controls if changed)")]
    public float decay = 0.2f;

    [Header("Output")]
    [Tooltip("Apply simple low-pass filtering")]
    public bool useFilter = false;

    [Range(100f, 20000f)]
    public float filterCutoff = 8000f;

    [Range(0.1f, 10f)]
    public float filterResonance = 0.707f;

    // Audio thread state
    protected double sampleRate;
    protected const double tau = 2.0 * Math.PI;

    // Trigger mechanism (thread-safe)
    private int triggerPending;
    private volatile bool isPlaying;
    private double envelopePhase; // 0 to 1 representing position in envelope
    private double envelopeValue; // Current envelope amplitude 0 to 1

    // Filter state
    private double cachedCutoff = -1.0;
    private double cachedResonance = -1.0;
    private double b0, b1, b2, a1, a2;
    private double z1, z2;

    // Stereo panning support (optional, auto-detected)
    private AudioSource audioSource;
    private Audio.AudioPanner2D panner;
    private float cachedPan; // Thread-safe cache of panStereo value

    protected virtual void Awake()
    {
        sampleRate = AudioSettings.outputSampleRate;
        isPlaying = false;
        envelopePhase = 0.0;
        envelopeValue = 0.0;

        // Auto-detect AudioSource and optional AudioPanner2D for stereo positioning
        audioSource = GetComponent<AudioSource>();
        panner = GetComponent<Audio.AudioPanner2D>();

        if (useFilter)
        {
            UpdateFilterCoefficients(sampleRate, filterCutoff, filterResonance);
        }

        OnDrumAwake();
    }

    protected virtual void OnDisable()
    {
        isPlaying = false;
        triggerPending = 0;
        envelopePhase = 0.0;
        envelopeValue = 0.0;
        z1 = z2 = 0.0;

        OnDrumDisable();
    }

    void Update()
    {
        // Cache pan value on main thread for use in audio thread
        if (panner != null && audioSource != null)
        {
            cachedPan = audioSource.panStereo;
        }
    }

    /// <summary>
    /// Triggers a one-shot drum hit. Thread-safe, can be called from any thread.
    /// </summary>
    public void TriggerOneShot()
    {
        if (!isActiveAndEnabled)
            return;

        Interlocked.Increment(ref triggerPending);
    }

    /// <summary>
    /// Sets the amplitude/volume for this drum.
    /// </summary>
    public void SetAmplitude(float value)
    {
        amplitude = Mathf.Clamp01(value);
    }

    // --- ISoundGenerator Interface Implementation ---

    /// <summary>
    /// ISoundGenerator: Sets pitch by frequency. Drums can override to implement pitch control.
    /// Default implementation does nothing (drums are typically unpitched).
    /// </summary>
    public virtual void SetPitch(float frequencyHz)
    {
        // Override in derived classes for pitched drums (e.g., 808 kick with tunable pitch)
        OnPitchChanged(frequencyHz);
    }

    /// <summary>
    /// ISoundGenerator: Sets pitch by note name. Converts to frequency and calls SetPitch.
    /// </summary>
    public void SetPitchByName(string noteName)
    {
        if (TryParseNote(noteName, out float frequency))
        {
            SetPitch(frequency);
        }
    }

    /// <summary>
    /// ISoundGenerator: Sets pitch offset in semitones. Drums can override for tuning.
    /// Default implementation does nothing.
    /// </summary>
    public virtual void SetPitchOffset(float semitones)
    {
        // Override in derived classes for pitch-bendable drums
    }

    /// <summary>
    /// ISoundGenerator: Sets velocity (MIDI 0-127). Converts to amplitude.
    /// </summary>
    public void SetVelocity(int velocity)
    {
        // Convert MIDI velocity to amplitude (linear for drums - they handle dynamics differently)
        amplitude = Mathf.Clamp01(velocity / 127f);
    }

    /// <summary>
    /// ISoundGenerator: Triggers the drum (note on).
    /// </summary>
    public void NoteOn()
    {
        TriggerOneShot();
    }

    /// <summary>
    /// ISoundGenerator: Release note (note off). Drums ignore this - they're one-shots.
    /// </summary>
    public void NoteOff()
    {
        // Drums are one-shots, no action needed
    }

    /// <summary>
    /// Called when pitch is changed. Override to implement pitch-sensitive drums.
    /// </summary>
    protected virtual void OnPitchChanged(float frequencyHz)
    {
        // Override in derived classes to handle pitch
    }

    // --- End ISoundGenerator Interface ---

    private void OnAudioFilterRead(float[] data, int channels)
    {
        if (sampleRate <= 0.0)
            sampleRate = AudioSettings.outputSampleRate;

        // Update filter coefficients if changed
        if (useFilter && (!Mathf.Approximately((float)cachedCutoff, filterCutoff) ||
                          !Mathf.Approximately((float)cachedResonance, filterResonance)))
        {
            UpdateFilterCoefficients(sampleRate, filterCutoff, filterResonance);
        }

        // Check for new triggers
        if (Interlocked.Exchange(ref triggerPending, 0) > 0)
        {
            isPlaying = true;
            envelopePhase = 0.0;
            envelopeValue = 0.0;
            OnTrigger();
        }

        double deltaTime = 1.0 / sampleRate;

        for (int i = 0; i < data.Length; i += channels)
        {
            double rawSample = 0.0;

            if (isPlaying)
            {
                // Update envelope
                UpdateEnvelope(deltaTime);

                // Generate drum-specific sample
                rawSample = GenerateDrumSample() * amplitude * envelopeValue;

                // Advance envelope phase
                envelopePhase += deltaTime;

                // Check if envelope is complete
                if (envelopeValue <= 0.00001 && envelopePhase > attack)
                {
                    isPlaying = false;
                    envelopeValue = 0.0;
                }
            }

            // Apply filter if enabled
            double processedSample = useFilter ? ProcessFilter(rawSample) : rawSample;
            float sampleValue = (float)processedSample;

            // Apply stereo panning if AudioPanner2D is present
            if (panner != null && audioSource != null && channels == 2)
            {
                // Use cached pan value (updated on main thread)
                // pan: -1 (left) to 1 (right)
                float pan = cachedPan;

                // Equal-power panning for smooth stereo imaging
                float leftGain = Mathf.Sqrt((1f - pan) / 2f);
                float rightGain = Mathf.Sqrt((1f + pan) / 2f);

                data[i] = sampleValue * leftGain;      // Left channel
                data[i + 1] = sampleValue * rightGain; // Right channel
            }
            else
            {
                // No panner or mono output - write same value to all channels
                for (int ch = 0; ch < channels; ch++)
                    data[i + ch] = sampleValue;
            }
        }
    }

    /// <summary>
    /// Updates the envelope value based on attack and decay times.
    /// </summary>
    private void UpdateEnvelope(double deltaTime)
    {
        double safeAttack = Math.Max(attack, 0.0001);
        double safeDecay = Math.Max(decay, 0.0001);

        if (envelopePhase < safeAttack)
        {
            // Attack phase: 0 -> 1
            envelopeValue = envelopePhase / safeAttack;
        }
        else
        {
            // Decay phase: 1 -> 0
            double decayPhase = envelopePhase - safeAttack;
            envelopeValue = Math.Max(0.0, 1.0 - (decayPhase / safeDecay));
        }

        // Allow derived classes to modify envelope
        envelopeValue = ModifyEnvelope(envelopeValue, envelopePhase);
    }

    /// <summary>
    /// Updates the low-pass filter coefficients using the biquad formula.
    /// </summary>
    private void UpdateFilterCoefficients(double currentSampleRate, float cutoffHz, float resonanceQ)
    {
        double nyquist = currentSampleRate * 0.5;
        double clampedCutoff = Math.Max(10.0, Math.Min(cutoffHz, nyquist - 10.0));
        double clampedResonance = Math.Max(0.1, resonanceQ);

        double omega = 2.0 * Math.PI * clampedCutoff / currentSampleRate;
        double sin = Math.Sin(omega);
        double cos = Math.Cos(omega);
        double alpha = sin / (2.0 * clampedResonance);

        double invA0 = 1.0 / (1.0 + alpha);
        b0 = ((1.0 - cos) * 0.5) * invA0;
        b1 = (1.0 - cos) * invA0;
        b2 = b0;
        a1 = (-2.0 * cos) * invA0;
        a2 = (1.0 - alpha) * invA0;

        cachedCutoff = cutoffHz;
        cachedResonance = resonanceQ;
    }

    /// <summary>
    /// Applies the low-pass filter to the input sample.
    /// </summary>
    private double ProcessFilter(double input)
    {
        double output = b0 * input + z1;
        z1 = b1 * input + z2 - a1 * output;
        z2 = b2 * input - a2 * output;
        return output;
    }

    // ===== Abstract and Virtual Methods for Derived Classes =====

    /// <summary>
    /// Called during Awake for drum-specific initialization.
    /// </summary>
    protected virtual void OnDrumAwake() { }

    /// <summary>
    /// Called during OnDisable for drum-specific cleanup.
    /// </summary>
    protected virtual void OnDrumDisable() { }

    /// <summary>
    /// Called when a trigger occurs. Use this to reset oscillator phases, etc.
    /// </summary>
    protected virtual void OnTrigger() { }

    /// <summary>
    /// Generate a single sample for this drum sound.
    /// Called once per audio sample when the drum is playing.
    /// Envelope is applied automatically after this.
    /// </summary>
    /// <returns>Sample value (typically -1 to 1)</returns>
    protected abstract double GenerateDrumSample();

    /// <summary>
    /// Allows derived classes to modify the envelope curve.
    /// </summary>
    /// <param name="envelopeValue">Current envelope value (0 to 1)</param>
    /// <param name="phase">Current time since trigger in seconds</param>
    /// <returns>Modified envelope value</returns>
    protected virtual double ModifyEnvelope(double envelopeValue, double phase)
    {
        return envelopeValue;
    }

    // ===== Helper Properties =====

    /// <summary>
    /// Returns true if the drum is currently playing.
    /// </summary>
    public bool IsPlaying => isPlaying;

    /// <summary>
    /// Returns the current envelope phase in seconds since trigger.
    /// </summary>
    protected double EnvelopePhase => envelopePhase;

    /// <summary>
    /// Returns the current envelope value (0 to 1).
    /// </summary>
    protected double EnvelopeValue => envelopeValue;

    // ===== Helper Methods =====

    /// <summary>
    /// Parses scientific pitch notation (e.g., "A4", "C#3") to frequency in Hz.
    /// </summary>
    /// <param name="input">Note name string</param>
    /// <param name="frequencyOut">Output frequency in Hz</param>
    /// <returns>True if parsing succeeded</returns>
    protected static bool TryParseNote(string input, out float frequencyOut)
    {
        frequencyOut = 0f;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string trimmed = input.Trim();
        if (trimmed.Length < 2)
            return false;

        char letter = char.ToUpperInvariant(trimmed[0]);
        int semitoneFromC;
        switch (letter)
        {
            case 'C': semitoneFromC = 0; break;
            case 'D': semitoneFromC = 2; break;
            case 'E': semitoneFromC = 4; break;
            case 'F': semitoneFromC = 5; break;
            case 'G': semitoneFromC = 7; break;
            case 'A': semitoneFromC = 9; break;
            case 'B': semitoneFromC = 11; break;
            default: return false;
        }

        int index = 1;
        if (index < trimmed.Length)
        {
            char accidental = trimmed[index];
            if (accidental == '#')
            {
                semitoneFromC += 1;
                index++;
            }
            else if (accidental == 'b' || accidental == 'B')
            {
                semitoneFromC -= 1;
                index++;
            }
        }

        if (index >= trimmed.Length)
            return false;

        if (!int.TryParse(trimmed.Substring(index), out int octave))
            return false;

        while (semitoneFromC < 0)
        {
            semitoneFromC += 12;
            octave -= 1;
        }
        while (semitoneFromC >= 12)
        {
            semitoneFromC -= 12;
            octave += 1;
        }

        int midiNote = (octave + 1) * 12 + semitoneFromC;
        double exponent = (midiNote - 69) / 12.0;
        frequencyOut = (float)(440.0 * Math.Pow(2.0, exponent));
        return frequencyOut > 0f;
    }
}
