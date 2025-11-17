using UnityEngine;
using UnityEngine.Events;

[CreateAssetMenu(fileName = "FloatVarSO", menuName = "Scriptable Objects/FloatVarSO")]
public class FloatVarSO : ScriptableObject
{
    [Header("Default Value")]
    [SerializeField] private float defaultValue;
    
    [Header("Runtime Value")]
    [SerializeField] private float runtimeValue;
    
    [Header("Events")]
    public UnityEvent<float> onValueChanged;
    
    /// <summary>
    /// Gets or sets the current runtime value. Setting triggers the onValueChanged event.
    /// </summary>
    public float Value
    {
        get => runtimeValue;
        set
        {
            if (!Mathf.Approximately(runtimeValue, value))
            {
                runtimeValue = value;
                onValueChanged?.Invoke(runtimeValue);
            }
        }
    }
    
    /// <summary>
    /// Gets the default value.
    /// </summary>
    public float DefaultValue => defaultValue;
    
    /// <summary>
    /// Resets the runtime value to the default value.
    /// </summary>
    public void ResetToDefault()
    {
        Value = defaultValue;
    }
    
    /// <summary>
    /// Sets the value without triggering the onValueChanged event.
    /// </summary>
    public void SetValueSilent(float value)
    {
        runtimeValue = value;
    }
    
    /// <summary>
    /// Adds to the current value.
    /// </summary>
    public void Add(float amount)
    {
        Value += amount;
    }
    
    /// <summary>
    /// Subtracts from the current value.
    /// </summary>
    public void Subtract(float amount)
    {
        Value -= amount;
    }
    
    /// <summary>
    /// Multiplies the current value.
    /// </summary>
    public void Multiply(float multiplier)
    {
        Value *= multiplier;
    }
    
    /// <summary>
    /// Divides the current value.
    /// </summary>
    public void Divide(float divisor)
    {
        if (!Mathf.Approximately(divisor, 0f))
        {
            Value /= divisor;
        }
        else
        {
            Debug.LogWarning($"Attempted to divide {name} by zero!");
        }
    }
    
    private void OnEnable()
    {
        // Reset to default when the game starts or the SO is loaded
        runtimeValue = defaultValue;
    }
    
#if UNITY_EDITOR
    private void OnValidate()
    {
        // In editor, keep runtime value in sync with default during setup
        if (!Application.isPlaying)
        {
            runtimeValue = defaultValue;
        }
    }
#endif
}

