using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

public class BrainwaveController : MonoBehaviour
{
    [SerializeField] private List<GameObject> rings = new List<GameObject>();
    [SerializeField] private float ringAnimationDuration = 0.1f;
    private readonly List<Vector3> ringInitialScales = new List<Vector3>();
    private Sequence ringSequence;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        CacheInitialScales();
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void OnClickEvent(Vector3 clickedPosition)
    {
        Debug.Log("BrainwaveController: OnClick called with clickedPosition: " + clickedPosition);

        // Move our object to the clicked position, but get z from our current position
        transform.position = new Vector3(clickedPosition.x, clickedPosition.y, transform.position.z);
        
        if (ringSequence != null && ringSequence.IsActive())
        {
            ringSequence.Kill();
        }

        if (ringInitialScales.Count != rings.Count)
        {
            CacheInitialScales();
        }

        ringSequence = DOTween.Sequence();
        Tween previousShake = null;

        for (int i = 0; i < rings.Count; i++)
        {
            GameObject ring = rings[i];
            if (ring == null) continue;

            if (i >= ringInitialScales.Count)
            {
                ringInitialScales.Add(ring.transform.localScale);
            }

            Vector3 initialScale = ringInitialScales[i];

            ring.transform.localScale = Vector3.zero;
            Tween scaleTween = ring.transform.DOScale(initialScale, ringAnimationDuration).SetEase(Ease.OutBack);
            if (previousShake != null)
            {
                ringSequence.Join(scaleTween);
            }
            else
            {
                ringSequence.Append(scaleTween);
            }

            Tween shakeTween = ring.transform.DOShakeScale(0.2f, 0.1f);
            ringSequence.Append(shakeTween);
            previousShake = shakeTween;
        }

        ringSequence.Play();
    }

    private void CacheInitialScales()
    {
        ringInitialScales.Clear();
        foreach (GameObject ring in rings)
        {
            if (ring != null)
            {
                ringInitialScales.Add(ring.transform.localScale);
            }
            else
            {
                ringInitialScales.Add(Vector3.one);
            }
        }
    }
}
