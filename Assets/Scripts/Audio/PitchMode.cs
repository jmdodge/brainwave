/// <summary>
/// Defines how pitch is specified for a sound generator.
/// </summary>
public enum PitchMode
{
    /// <summary>
    /// Pitch specified using scientific pitch notation (e.g., "C4", "A#3").
    /// Best for melodic/harmonic content.
    /// </summary>
    NoteName,

    /// <summary>
    /// Pitch specified as frequency in Hz (e.g., 440.0).
    /// Best for sound design and non-musical frequencies.
    /// </summary>
    Frequency,

    /// <summary>
    /// Pitch specified as semitone offset from base pitch (e.g., +12 = octave up).
    /// Best for sample playback and drum tuning.
    /// </summary>
    Semitones
}
