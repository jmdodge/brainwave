using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

public class BrainwaveController : MonoBehaviour
{
    [SerializeField] private List<GameObject> rings = new List<GameObject>();
    [SerializeField] private float ringAnimationDuration = 0.4f;
    [SerializeField] private float ringShakeDuration = 0.8f;
    [SerializeField] private float ringShakeStrength = 0.1f;
    [SerializeField] private float controllerCollapseDuration = 0.3f;
    [SerializeField] private UnityEvent onAnimationComplete;
    private readonly List<Vector3> ringInitialScales = new List<Vector3>();
    private Sequence ringSequence;
    private Vector3 controllerInitialScale;
    private bool isClicking = false;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        CacheInitialScales();
        transform.localScale = Vector3.zero;
    }


    public void OnClickEvent(Vector3 clickedPosition)
    {
        if (isClicking) return;
        isClicking = true;
        transform.localScale = controllerInitialScale;

        // Move our object to the clicked position, but get z from our current position
        transform.position = new Vector3(clickedPosition.x, clickedPosition.y, transform.position.z);

        if (ringSequence != null && ringSequence.IsActive()) ringSequence.Kill();

        if (ringInitialScales.Count != rings.Count) CacheInitialScales();

        ringSequence = DOTween.Sequence();
        float ringTotalDuration = ringAnimationDuration; //+ ringShakeDuration;
        float overlapOffset = ringTotalDuration * 0.5f;
        float currentStartTime = 0f;

        for (int i = 0; i < rings.Count; i++)
        {
            GameObject ring = rings[i];
            if (ring == null) continue;

            if (i >= ringInitialScales.Count)
            {
                ringInitialScales.Add(ring.transform.localScale);
                ring.SetActive(false);
            }

            Vector3 initialScale = ringInitialScales[i];

            ring.transform.localScale = Vector3.zero;
            Tween scaleTween = ring.transform.DOScale(initialScale, ringAnimationDuration)
                .SetEase(Ease.OutBack)
                .OnStart(() => ring.SetActive(true));
            ringSequence.Insert(currentStartTime, scaleTween);

            Tween shakeTween = ring.transform.DOShakeScale(ringShakeDuration, ringShakeStrength);
            ringSequence.Insert(currentStartTime + ringAnimationDuration, shakeTween);

            currentStartTime += overlapOffset;
        }

        // Scale down our main parent and trigger our event, which plays a sound at the moment.
        Tween scaleParentTween = transform.DOScale(Vector3.zero, controllerCollapseDuration)
            .SetEase(Ease.InBack)
            .OnStart(() => onAnimationComplete.Invoke());
        ringSequence.Append(scaleParentTween);

        ringSequence.OnComplete(() =>
        {
            foreach (GameObject ring in rings)
            {
                if (ring != null)
                {
                    ring.SetActive(false);
                }
            }

            isClicking = false;
        });
        ringSequence.Play();
    }

    private void CacheInitialScales()
    {
        controllerInitialScale = transform.localScale;
        ringInitialScales.Clear();
        foreach (GameObject ring in rings)
        {
            if (ring != null)
            {
                ringInitialScales.Add(ring.transform.localScale);
                ring.SetActive(false);
            }
            else
            {
                ringInitialScales.Add(Vector3.one);
            }
        }
    }
}