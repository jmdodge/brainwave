using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// Hi-hat synthesizer using filtered noise with metallic character.
/// Supports both closed and open hi-hat sounds.
/// </summary>
[AddComponentMenu("Audio/Drums/Hi-Hat")]
public class HiHat : BaseDrumSynth
{
    [BoxGroup("Simple Controls")]
    [Range(0f, 1f)]
    [Tooltip("Brightness/tone of the hi-hat. Higher = brighter")]
    public float brightness = 0.6f;

    [BoxGroup("Simple Controls")]
    [Range(0f, 1f)]
    [Tooltip("How closed/tight the hi-hat is. 0 = open, 1 = closed")]
    public float tightness = 0.8f;

    [BoxGroup("Simple Controls")]
    [Range(0.02f, 0.5f)]
    [Tooltip("How long the hi-hat rings (also affected by tightness)")]
    public float sustain = 0.1f;

    [BoxGroup("Advanced", false)]
    [Range(3000f, 12000f)]
    [Tooltip("Base frequency for metallic filtering")]
    public float metallicFreq = 7000f;

    [BoxGroup("Advanced", false)]
    [Range(0.1f, 5f)]
    [Tooltip("Resonance/Q of the metallic filters")]
    public float metallicResonance = 2.0f;

    [BoxGroup("Advanced", false)]
    [Range(0f, 1f)]
    [Tooltip("Amount of secondary metallic band for richer sound")]
    public float secondaryBandAmount = 0.4f;

    [BoxGroup("Advanced", false)]
    [Range(1.1f, 2.5f)]
    [Tooltip("Frequency ratio for secondary metallic band")]
    public float secondaryBandRatio = 1.73f; // ~sqrt(3) for inharmonic relationship

    // Synthesis state
    private System.Random noiseGen;

    // Band-pass filter 1 state
    private double bp1Z1, bp1Z2;

    // Band-pass filter 2 state
    private double bp2Z1, bp2Z2;

    // High-pass filter state (for removing low frequencies)
    private double hpZ1, hpZ2;

    protected override void OnDrumAwake()
    {
        noiseGen = new System.Random();
        attack = 0.0001f; // Very fast attack for hi-hats
    }

    protected override void OnTrigger()
    {
        // Reset filter states
        bp1Z1 = bp1Z2 = 0.0;
        bp2Z1 = bp2Z2 = 0.0;
        hpZ1 = hpZ2 = 0.0;

        // Apply simple controls to envelope (can be overridden in Advanced section)
        // Tightness modulates the sustain (closed = shorter)
        attack = 0.0001f;
        decay = sustain * Mathf.Lerp(1.5f, 0.5f, tightness);
    }

    protected override double GenerateDrumSample()
    {
        // Generate white noise
        double noise = (noiseGen.NextDouble() * 2.0) - 1.0;

        // Apply high-pass filter to remove low frequencies
        double hpCutoff = Mathf.Lerp(6000f, 9000f, tightness);
        noise = ApplyHighPass(noise, hpCutoff);

        // Calculate metallic filter frequencies based on brightness
        double freq1 = metallicFreq * Mathf.Lerp(0.7f, 1.3f, brightness);
        double freq2 = freq1 * secondaryBandRatio;

        // Apply primary band-pass filter for main metallic character
        double metallic1 = ApplyBandPass1(noise, freq1, metallicResonance);

        // Apply secondary band-pass filter for richer harmonic content
        double metallic2 = ApplyBandPass2(noise, freq2, metallicResonance * 0.8);

        // Mix the two metallic bands
        double metallicMix = metallic1 + (metallic2 * secondaryBandAmount);

        // Scale output
        return metallicMix * 0.5;
    }

    protected override double ModifyEnvelope(double envelopeValue, double phase)
    {
        // Hi-hats have very fast attack and exponential decay
        // Closed hi-hats decay faster
        double curve = Mathf.Lerp(1.2f, 2.0f, tightness);
        return Math.Pow(envelopeValue, curve);
    }

    /// <summary>
    /// Applies a high-pass filter to remove low frequencies.
    /// </summary>
    private double ApplyHighPass(double input, double cutoffHz)
    {
        double omega = 2.0 * Math.PI * cutoffHz / sampleRate;
        double cosOmega = Math.Cos(omega);
        double sinOmega = Math.Sin(omega);
        double alpha = sinOmega / (2.0 * 0.707);

        double b0 = (1.0 + cosOmega) * 0.5;
        double b1 = -(1.0 + cosOmega);
        double b2 = b0;
        double a1 = -2.0 * cosOmega;
        double a2 = 1.0 - alpha;

        double invA0 = 1.0 / (1.0 + alpha);
        b0 *= invA0;
        b1 *= invA0;
        b2 *= invA0;
        a1 *= invA0;
        a2 *= invA0;

        double output = b0 * input + hpZ1;
        hpZ1 = b1 * input + hpZ2 - a1 * output;
        hpZ2 = b2 * input - a2 * output;

        return output;
    }

    /// <summary>
    /// Applies the first band-pass filter for metallic character.
    /// </summary>
    private double ApplyBandPass1(double input, double centerHz, double Q)
    {
        double omega = 2.0 * Math.PI * centerHz / sampleRate;
        double sinOmega = Math.Sin(omega);
        double cosOmega = Math.Cos(omega);
        double alpha = sinOmega / (2.0 * Q);

        double invA0 = 1.0 / (1.0 + alpha);
        double b0 = alpha * invA0;
        double b1 = 0.0;
        double b2 = -alpha * invA0;
        double a1 = -2.0 * cosOmega * invA0;
        double a2 = (1.0 - alpha) * invA0;

        double output = b0 * input + bp1Z1;
        bp1Z1 = b1 * input + bp1Z2 - a1 * output;
        bp1Z2 = b2 * input - a2 * output;

        return output;
    }

    /// <summary>
    /// Applies the second band-pass filter for additional harmonic content.
    /// </summary>
    private double ApplyBandPass2(double input, double centerHz, double Q)
    {
        double omega = 2.0 * Math.PI * centerHz / sampleRate;
        double sinOmega = Math.Sin(omega);
        double cosOmega = Math.Cos(omega);
        double alpha = sinOmega / (2.0 * Q);

        double invA0 = 1.0 / (1.0 + alpha);
        double b0 = alpha * invA0;
        double b1 = 0.0;
        double b2 = -alpha * invA0;
        double a1 = -2.0 * cosOmega * invA0;
        double a2 = (1.0 - alpha) * invA0;

        double output = b0 * input + bp2Z1;
        bp2Z1 = b1 * input + bp2Z2 - a1 * output;
        bp2Z2 = b2 * input - a2 * output;

        return output;
    }

    #if UNITY_EDITOR
    [BoxGroup("Simple Controls")]
    [Button("Preview Hi-Hat")]
    private void PreviewHiHat()
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
