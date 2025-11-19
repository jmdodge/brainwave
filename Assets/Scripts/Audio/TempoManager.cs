using System;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

/**
 * Provides a global beat clock backed by Unity's DSP time so gameplay systems can stay phase-locked to music.
 *
 * Example:
 * var tempoManager = FindAnyObjectByType<TempoManager>();
 * double dspStart = AudioSettings.dspTime + 0.1;
 * tempoManager.SetTempo(120f, dspStart);
 * tempoManager.StartTransport(dspStart);
 * tempoManager.OnBeat += (beatInBar, absoluteBeat) =>
 * {
 *     Debug.Log($"Beat {absoluteBeat} (bar position {beatInBar})");
 * };
 */
public sealed class TempoManager : MonoBehaviour
{
    [TitleGroup("Tempo Settings", order: 0)] [Min(1f)] [OnValueChanged(nameof(OnBpmChanged))] [SerializeField]
    float bpm = 100f;

    [TitleGroup("Tempo Settings")] [Tooltip("Time signature numerator (e.g. 4 in 4/4 time)")] [Min(1)] [SerializeField]
    int beatsPerBar = 4;

    [TitleGroup("Tempo Settings")]
    [Tooltip("Time signature denominator - which note gets the beat (4 = quarter note, 8 = eighth note)")]
    [Min(1)]
    [SerializeField]
    int timeSignatureDenominator = 4;

    [TitleGroup("Editor Controls", order: -1)]
    [InfoBox(
        "Use these buttons to control the transport during play mode. Changes to BPM will preserve the current beat phase.")]
    [HorizontalGroup("Editor Controls/Transport")]
    [Button(ButtonSizes.Medium), GUIColor(0.4f, 1f, 0.4f)]
    [EnableIf("@!transportRunning")]
    void StartTransportButton()
    {
        double dspStart = AudioSettings.dspTime + 0.1;
        // Ensure BPM is set (this will preserve phase, but StartTransport will reset it)
        if (secondsPerBeat <= double.Epsilon) SetTempo(bpm, dspStart);
        StartTransport(dspStart);
    }

    [HorizontalGroup("Editor Controls/Transport")]
    [Button(ButtonSizes.Medium), GUIColor(1f, 0.4f, 0.4f)]
    [EnableIf("@transportRunning")]
    void StopTransportButton() => StopTransport();

    [TitleGroup("Editor Controls")]
    [HorizontalGroup("Editor Controls/Test Scheduling")]
    [Button(ButtonSizes.Small), GUIColor(0.4f, 0.8f, 1f)]
    [EnableIf("@transportRunning")]
    void TestSchedule4Beats() =>
        ScheduleBeatsFromNow(4, () => Debug.Log("Scheduled event fired: 4 beats from now", this));

    [HorizontalGroup("Editor Controls/Test Scheduling")]
    [Button(ButtonSizes.Small), GUIColor(0.4f, 0.8f, 1f)]
    [EnableIf("@transportRunning")]
    void TestSchedule8Beats() =>
        ScheduleBeatsFromNow(8, () => Debug.Log("Scheduled event fired: 8 beats from now", this));

    [TitleGroup("Runtime State", order: 1)]
    [ShowInInspector, ReadOnly, LabelText("Transport Status")]
    [PropertyOrder(10)]
    string TransportStatus => transportRunning ? "Running" : transportArmed ? "Armed" : "Stopped";

    [TitleGroup("Runtime State")]
    [ShowInInspector, ReadOnly, LabelText("Current Beat")]
    [PropertyOrder(11)]
    string CurrentBeatDisplay => transportRunning ? $"{CurrentBeat:F3}" : "N/A";

    [TitleGroup("Runtime State")]
    [ShowInInspector, ReadOnly, LabelText("Beat in Bar")]
    [PropertyOrder(12)]
    string CurrentBeatInBarDisplay => transportRunning ? $"{CurrentBeatInBar}" : "N/A";

    [TitleGroup("Runtime State")]
    [ShowInInspector, ReadOnly, LabelText("Current Bar")]
    [PropertyOrder(13)]
    string CurrentBarDisplay => transportRunning ? $"{CurrentBar}" : "N/A";

    [TitleGroup("Runtime State")]
    [ShowInInspector, ReadOnly, LabelText("Seconds Per Beat")]
    [PropertyOrder(14)]
    string SecondsPerBeatDisplay => $"{secondsPerBeat:F4}";

    [TitleGroup("Runtime State")]
    [ShowInInspector, ReadOnly, LabelText("Scheduled Events")]
    [PropertyOrder(15)]
    int ScheduledEventCount => scheduledEvents.Count;

    [TitleGroup("Runtime State")]
    [ShowInInspector, ReadOnly, LabelText("Registered Events")]
    [PropertyOrder(16)]
    int RegisteredEventCount => registeredEvents.Count;

    double secondsPerBeat;
    double beatZeroDspTime;
    double transportStartDspTime;
    bool transportArmed;
    bool transportRunning;
    int lastDispatchedBeat = -1;

    readonly List<ScheduledEvent> scheduledEvents = new();

    /**
     * Represents a scheduled tempo event so it can be cancelled later.
     */
    public readonly struct TempoEventHandle
    {
        internal TempoEventHandle(Guid id) => Id = id;
        internal Guid Id { get; }
        public bool IsValid => Id != Guid.Empty;
    }

    /**
     * Internal storage for scheduled callbacks.
     */
    sealed class ScheduledEvent
    {
        public Guid Id;
        public double BeatIndex;
        public double DspTime;
        public Action Callback;
    }

    /**
     * Metadata for registered events that can be queried by lookahead systems.
     * This allows visual/gameplay systems to see what's coming up and react in advance.
     *
     * Example: A StepSequencer registers an event at beat 4.0. A visual controller queries
     * upcoming events and sees this event at beat 3.5, giving it 0.5 beats to prepare an animation
     * that completes exactly when the audio plays at beat 4.0.
     */
    public sealed class RegisteredEventInfo
    {
        /// <summary>
        /// The absolute beat time when this event will occur (e.g., 4.0, 8.5).
        /// This is the same beat index used by ScheduleAtBeat().
        /// Example: If an event is scheduled at beat 16.0, BeatTime = 16.0
        /// </summary>
        public double BeatTime { get; internal set; }

        /// <summary>
        /// The object that registered this event (typically the component that scheduled it).
        /// Used for filtering events by source.
        /// Example: A StepSequencer passes 'this' so listeners can filter to only that sequencer's events.
        /// </summary>
        public object Source { get; internal set; }

        /// <summary>
        /// A string identifying the type/category of event.
        /// Listeners can filter by this to only react to specific event types.
        ///
        /// Common event types:
        /// - "sequence_start": Initialize visuals/gameplay before sequence begins
        /// - "sequence_next": Normal step trigger (contains step index in Payload)
        /// - "sequence_end": Cleanup visuals/gameplay after sequence completes
        /// - "note_on", "note_off": MIDI-style note events
        /// - "beat_marker", "bar_marker": Timing markers
        /// - Custom types: Any string your system needs
        /// </summary>
        public string EventType { get; internal set; }

        /// <summary>
        /// Optional additional data about this event.
        /// Example: The step index (int), note pitch (float), or any custom data object.
        /// </summary>
        public object Payload { get; internal set; }
    }

    /// <summary>
    /// List of registered events that systems can query for lookahead.
    /// Events are automatically cleaned up after they pass.
    /// </summary>
    readonly List<RegisteredEventInfo> registeredEvents = new();

    /**
     * Current tempo in beats per minute.
     */
    public float Bpm => bpm;

    /**
     * Number of beats that form one bar (e.g. 4 for a 4/4 measure).
     */
    public int BeatsPerBar => beatsPerBar;

    /**
     * Time signature denominator - which note gets the beat (4 = quarter note, 8 = eighth note).
     */
    public int TimeSignatureDenominator => timeSignatureDenominator;

    /**
     * Length of a beat in seconds with the current BPM.
     */
    public double SecondsPerBeat => secondsPerBeat;

    /**
     * Indicates whether the beat transport has started.
     */
    public bool TransportRunning => transportRunning;

    /**
     * Current fractional beat index since beat zero.
     */
    public double CurrentBeat => GetBeatAtDsp(AudioSettings.dspTime);

    /**
     * Current beat within the bar (1-indexed, e.g. 1, 2, 3, 4 for 4/4 time).
     * Returns 1 if transport is not running.
     */
    public int CurrentBeatInBar
    {
        get
        {
            if (!transportRunning) return 1;
            int absoluteBeat = (int)Math.Floor(CurrentBeat);
            int beatInBar = beatsPerBar > 0 ? Modulo(absoluteBeat, beatsPerBar) : 0;
            return beatInBar + 1; // Convert to 1-indexed
        }
    }

    /**
     * Current bar number (1-indexed).
     * Returns 1 if transport is not running.
     */
    public int CurrentBar
    {
        get
        {
            if (!transportRunning) return 1;
            int absoluteBeat = (int)Math.Floor(CurrentBeat);
            return (absoluteBeat / beatsPerBar) + 1;
        }
    }

    /**
     * Fired once per beat. First argument is the beat within the current bar; second is the absolute beat since transport start.
     */
    public event Action<int, int> OnBeat;

    /**
     * Fired when the transport starts running.
     */
    public event Action OnTransportStarted;

    /**
     * Initialises tempo state when the component loads.
     */
    void Awake()
    {
        InitialiseTempo();
    }

    /**
     * Advances the transport, firing beat events and scheduled callbacks when their DSP times arrive.
     */
    void Update()
    {
        double dspNow = AudioSettings.dspTime;

        if (transportArmed && !transportRunning && dspNow >= transportStartDspTime)
        {
            transportRunning = true;
            lastDispatchedBeat = (int)Math.Floor(GetBeatAtDsp(transportStartDspTime)) - 1;
            OnTransportStarted?.Invoke();
        }

        if (!transportRunning) return;

        DispatchBeats(dspNow);
        DispatchScheduledEvents(dspNow);
        CleanupPastRegisteredEvents(dspNow);
    }

    /**
     * Sets the global tempo while preserving the current beat phase relative to an anchor DSP timestamp.
     *
     * Example:
     * tempoManager.SetTempo(140f, AudioSettings.dspTime);
     */
    public void SetTempo(float newBpm, double anchorDspTime)
    {
        bpm = Mathf.Max(newBpm, 0.0001f);

        double currentBeatAtAnchor = secondsPerBeat > double.Epsilon
            ? (anchorDspTime - beatZeroDspTime) / secondsPerBeat
            : 0d;

        secondsPerBeat = 60.0 / bpm;
        beatZeroDspTime = anchorDspTime - currentBeatAtAnchor * secondsPerBeat;

        RecalculateScheduledEventTimes();
    }

    public void StartTransport()
    {
        double startDspTime = AudioSettings.dspTime + 0.1;
        StartTransport(startDspTime);
    }

    /**
     * Arms the transport so beat dispatch begins exactly when the DSP timeline reaches the supplied timestamp.
     * Resets the beat anchor so transport always starts at beat 0 (bar 1, beat 1).
     *
     * Example:
     * double dspStart = AudioSettings.dspTime + 0.05;
     * tempoManager.StartTransport(dspStart);
     */
    public void StartTransport(double startDspTime)
    {
        // Reset beat zero to the transport start time so we always begin at beat 0
        if (secondsPerBeat <= double.Epsilon)
        {
            secondsPerBeat = bpm > 0f ? 60.0 / bpm : 0d;
        }

        beatZeroDspTime = startDspTime;

        transportStartDspTime = startDspTime;
        transportArmed = true;
        transportRunning = false;
        lastDispatchedBeat = -1; // Start from beat 0
    }

    /**
     * Stops beat dispatch and clears armed transport state.
     *
     * Example:
     * tempoManager.StopTransport();
     */
    public void StopTransport()
    {
        transportArmed = false;
        transportRunning = false;
        lastDispatchedBeat = -1;
    }

    /**
     * Converts a DSP timestamp into a fractional beat index using the current tempo and anchor.
     *
     * Example:
     * double beatIndex = tempoManager.GetBeatAtDsp(AudioSettings.dspTime);
     */
    public double GetBeatAtDsp(double dspTime)
    {
        return secondsPerBeat > double.Epsilon
            ? (dspTime - beatZeroDspTime) / secondsPerBeat
            : 0d;
    }

    /**
     * Converts a beat index into an absolute DSP time.
     *
     * Example:
     * double hitTime = tempoManager.BeatToDspTime(8);
     */
    public double BeatToDspTime(double beatIndex) => beatZeroDspTime + beatIndex * secondsPerBeat;

    /**
     * Schedules a callback relative to the current beat position.
     *
     * Example:
     * tempoManager.ScheduleBeatsFromNow(4, SpawnEnemy);
     */
    public TempoEventHandle ScheduleBeatsFromNow(double beatsAhead, Action callback)
    {
        if (callback == null) return default;

        double referenceBeat = transportRunning
            ? CurrentBeat
            : GetBeatAtDsp(transportArmed ? transportStartDspTime : AudioSettings.dspTime);

        double targetBeat = referenceBeat + Math.Max(0d, beatsAhead);
        return ScheduleAtBeat(targetBeat, callback);
    }

    /**
     * Schedules a callback for an absolute beat index.
     *
     * Example:
     * tempoManager.ScheduleAtBeat(16, TriggerBossPhaseTwo);
     */
    public TempoEventHandle ScheduleAtBeat(double beatIndex, Action callback)
    {
        if (callback == null) return default;
        if (secondsPerBeat <= double.Epsilon)
        {
            Debug.LogWarning("TempoManager cannot schedule callbacks because BPM is zero.", this);
            return default;
        }

        var scheduledEvent = new ScheduledEvent
        {
            Id = Guid.NewGuid(),
            BeatIndex = beatIndex,
            DspTime = BeatToDspTime(beatIndex),
            Callback = callback
        };

        InsertScheduledEvent(scheduledEvent);
        return new TempoEventHandle(scheduledEvent.Id);
    }

    /**
     * Cancels a previously scheduled callback.
     *
     * Example:
     * bool cancelled = tempoManager.CancelScheduledEvent(handle);
     */
    public bool CancelScheduledEvent(TempoEventHandle handle)
    {
        if (!handle.IsValid) return false;

        int index = scheduledEvents.FindIndex(evt => evt.Id == handle.Id);
        if (index < 0) return false;

        scheduledEvents.RemoveAt(index);
        return true;
    }

    /**
     * Registers an event for lookahead querying by visual/gameplay systems.
     * Registered events are automatically cleaned up after they pass.
     *
     * This does NOT schedule a callback - it just makes the event queryable.
     * If you want a callback, use ScheduleAtBeat() in addition to this.
     *
     * <param name="beatTime">The absolute beat time when this event occurs (e.g., 4.0, 16.5)</param>
     * <param name="source">The object registering this event (typically 'this'). Used for filtering.</param>
     * <param name="eventType">A string identifying the event type (e.g., "step_trigger", "note_on")</param>
     * <param name="payload">Optional data about the event (e.g., step index, note pitch, etc.)</param>
     *
     * Example:
     * // In a StepSequencer, when scheduling step 5 at beat 8.0:
     * tempoManager.RegisterScheduledEvent(8.0, this, "step_trigger", 5);
     *
     * // Later, a visual system queries:
     * var upcoming = tempoManager.GetUpcomingEvents(currentBeat, 1.0f);
     * // Returns events between currentBeat and currentBeat + 1.0
     */
    public void RegisterScheduledEvent(double beatTime, object source, string eventType, object payload = null)
    {
        var eventInfo = new RegisteredEventInfo
        {
            BeatTime = beatTime,
            Source = source,
            EventType = eventType,
            Payload = payload
        };

        registeredEvents.Add(eventInfo);
    }

    /**
     * Queries upcoming registered events within a lookahead window.
     * Use this to see what events are coming up so you can prepare/react in advance.
     *
     * <param name="fromBeat">The starting beat time to search from (typically CurrentBeat)</param>
     * <param name="lookaheadBeats">How many beats ahead to search (e.g., 0.5 = half a beat ahead)</param>
     * <param name="sourceFilter">Optional: only return events from this source object (e.g., a specific StepSequencer)</param>
     * <returns>List of events occurring between fromBeat and fromBeat + lookaheadBeats</returns>
     *
     * Example 1 - Query all events in the next 1 beat:
     * double currentBeat = tempoManager.CurrentBeat; // e.g., 3.75
     * var upcoming = tempoManager.GetUpcomingEvents(currentBeat, 1.0);
     * // Returns all events between beat 3.75 and 4.75
     *
     * Example 2 - Query events from a specific sequencer:
     * var upcoming = tempoManager.GetUpcomingEvents(currentBeat, 0.5, myStepSequencer);
     * // Returns only events from myStepSequencer that occur in the next 0.5 beats
     *
     * Example 3 - Prepare animations to finish on-beat:
     * double animationDuration = 0.5; // beats
     * var upcoming = tempoManager.GetUpcomingEvents(currentBeat, animationDuration);
     * foreach (var evt in upcoming) {
     *     // Start animation now so it finishes at evt.BeatTime
     *     double startDelay = evt.BeatTime - animationDuration - currentBeat;
     *     ScheduleAnimation(startDelay);
     * }
     */
    public List<RegisteredEventInfo> GetUpcomingEvents(double fromBeat, double lookaheadBeats, object sourceFilter = null)
    {
        double endBeat = fromBeat + lookaheadBeats;
        var results = new List<RegisteredEventInfo>();

        foreach (var eventInfo in registeredEvents)
        {
            if (eventInfo.BeatTime < fromBeat) continue;
            if (eventInfo.BeatTime > endBeat) continue;
            if (sourceFilter != null && eventInfo.Source != sourceFilter) continue;

            results.Add(eventInfo);
        }

        return results;
    }

    /**
     * Calculates the initial beat duration and anchor.
     */
    void InitialiseTempo()
    {
        secondsPerBeat = bpm > 0f ? 60.0 / bpm : 0d;
        beatZeroDspTime = AudioSettings.dspTime;
    }

    /**
     * Called when BPM is changed in the inspector to update tempo while preserving beat phase.
     */
    void OnBpmChanged()
    {
        if (Application.isPlaying)
        {
            SetTempo(bpm, AudioSettings.dspTime);
        }
    }

    /**
     * Dispatches beat events for every whole beat that elapsed since the last frame.
     */
    void DispatchBeats(double dspNow)
    {
        int currentBeat = (int)Math.Floor(GetBeatAtDsp(dspNow));
        if (currentBeat <= lastDispatchedBeat) return;

        for (int beat = lastDispatchedBeat + 1; beat <= currentBeat; beat++)
        {
            int beatInBar = beatsPerBar > 0 ? Modulo(beat, beatsPerBar) : beat;
            OnBeat?.Invoke(beatInBar, beat);
        }

        lastDispatchedBeat = currentBeat;
    }

    /**
     * Invokes scheduled callbacks that are due at or before the current DSP time.
     */
    void DispatchScheduledEvents(double dspNow)
    {
        if (scheduledEvents.Count == 0) return;

        for (int i = 0; i < scheduledEvents.Count;)
        {
            var scheduledEvent = scheduledEvents[i];
            if (scheduledEvent.DspTime - dspNow > 1e-6) break;

            scheduledEvents.RemoveAt(i);
            scheduledEvent.Callback?.Invoke();
        }
    }

    /**
     * Inserts a scheduled event into the list so it remains sorted by DSP time.
     */
    void InsertScheduledEvent(ScheduledEvent scheduledEvent)
    {
        int insertIndex = scheduledEvents.FindIndex(evt => evt.DspTime > scheduledEvent.DspTime);
        if (insertIndex < 0) scheduledEvents.Add(scheduledEvent);
        else scheduledEvents.Insert(insertIndex, scheduledEvent);
    }

    /**
     * Updates stored DSP timestamps for scheduled events when tempo changes.
     */
    void RecalculateScheduledEventTimes()
    {
        for (int i = 0; i < scheduledEvents.Count; i++)
        {
            var scheduledEvent = scheduledEvents[i];
            scheduledEvent.DspTime = BeatToDspTime(scheduledEvent.BeatIndex);
            scheduledEvents[i] = scheduledEvent;
        }

        scheduledEvents.Sort((a, b) => a.DspTime.CompareTo(b.DspTime));
    }

    /**
     * Removes registered events that have already passed (with a small buffer to avoid premature cleanup).
     * Called automatically in Update() to prevent the list from growing indefinitely.
     *
     * Events are kept for 0.1 beats after they pass to ensure any last-minute queries can still see them.
     */
    void CleanupPastRegisteredEvents(double dspNow)
    {
        if (registeredEvents.Count == 0) return;

        double currentBeat = GetBeatAtDsp(dspNow);
        // Keep a small buffer (0.1 beats) to avoid cleaning up events that are about to fire
        // Example: If currentBeat = 5.0, we remove events before beat 4.9
        double cleanupBeat = currentBeat - 0.1;

        registeredEvents.RemoveAll(evt => evt.BeatTime < cleanupBeat);
    }

    /**
     * Returns a mathematically positive modulo result.
     */
    static int Modulo(int value, int modulus)
    {
        if (modulus == 0) return value;
        int result = value % modulus;
        return result < 0 ? result + modulus : result;
    }
}

