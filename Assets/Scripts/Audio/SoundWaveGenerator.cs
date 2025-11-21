using System;
using System.Threading;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class SoundWaveGenerator : MonoBehaviour, ISoundGenerator
{
    public enum Waveform
    {
        Sine,
        Saw,
        Triangle
    }

    [Tooltip("Use scientific pitch notation, e.g. A4, C#3")]
    public bool useNoteInput;
    public string note = "A4";
    [Range(20f, 2000f)] public float frequency = 440f;
    [Range(0f, 1f)] public float amplitude = 0.1f;
    public Waveform waveform = Waveform.Sine;
    [Range(10f, 20000f)] public float cutoff = 1000f;
    [Range(0.1f, 10f)] public float resonance = 0.707f;
    [Header("Envelope (seconds / 0-1)")]
    [Min(0f)] public float attack = 0.01f;
    [Min(0f)] public float decay = 0.1f;
    [Range(0f, 1f)] public float sustain = 0.7f;
    [Min(0f)] public float release = 0.2f;
    public bool triggerOnEnable;
    double phase;           // keeps waveform continuous across callbacks
    const double tau = 2.0 * Mathf.PI;
    double sampleRate;
    double cachedCutoff = -1.0;
    double cachedResonance = -1.0;
    double b0, b1, b2, a1, a2;
    double z1, z2;

    // Stereo panning support (optional, auto-detected)
    AudioSource audioSource;
    Audio.AudioPanner2D panner;
    float cachedPan; // Thread-safe cache of panStereo value

    bool gateRequested;
    volatile bool autoReleasePending;
    int retriggerPending;
    bool gateActive;

    double envelopeValue;
    EnvelopeState envelopeState = EnvelopeState.Attack;
    double resolvedFrequency = 440.0;
    bool cachedUseNoteInput;
    string cachedNoteInput = string.Empty;
    float cachedFrequencyValue;

    void Awake()
    {
        sampleRate = AudioSettings.outputSampleRate;
        gateRequested = false;
        gateActive = false;
        autoReleasePending = false;
        envelopeValue = 0.0;
        envelopeState = EnvelopeState.Idle;
        cachedUseNoteInput = useNoteInput;
        cachedNoteInput = note;
        cachedFrequencyValue = frequency;

        // Auto-detect AudioSource and optional AudioPanner2D for stereo positioning
        audioSource = GetComponent<AudioSource>();
        panner = GetComponent<Audio.AudioPanner2D>();

        RefreshFrequencyCache(true);
        UpdateFilterCoefficients(sampleRate, cutoff, resonance);
    }

    void OnEnable()
    {
        if (triggerOnEnable)
        {
            NoteOn();
            autoReleasePending = true;
        }
    }

    void OnValidate()
    {
        cachedUseNoteInput = useNoteInput;
        cachedNoteInput = note;
        cachedFrequencyValue = frequency;
        RefreshFrequencyCache(true);
    }

    void Update()
    {
        // Cache pan value on main thread for use in audio thread
        if (panner != null && audioSource != null)
        {
            cachedPan = audioSource.panStereo;
        }
    }

    void OnAudioFilterRead(float[] data, int channels)
    {
        if (sampleRate <= 0.0)
            sampleRate = AudioSettings.outputSampleRate;

        if (!Mathf.Approximately((float)cachedCutoff, cutoff) ||
            !Mathf.Approximately((float)cachedResonance, resonance))
        {
            UpdateFilterCoefficients(sampleRate, cutoff, resonance);
        }

        bool desiredGate = Volatile.Read(ref gateRequested);
        if (desiredGate && !gateActive)
        {
            gateActive = true;
            envelopeState = EnvelopeState.Attack;
        }
        else if (!desiredGate && gateActive)
        {
            gateActive = false;
            if (envelopeState != EnvelopeState.Idle)
                envelopeState = EnvelopeState.Release;
        }

        if (Interlocked.Exchange(ref retriggerPending, 0) > 0)
        {
            gateActive = true;
            envelopeState = EnvelopeState.Attack;
            envelopeValue = 0.0;
        }

        RefreshFrequencyCache();
        double currentFrequency = Interlocked.CompareExchange(ref resolvedFrequency, 0.0, 0.0);
        if (currentFrequency <= 0.0)
            currentFrequency = 440.0;
        double increment = currentFrequency * tau / sampleRate;

        for (int i = 0; i < data.Length; i += channels)
        {
            double env = UpdateEnvelope();
            double rawSample = amplitude * env * GenerateSample(phase);
            double filteredSample = ProcessFilter(rawSample);
            float sampleValue = (float)filteredSample;

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

            phase += increment;
            phase %= tau;    // wrap to avoid overflow
        }
    }

    enum EnvelopeState
    {
        Attack,
        Decay,
        Sustain,
        Release,
        Idle
    }

    double UpdateEnvelope()
    {
        double deltaTime = 1.0 / sampleRate;
        switch (envelopeState)
        {
            case EnvelopeState.Attack:
            {
                double attackTime = Math.Max(attack, 0.0001f);
                envelopeValue += deltaTime / attackTime;
                if (envelopeValue >= 1.0)
                {
                    envelopeValue = 1.0;
                    envelopeState = EnvelopeState.Decay;
                }
                break;
            }
            case EnvelopeState.Decay:
            {
                double decayTime = Math.Max(decay, 0.0001f);
                double targetValue = sustain;
                envelopeValue -= (1.0 - targetValue) * (deltaTime / decayTime);
                if (envelopeValue <= targetValue)
                {
                    envelopeValue = targetValue;
                    envelopeState = gateActive ? EnvelopeState.Sustain : EnvelopeState.Release;
                }
                break;
            }
            case EnvelopeState.Sustain:
            {
                envelopeValue = sustain;
                if (!gateActive)
                {
                    envelopeState = EnvelopeState.Release;
                }
                break;
            }
            case EnvelopeState.Release:
            {
                double releaseTime = Math.Max(release, 0.0001f);
                envelopeValue -= envelopeValue * (deltaTime / releaseTime);
                if (envelopeValue <= 0.00001)
                {
                    envelopeValue = 0.0;
                    envelopeState = gateActive ? EnvelopeState.Attack : EnvelopeState.Idle;
                }
                break;
            }
            case EnvelopeState.Idle:
            {
                envelopeValue = 0.0;
                break;
            }
        }

        if (autoReleasePending && envelopeState == EnvelopeState.Sustain)
        {
            autoReleasePending = false;
            Volatile.Write(ref gateRequested, false);
        }

        return envelopeValue;
    }

    public double CurrentFrequency => Interlocked.CompareExchange(ref resolvedFrequency, 0.0, 0.0);

    public void NoteOn()
    {
        Interlocked.Increment(ref retriggerPending);
        Volatile.Write(ref gateRequested, true);
    }

    public void NoteOff()
    {
        Volatile.Write(ref gateRequested, false);
    }

    public void TriggerOneShot()
    {
        if (!isActiveAndEnabled)
            return;
        NoteOn();
        autoReleasePending = true;
    }

    void OnDisable()
    {
        Volatile.Write(ref gateRequested, false);
        gateActive = false;
        autoReleasePending = false;
        retriggerPending = 0;
        envelopeValue = 0.0;
        envelopeState = EnvelopeState.Idle;
        z1 = z2 = 0.0;
    }

    float GenerateSample(double currentPhase)
    {
        switch (waveform)
        {
            case Waveform.Saw:
            {
                float normalized = (float)(currentPhase / tau); // 0..1
                return (normalized * 2f) - 1f;
            }
            case Waveform.Triangle:
            {
                float normalized = (float)(currentPhase / tau); // 0..1
                return 1f - 4f * Mathf.Abs(normalized - 0.5f);
            }
            default:
                return Mathf.Sin((float)currentPhase);
        }
    }

    void UpdateFilterCoefficients(double currentSampleRate, float cutoffHz, float resonanceQ)
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

    double ProcessFilter(double input)
    {
        double output = b0 * input + z1;
        z1 = b1 * input + z2 - a1 * output;
        z2 = b2 * input - a2 * output;
        return output;
    }

    public void SetNoteName(string noteName)
    {
        note = noteName;
        useNoteInput = true;
        RefreshFrequencyCache(true);
    }

    public void SetFrequency(float hz)
    {
        frequency = hz;
        useNoteInput = false;
        RefreshFrequencyCache(true);
    }

    // --- ISoundGenerator Interface Implementation ---

    /// <summary>
    /// ISoundGenerator: Sets pitch by frequency in Hz.
    /// </summary>
    public void SetPitch(float frequencyHz)
    {
        SetFrequency(frequencyHz);
    }

    /// <summary>
    /// ISoundGenerator: Sets pitch using scientific pitch notation.
    /// </summary>
    public void SetPitchByName(string noteName)
    {
        SetNoteName(noteName);
    }

    /// <summary>
    /// ISoundGenerator: Sets pitch offset in semitones from current frequency.
    /// </summary>
    public void SetPitchOffset(float semitones)
    {
        // Calculate new frequency from current frequency + semitone offset
        // Formula: newFreq = currentFreq * 2^(semitones/12)
        float currentFreq = useNoteInput ? (float)resolvedFrequency : frequency;
        float newFreq = currentFreq * Mathf.Pow(2f, semitones / 12f);
        SetFrequency(newFreq);
    }

    /// <summary>
    /// ISoundGenerator: Sets velocity/amplitude (MIDI 0-127).
    /// </summary>
    public void SetVelocity(int velocity)
    {
        // Convert MIDI velocity (0-127) to amplitude (0-1)
        // Using a curve: velocity^2 / 127^2 for more musical response
        float normalized = velocity / 127f;
        amplitude = normalized * normalized; // Square for exponential feel
    }

    // --- End ISoundGenerator Interface ---

    void RefreshFrequencyCache(bool force = false)
    {
        if (useNoteInput)
        {
            if (force || !cachedUseNoteInput || !string.Equals(cachedNoteInput, note, StringComparison.Ordinal))
            {
                if (TryParseNote(note, out double freq))
                {
                    Interlocked.Exchange(ref resolvedFrequency, freq);
                }
                else
                {
                    Interlocked.Exchange(ref resolvedFrequency, Mathf.Max(20f, frequency));
                }
                cachedNoteInput = note;
                cachedUseNoteInput = true;
            }
        }
        else
        {
            if (force || cachedUseNoteInput || !Mathf.Approximately(cachedFrequencyValue, frequency))
            {
                Interlocked.Exchange(ref resolvedFrequency, Mathf.Max(20f, frequency));
                cachedFrequencyValue = frequency;
                cachedUseNoteInput = false;
            }
        }
    }

    static bool TryParseNote(string input, out double frequencyOut)
    {
        frequencyOut = 0.0;
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
        frequencyOut = 440.0 * Math.Pow(2.0, exponent);
        return frequencyOut > 0.0;
    }
}