using System;
using System.Threading;
using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public class SineWaveGenerator : MonoBehaviour
{
    public enum Waveform
    {
        Sine,
        Saw,
        Triangle
    }

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

    volatile bool gateRequested;
    volatile bool autoReleasePending;
    bool gateActive;

    double envelopeValue;
    EnvelopeState envelopeState = EnvelopeState.Attack;

    void Awake()
    {
        sampleRate = AudioSettings.outputSampleRate;
        gateRequested = false;
        gateActive = false;
        autoReleasePending = false;
        envelopeValue = 0.0;
        envelopeState = EnvelopeState.Idle;
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

        double increment = frequency * tau / sampleRate;

        for (int i = 0; i < data.Length; i += channels)
        {
            double env = UpdateEnvelope();
            double rawSample = amplitude * env * GenerateSample(phase);
            double filteredSample = ProcessFilter(rawSample);
            float sampleValue = (float)filteredSample;
            for (int ch = 0; ch < channels; ch++)
                data[i + ch] = sampleValue;

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

    public void NoteOn()
    {
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
}