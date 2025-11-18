using Sirenix.OdinInspector;
using System;
using UnityEngine;

/// <summary>
/// Kick drum synthesizer with pitch envelope and punch control.
/// Generates classic analog-style kick drum sounds.
/// </summary>
[AddComponentMenu("Audio/Drums/Kick Drum")]
public class KickDrum : BaseDrumSynth
{
    [BoxGroup("Simple Controls")]
    [Range(0f, 1f)]
    [Tooltip("Controls the punch/impact of the kick. Higher = harder attack")]
    public float punch = 0.5f;

    [BoxGroup("Simple Controls")]
    [Range(30f, 150f)]
    [Tooltip("Base frequency of the kick drum in Hz")]
    public float tone = 60f;

    [BoxGroup("Simple Controls")]
    [Range(0.05f, 1f)]
    [Tooltip("How long the kick sustains")]
    public float sustain = 0.3f;

    [BoxGroup("Advanced", false)]
    [Range(50f, 500f)]
    [Tooltip("Starting pitch of the pitch envelope in Hz")]
    public float pitchStart = 200f;

    [BoxGroup("Advanced", false)]
    [Range(0.1f, 5f)]
    [Tooltip("How fast the pitch envelope decays (lower = slower sweep)")]
    public float pitchDecay = 0.15f;

    [BoxGroup("Advanced", false)]
    [Range(0f, 1f)]
    [Tooltip("Mix between pure sine (0) and harmonically rich (1)")]
    public float harmonicMix = 0.1f;

    [BoxGroup("Advanced", false)]
    [Range(0f, 1f)]
    [Tooltip("Amount of click/beater sound at the start")]
    public float clickAmount = 0.3f;

    // Synthesis state
    private double phase;
    private double clickPhase;
    private System.Random noiseGen;

    protected override void OnDrumAwake()
    {
        noiseGen = new System.Random();
    }

    protected override void OnTrigger()
    {
        // Reset oscillator phases
        phase = 0.0;
        clickPhase = 0.0;

        // Apply simple controls to envelope (can be overridden in Advanced section)
        attack = Mathf.Lerp(0.005f, 0.001f, punch);
        decay = sustain;
    }

    protected override double GenerateDrumSample()
    {
        double sample = 0.0;

        // Calculate pitch envelope
        double pitchEnv = Math.Exp(-EnvelopePhase / Math.Max(0.01, pitchDecay));
        double currentFreq = tone + (pitchStart - tone) * pitchEnv;

        // Generate main tone (sine wave with optional harmonics)
        double sineWave = Math.Sin(phase);

        // Add subtle harmonics for more punch if requested
        double harmonicWave = sineWave;
        if (harmonicMix > 0.001)
        {
            harmonicWave += 0.3 * Math.Sin(phase * 2.0); // 2nd harmonic
            harmonicWave += 0.15 * Math.Sin(phase * 3.0); // 3rd harmonic
            harmonicWave *= 0.7; // Normalize
        }

        sample = sineWave * (1.0 - harmonicMix) + harmonicWave * harmonicMix;

        // Add click/beater transient at the beginning
        if (clickAmount > 0.001 && EnvelopePhase < 0.01)
        {
            double clickEnv = Math.Exp(-EnvelopePhase / 0.005);
            double clickFreq = 3000.0 * clickEnv; // High frequency that decays quickly
            double clickSample = Math.Sin(clickPhase) * clickEnv * clickAmount;
            sample += clickSample * 0.5;

            // Advance click phase
            clickPhase += (clickFreq * tau) / sampleRate;
        }

        // Advance main oscillator phase
        phase += (currentFreq * tau) / sampleRate;

        // Keep phase wrapped to prevent overflow
        if (phase > tau)
            phase -= tau;

        return sample;
    }

    protected override double ModifyEnvelope(double envelopeValue, double phase)
    {
        // Apply punch curve to envelope
        // Higher punch = sharper, more exponential decay
        if (punch > 0.01)
        {
            double punchCurve = Math.Pow(envelopeValue, 1.0 + punch * 2.0);
            return punchCurve;
        }

        return envelopeValue;
    }

    #if UNITY_EDITOR
    [BoxGroup("Simple Controls")]
    [Button("Preview Kick")]
    private void PreviewKick()
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
