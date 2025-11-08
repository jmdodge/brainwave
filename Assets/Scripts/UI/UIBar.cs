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
    [SerializeField] private bool subscribeToCurrentValueDirectly;
    [SerializeField] private float shakeStrength = 5f;
    [SerializeField] private float shakeDuration = 0.3f;
    [SerializeField] private int shakeVibrato = 50;

    /// State ///
    private Tween _myTween;

    private Action myAction;

    private void OnEnable()
    {
        // Initialize our bar value when we turn on.
        UpdateBarUI();

        // Add a listener directly to our variable reference. We could have done this with an event listener on the component too.
        if (subscribeToCurrentValueDirectly)
        {
        }
    }

    private void OnDisable()
    {
        // Remove our listener when disabled, if applied.
        if (subscribeToCurrentValueDirectly)
        {
        }
    }

    public void UpdateBarUI()
    {
        // Kill an existing tween if we have one and then begin the change animation
        _myTween?.Kill();

        if (healthBarImage != null)
        {
            Debug.Log("UpdateBarUI called with value:" + currentValueRef.Value);

            float normalizedValue = maxValueRef.Value != 0f
                ? currentValueRef.Value / maxValueRef.Value
                : 0f;

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