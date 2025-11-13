using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Serialization;

public class BrainwaveControllerBeats : MonoBehaviour
{
    [SerializeField] private bool fixedPosition;
    [SerializeField] private List<GameObject> rings = new();

    [SerializeField, Min(0f)]
    private float ringScaleDurationBeats = 0.25f;

    [SerializeField, Min(0f)]
    private float ringShakeDurationBeats = 0.5f;

    [SerializeField] private float ringShakeStrength = 0.1f;

    [SerializeField, Min(0f)]
    private float controllerCollapseDurationBeats = 0.25f;

    [SerializeField]
    private UnityEvent onSequenceComplete;

    [SerializeField] private TempoManager tempoManager;

    private readonly List<Vector3> ringInitialScales = new();
    private readonly List<Tween> activeRingTweens = new();
    private Tween controllerTween;
    private Vector3 controllerInitialScale;
    private int nextRingIndex;
    private bool sequenceActive;

    /**
     * Initializes the controller by caching initial scales and hiding it.
     */
    void Start()
    {
        CacheInitialScales();
        transform.localScale = Vector3.zero;
        nextRingIndex = 0;
        sequenceActive = false;
    }

    void OnEnable()
    {
        CacheInitialScales();
    }

    void OnDisable()
    {
        KillActiveTweens();
    }
    
    public void SetPosition(Vector3 worldPosition)
    {
        if(fixedPosition) return;
        Vector3 alignedPosition = new Vector3(worldPosition.x, worldPosition.y, transform.position.z);
        transform.position = alignedPosition;
    }

    /**
     * Prepares the controller for a new beat-driven sequence at the supplied world position.
     * Use this to align the controller before driving rings via NextRing().
     */
    public void BeginSequence(Vector3 worldPosition)
    {
        SetPosition(worldPosition);
        BeginSequence();
    }

    /**
     * Prepares the controller for a new beat-driven sequence without adjusting position.
     */
    public void BeginSequence()
    {
        KillActiveTweens();
        transform.localScale = controllerInitialScale;
        sequenceActive = true;
        nextRingIndex = 0;

        EnsureRingCaches();
        ResetRingsVisuals();
    }

    /**
     * Triggers the next ring in the list to animate using beat-synced durations.
     * Subsequent calls continue advancing through the list.
     */
    public void NextRing()
    {
        if (rings == null || rings.Count == 0) return;
        EnsureRingCaches();

        if (!sequenceActive)
        {
            BeginSequence();
        }

        while (nextRingIndex < rings.Count && rings[nextRingIndex] == null)
        {
            nextRingIndex++;
        }

        if (nextRingIndex >= rings.Count) return;

        int ringIndex = nextRingIndex;
        GameObject ring = rings[ringIndex];
        nextRingIndex++;

        Vector3 initialScale = ringInitialScales[ringIndex];

        ring.transform.DOKill();
        ring.transform.localScale = Vector3.zero;
        ring.SetActive(true);

        float scaleDuration = Mathf.Max(0f, BeatsToSeconds(ringScaleDurationBeats));
        Tween scaleTween = ring.transform
            .DOScale(initialScale, Mathf.Max(1e-4f, scaleDuration))
            .SetEase(Ease.OutBack);

        Tween shakeTween = null;
        float shakeDuration = Mathf.Max(0f, BeatsToSeconds(ringShakeDurationBeats));
        if (shakeDuration > 1e-4f && ringShakeStrength > 0f)
        {
            shakeTween = ring.transform.DOShakeScale(shakeDuration, ringShakeStrength);
        }

        Sequence ringSequence = DOTween.Sequence();
        ringSequence.Append(scaleTween);
        if (shakeTween != null) ringSequence.Append(shakeTween);

        activeRingTweens.Add(ringSequence);
        ringSequence.OnKill(() => { activeRingTweens.Remove(ringSequence); });
        ringSequence.Play();
    }

    /**
     * Scales the controller back to zero and hides all rings, invoking the completion event once finished.
     */
    public void CompleteSequence()
    {
        if (!sequenceActive) return;

        sequenceActive = false;
        nextRingIndex = 0;

        KillActiveTweens();

        float collapseDuration = Mathf.Max(0f, BeatsToSeconds(controllerCollapseDurationBeats));
        controllerTween = transform
            .DOScale(Vector3.zero, Mathf.Max(1e-4f, collapseDuration))
            .SetEase(Ease.InBack)
            .OnComplete(() =>
            {
                foreach (GameObject ring in rings)
                {
                    if (ring == null) continue;
                    ring.SetActive(false);
                    ring.transform.localScale = Vector3.zero;
                }

                onSequenceComplete?.Invoke();
            });
    }

    /**
     * Caches the initial scales of the controller and all rings for animation reset purposes.
     * Rings are hidden after caching.
     */
    private void CacheInitialScales()
    {
        controllerInitialScale = transform.localScale;
        EnsureRingCaches();
        ResetRingsVisuals();
    }

    private void EnsureRingCaches()
    {
        if (rings == null) return;

        if (ringInitialScales.Count != rings.Count)
        {
            ringInitialScales.Clear();
            if (rings != null)
            {
                foreach (GameObject ring in rings)
                {
                    if (ring != null) ringInitialScales.Add(ring.transform.localScale);
                    else ringInitialScales.Add(Vector3.one);
                }
            }
        }
    }

    private void ResetRingsVisuals()
    {
        if (rings == null) return;

        for (int i = 0; i < rings.Count; i++)
        {
            GameObject ring = rings[i];
            if (ring == null) continue;

            ring.SetActive(false);
            ring.transform.localScale = Vector3.zero;
        }
    }

    private float BeatsToSeconds(float beatFraction)
    {
        if (tempoManager != null && tempoManager.SecondsPerBeat > double.Epsilon)
        {
            return Mathf.Max(0f, beatFraction) * (float)tempoManager.SecondsPerBeat;
        }

        return Mathf.Max(0f, beatFraction);
    }

    private void KillActiveTweens()
    {
        for (int i = activeRingTweens.Count - 1; i >= 0; i--)
        {
            Tween tween = activeRingTweens[i];
            tween?.Kill();
        }

        activeRingTweens.Clear();

        controllerTween?.Kill();
        controllerTween = null;

        if (rings != null)
        {
            foreach (GameObject ring in rings)
            {
                if (ring == null) continue;
                ring.transform.DOKill();
            }
        }

        transform.DOKill();
    }
}