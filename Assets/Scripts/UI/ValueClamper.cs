using UnityEngine;

public class ValueClamper : MonoBehaviour
{
    [SerializeField] private float minValue = 0f;
    [SerializeField] private float maxValue = 1f;

    public float ClampValue(float value)
    {
        return Mathf.Clamp(value, minValue, maxValue);
    }
}
