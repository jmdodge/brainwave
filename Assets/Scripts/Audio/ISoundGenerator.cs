using UnityEngine;

/// <summary>
/// Common interface for all sound-generating components that can be triggered by sequencers.
/// Supports pitched synths, drums, and sample playback with pitch, velocity, and trigger control.
/// </summary>
public interface ISoundGenerator
{
    // --- Pitch Control ---

    /// <summary>
    /// Sets pitch by frequency in Hz.
    /// Used primarily for synthesizers and tonal sound generation.
    /// </summary>
    void SetPitch(float frequencyHz);

    /// <summary>
    /// Sets pitch using scientific pitch notation (e.g., "C4", "A#3", "Gb2").
    /// Used primarily for melodic/harmonic content.
    /// </summary>
    void SetPitchByName(string noteName);

    /// <summary>
    /// Sets pitch offset in semitones from the generator's base pitch.
    /// Used primarily for sample playback and drum tuning.
    /// Range: typically ±48 semitones (±4 octaves)
    /// </summary>
    void SetPitchOffset(float semitones);

    // --- Amplitude Control ---

    /// <summary>
    /// Sets the velocity/amplitude for the next triggered sound.
    /// Uses MIDI convention: 0 (silent) to 127 (maximum).
    /// Default: 100 (forte)
    /// </summary>
    void SetVelocity(int velocity);

    // --- Trigger Control ---

    /// <summary>
    /// Triggers the sound (note on / sample trigger / drum hit).
    /// </summary>
    void NoteOn();

    /// <summary>
    /// Releases/stops the sound (note off).
    /// For one-shot sounds (drums, samples), this may be ignored.
    /// For sustained synths, this stops the note.
    /// </summary>
    void NoteOff();
}
