using System;
using DG.Tweening;
using UnityAtoms.BaseAtoms;
using UnityEngine;
using UnityEngine.UI;

public class UIBar : MonoBehaviour
{
    /// Config ///
    [SerializeField] private Image healthBarImage;

    [SerializeField] private float healthChangeTransitionTime = .2f;
    [SerializeField] private FloatReference currentValueRef;
    [SerializeField] private FloatReference maxValueRef;
    [SerializeField] private float shakeStrength = 5f;
    [SerializeField] private float shakeDuration = 0.3f;
    [SerializeField] private int shakeVibrato = 50;

    /// State ///
    private Tween _myTween;

    private Action myAction;

    private void OnEnable()
    {
        currentValueRef.GetEvent<FloatEvent>()?.Register(UpdateBarUI);
        
        // Make sure the image of our child bar is the same dimensions as the parent bar
        if (healthBarImage != null)
        {
            healthBarImage.rectTransform.sizeDelta = GetComponent<RectTransform>().sizeDelta;
        }

        // Initialize our bar value when we turn on.
        UpdateBarUI();
    }

    private void OnDisable()
    {
        currentValueRef.GetEvent<FloatEvent>()?.Unregister(UpdateBarUI);
    }

    public void UpdateBarUI()
    {
        // Kill an existing tween if we have one and then begin the change animation
        _myTween?.Kill();

        if (healthBarImage != null)
        {
            Debug.Log("UpdateBarUI called with value:" + currentValueRef.Value);

            float normalizedValue = 0f;
            if(maxValueRef > 0f) normalizedValue = currentValueRef.Value / maxValueRef.Value;

            Sequence sequence = DOTween.Sequence();
            sequence.Append(healthBarImage.DOFillAmount(normalizedValue, healthChangeTransitionTime));
            sequence.Append(healthBarImage.rectTransform.DOShakeAnchorPos(shakeDuration, shakeStrength, shakeVibrato));
            sequence.SetTarget(healthBarImage);

            _myTween = sequence;
        }
        else
        {
            Debug.LogWarning("UIBar: healthBarImage is null. Please assign it in the inspector.");
        }
    }
}