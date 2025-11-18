/// <summary>
/// Defines what type of output the step sequencer produces.
/// Used to hide irrelevant fields in the inspector.
/// </summary>
public enum SequencerMode
{
    /// <summary>
    /// Sequence triggers audio only (no UnityEvents).
    /// Hides: onStepStart, onStepEnd events
    /// </summary>
    AudioOnly,

    /// <summary>
    /// Sequence fires UnityEvents only (no audio).
    /// Hides: soundGenerator, pitch, velocity fields
    /// </summary>
    EventsOnly,

    /// <summary>
    /// Sequence triggers both audio and UnityEvents.
    /// Shows: all fields
    /// </summary>
    AudioAndEvents
}
