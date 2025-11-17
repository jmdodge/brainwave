using UnityEngine;
using TMPro;

/// <summary>
/// Example script showing how to use FloatVarSO
/// </summary>
public class FloatVarSOExample : MonoBehaviour
{
    [Header("Float Variable Reference")]
    [SerializeField] private FloatVarSO healthVariable;
    
    [Header("UI (Optional)")]
    [SerializeField] private TextMeshProUGUI displayText;
    
    private void OnEnable()
    {
        // Subscribe to value changes
        if (healthVariable != null)
        {
            healthVariable.onValueChanged.AddListener(OnHealthChanged);
            // Update UI with initial value
            OnHealthChanged(healthVariable.Value);
        }
    }
    
    private void OnDisable()
    {
        // Unsubscribe to prevent memory leaks
        if (healthVariable != null)
        {
            healthVariable.onValueChanged.RemoveListener(OnHealthChanged);
        }
    }
    
    private void OnHealthChanged(float newValue)
    {
        Debug.Log($"Health changed to: {newValue}");
        
        // Update UI if available
        if (displayText != null)
        {
            displayText.text = $"Health: {newValue:F1}";
        }
    }
    
    // Example methods that modify the float variable
    public void TakeDamage(float damage)
    {
        healthVariable.Subtract(damage);
    }
    
    public void Heal(float amount)
    {
        healthVariable.Add(amount);
    }
    
    public void ResetHealth()
    {
        healthVariable.ResetToDefault();
    }
}

