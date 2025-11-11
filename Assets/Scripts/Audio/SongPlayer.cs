using UnityEngine;

/**
 * Handles music playback and synchronises the global tempo transport with the AudioSource start time.
 *
 * Example:
 * var player = GetComponent<SongPlayer>();
 * player.PlaySong();
 */
[RequireComponent(typeof(AudioSource))]
public sealed class SongPlayer : MonoBehaviour
{
    [SerializeField] TempoManager tempoManager;
    [SerializeField] AudioClip musicClip;
    [SerializeField] float bpm = 100f;
    [SerializeField] double leadInSeconds = 0.1;
    [SerializeField] bool playOnStart = true;

    AudioSource audioSource;
    double scheduledStartDspTime;
    bool songQueued;

    /**
     * Caches the AudioSource reference for later scheduling calls.
     */
    void Awake()
    {
        audioSource = GetComponent<AudioSource>();
    }

    /**
     * Automatically begins playback if playOnStart is true.
     */
    void Start()
    {
        if (!playOnStart) return;
        PlaySong();
    }

    /**
     * Starts the assigned clip with a short lead-in and aligns the tempo manager to the scheduled start time.
     *
     * Example:
     * songPlayer.PlaySong();
     */
    public void PlaySong()
    {
        if (musicClip == null)
        {
            Debug.LogWarning("SongPlayer cannot play because no clip is assigned.", this);
            return;
        }

        if (tempoManager == null) tempoManager = FindAnyObjectByType<TempoManager>();

        double dspNow = AudioSettings.dspTime;
        scheduledStartDspTime = dspNow + Mathf.Max(0.0f, (float)leadInSeconds);

        audioSource.clip = musicClip;
        audioSource.PlayScheduled(scheduledStartDspTime);
        songQueued = true;

        if (tempoManager == null)
        {
            Debug.LogWarning("SongPlayer could not find a TempoManager. Beat-driven systems will not run.", this);
            return;
        }

        tempoManager.SetTempo(bpm, scheduledStartDspTime);
        tempoManager.StartTransport(scheduledStartDspTime);
    }

    /**
     * Stops playback and halts the tempo transport.
     *
     * Example:
     * songPlayer.StopSong();
     */
    public void StopSong()
    {
        if (songQueued)
        {
            audioSource.Stop();
            songQueued = false;
        }

        if (tempoManager != null) tempoManager.StopTransport();
    }

    /**
     * Ensures the tempo transport stops if the component becomes disabled while a song is queued.
     */
    void OnDisable()
    {
        if (!songQueued) return;
        if (tempoManager != null) tempoManager.StopTransport();
        songQueued = false;
    }
}

