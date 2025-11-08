using System;
using DG.Tweening;
using ScriptableObjectArchitecture;
using UnityEngine;
using UnityEngine.UI;

public class UIBarOrig : MonoBehaviour {
    /// Config ///
    [SerializeField] private Image healthBarImage;
    [SerializeField] private float healthChangeTransitionTime = .2f;
    [SerializeField] private FloatReference currentValueRef;
    [SerializeField] private FloatReference maxValueRef;
    [SerializeField] private bool subscribeToCurrentValueDirectly;

    /// State ///
    private Tween _myTween;
    private Action myAction;
    
    private void OnEnable() {
        // Initialize our bar value when we turn on.
        UpdateBarUI();

        // Add a listener directly to our variable reference. We could have done this with an event listener on the component too.
        if (subscribeToCurrentValueDirectly) {
            currentValueRef.AddListener(UpdateBarUI);
        }
        
    }

    private void OnDisable() {
        // Remove our listener when disabled, if applied.
        if (subscribeToCurrentValueDirectly) {
            currentValueRef.RemoveListener(UpdateBarUI);
        }
    }

    public void UpdateBarUI() {
        // Kill an existing tween if we have one and then begin the change animation
        _myTween.Kill();
        _myTween = healthBarImage.DOFillAmount(currentValueRef.Value / maxValueRef.Value, healthChangeTransitionTime);
    }
}