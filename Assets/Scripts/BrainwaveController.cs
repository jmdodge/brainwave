using DG.Tweening;
using UnityEngine;

public class BrainwaveController : MonoBehaviour
{
    [SerializeField] private GameObject ringSm;
    [SerializeField] private GameObject ringM;
    [SerializeField] private GameObject ringLg;
    [SerializeField] private float ringAnimationDuration = 0.1f;
    private Vector3 ringSmInitialScale;
    private Vector3 ringMInitialScale;
    private Vector3 ringLgInitialScale;
    private Sequence ringSequence;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        if (ringSm != null)
        {
            ringSmInitialScale = ringSm.transform.localScale;
        }

        if (ringM != null)
        {
            ringMInitialScale = ringM.transform.localScale;
        }

        if (ringLg != null)
        {
            ringLgInitialScale = ringLg.transform.localScale;
        }
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

        ringSequence = DOTween.Sequence();

        if (ringSm != null)
        {
            ringSm.transform.localScale = Vector3.zero;
            ringSequence.Append(ringSm.transform.DOScale(ringSmInitialScale, ringAnimationDuration).SetEase(Ease.OutBack));
            ringSequence.Append(ringSm.transform.DOShakeScale(0.2f, 0.1f));
        }

        if (ringM != null)
        {
            ringM.transform.localScale = Vector3.zero;
            ringSequence.Append(ringM.transform.DOScale(ringMInitialScale, ringAnimationDuration).SetEase(Ease.OutBack));
        }

        if (ringLg != null)
        {
            ringLg.transform.localScale = Vector3.zero;
            ringSequence.Append(ringLg.transform.DOScale(ringLgInitialScale, ringAnimationDuration).SetEase(Ease.OutBack));
        }

        ringSequence.Play();
    }
}
