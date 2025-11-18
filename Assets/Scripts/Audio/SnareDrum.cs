using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// Snare drum synthesizer with tone/noise mixing and snap control.
/// Combines a tonal body with noise-based snare character.
/// </summary>
[AddComponentMenu("Audio/Drums/Snare Drum")]
public class SnareDrum : BaseDrumSynth
{
    [BoxGroup("Simple Controls")]
    [Range(0f, 1f)]
    [Tooltip("Controls the snap/crack of the snare. Higher = sharper attack")]
    public float snap = 0.6f;

    [BoxGroup("Simple Controls")]
    [Range(100f, 400f)]
    [Tooltip("Fundamental frequency of the drum body in Hz")]
    public float tone = 200f;

    [BoxGroup("Simple Controls")]
    [Range(0f, 1f)]
    [Tooltip("Balance between tonal body (0) and snare noise (1)")]
    public float snareAmount = 0.5f;

    [BoxGroup("Simple Controls")]
    [Range(0.05f, 0.5f)]
    [Tooltip("How long the snare sustains")]
    public float sustain = 0.15f;

    [BoxGroup("Advanced", false)]
    [Range(0f, 1f)]
    [Tooltip("Mix of noise in the initial transient")]
    public float noiseTransient = 0.7f;

    [BoxGroup("Advanced", false)]
    [Range(2000f, 10000f)]
    [Tooltip("Center frequency for snare noise filtering")]
    public float snareFilterFreq = 5000f;

    [BoxGroup("Advanced", false)]
    [Range(0.1f, 2f)]
    [Tooltip("Decay time for the snare rattle (independent of main decay)")]
    public float snareDecay = 0.15f;

    [BoxGroup("Advanced", false)]
    [Range(0f, 1f)]
    [Tooltip("Amount of pitch bend on the drum body")]
    public float bodyPitchBend = 0.3f;

    // Synthesis state
    private double bodyPhase;
    private double noiseHPZ1, noiseHPZ2; // High-pass filter state for noise
    private double noiseBPZ1, noiseBPZ2; // Band-pass filter state for snare
    private System.Random noiseGen;

    protected override void OnDrumAwake()
    {
        noiseGen = new System.Random();
    }

    protected override void OnTrigger()
    {
        // Reset oscillator phases and filter state
        bodyPhase = 0.0;
        noiseHPZ1 = noiseHPZ2 = 0.0;
        noiseBPZ1 = noiseBPZ2 = 0.0;

        // Apply simple controls to envelope (can be overridden in Advanced section)
        attack = Mathf.Lerp(0.005f, 0.0005f, snap);
        decay = sustain;
    }

    protected override double GenerateDrumSample()
    {
        double sample = 0.0;

        // Generate tonal body component
        double bodyFreq = tone;
        if (bodyPitchBend > 0.001)
        {
            // Slight pitch bend at the start for more realistic sound
            double pitchEnv = Math.Exp(-EnvelopePhase / 0.02);
            bodyFreq *= 1.0 + (bodyPitchBend * 0.2 * pitchEnv);
        }

        // Use triangle wave for drum body (more realistic than sine)
        double normalizedPhase = bodyPhase / tau;
        double bodyWave = 1.0 - 4.0 * Math.Abs(normalizedPhase - 0.5);
        double bodyComponent = bodyWave * (1.0 - snareAmount);

        // Generate white noise
        double noise = (noiseGen.NextDouble() * 2.0) - 1.0;

        // High-pass filter the noise to remove low frequencies
        double hpCutoff = 1000.0;
        double hpOmega = 2.0 * Math.PI * hpCutoff / sampleRate;
        double hpAlpha = Math.Sin(hpOmega) / (2.0 * 0.707);
        double hpB0 = (1.0 + Math.Cos(hpOmega)) * 0.5;
        double hpB1 = -(1.0 + Math.Cos(hpOmega));
        double hpB2 = hpB0;
        double hpA1 = -2.0 * Math.Cos(hpOmega);
        double hpA2 = 1.0 - hpAlpha;

        double hpOut = hpB0 * noise + noiseHPZ1;
        noiseHPZ1 = hpB1 * noise + noiseHPZ2 - hpA1 * hpOut;
        noiseHPZ2 = hpB2 * noise - hpA2 * hpOut;

        // Band-pass filter for snare character
        double bpOmega = 2.0 * Math.PI * snareFilterFreq / sampleRate;
        double bpQ = 2.0;
        double bpAlpha = Math.Sin(bpOmega) / (2.0 * bpQ);
        double bpB0 = bpAlpha;
        double bpB1 = 0.0;
        double bpB2 = -bpAlpha;
        double bpA1 = -2.0 * Math.Cos(bpOmega);
        double bpA2 = 1.0 - bpAlpha;

        double snareNoise = bpB0 * hpOut + noiseBPZ1;
        noiseBPZ1 = bpB1 * hpOut + noiseBPZ2 - bpA1 * snareNoise;
        noiseBPZ2 = bpB2 * hpOut - bpA2 * snareNoise;

        // Apply different envelope to snare noise
        double snareEnv = Math.Exp(-EnvelopePhase / Math.Max(0.01, snareDecay));
        double snareComponent = snareNoise * snareEnv * snareAmount;

        // Add transient noise at the very start
        double transient = 0.0;
        if (noiseTransient > 0.001 && EnvelopePhase < 0.01)
        {
            double transientEnv = Math.Exp(-EnvelopePhase / 0.002);
            transient = hpOut * transientEnv * noiseTransient * 0.5;
        }

        // Combine components
        sample = bodyComponent + snareComponent + transient;

        // Advance body oscillator phase
        bodyPhase += (bodyFreq * tau) / sampleRate;
        if (bodyPhase > tau)
            bodyPhase -= tau;

        return sample * 0.7; // Scale down to prevent clipping
    }

    protected override double ModifyEnvelope(double envelopeValue, double phase)
    {
        // Snare has a sharper, more exponential decay
        return Math.Pow(envelopeValue, 1.5);
    }

    #if UNITY_EDITOR
    [BoxGroup("Simple Controls")]
    [Button("Preview Snare")]
    private void PreviewSnare()
    {
        if (!Application.isPlaying)
        {
            Debug.Log("Enter Play mode to preview sounds");
            return;
        }
        TriggerOneShot();
    }
    #endif
}
