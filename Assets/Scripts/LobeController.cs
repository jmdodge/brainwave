using UnityAtoms.BaseAtoms;
using UnityEngine;

public class LobeController : MonoBehaviour
{
    [SerializeField] public FloatReference brainMeterIncreaseValue;
    [SerializeField] public FloatReference brainMeterDecreaseValue;
    [SerializeField] private float cooldownSeconds = 2f;

    private float _lastTriggerTime = float.NegativeInfinity;

    void OnTriggerEnter2D(Collider2D col)
    {
        if (Time.time - _lastTriggerTime < cooldownSeconds) return;

        var ring = col.GetComponent<RingController>();
        if (ring == null) return;
        if (brainMeterIncreaseValue == null || brainMeterDecreaseValue == null) return;
        _lastTriggerTime = Time.time;

        ActivateBrainwave(ring.Power);
    }

    void ActivateBrainwave(float power)
    {
        brainMeterIncreaseValue.Value += power;
        brainMeterDecreaseValue.Value -= power;
    }
}