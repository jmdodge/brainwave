using UnityEngine;
using UnityEngine.Events;

/**
 * Schedules an enemy's vulnerability window on the global tempo timeline while exposing UnityEvents for design hooks.
 * Example:
 * vulnerableEnemy.PrimeVulnerability();
 * vulnerableEnemy.onVulnerabilityBecameAvailable.AddListener(OnWindowOpened);
 */
public sealed class VulnerableEnemy : MonoBehaviour
{
    [SerializeField] TempoManager tempoManager;
    [Min(0)]
    [SerializeField] int beatsUntilVulnerable = 4;
    [SerializeField] UnityEvent onVulnerabilityPrimed;
    [SerializeField] UnityEvent onVulnerabilityBecameAvailable;
    [SerializeField] UnityEvent onVulnerabilityCancelled;

    TempoManager.TempoEventHandle scheduledHandle;
    bool vulnerabilityPrimed;
    bool isVulnerable;

    /**
     * Lazily resolves the tempo manager so the component can work when instantiated at runtime.
     */
    void OnEnable()
    {
        if (tempoManager == null) tempoManager = FindAnyObjectByType<TempoManager>();
    }

    /**
     * Cancels any pending beat schedule when the component is disabled.
     */
    void OnDisable()
    {
        CancelPendingSchedule();
    }

    /**
     * Schedules the enemy to become vulnerable after the configured number of beats.
     * Example:
     * vulnerableEnemy.PrimeVulnerability();
     */
    public void PrimeVulnerability()
    {
        if (tempoManager == null)
        {
            Debug.LogWarning("VulnerableEnemy has no TempoManager reference.", this);
            return;
        }

        CancelPendingSchedule();

        double currentBeat = tempoManager.CurrentBeat;
        double targetBeat = Mathf.CeilToInt((float)currentBeat) + Mathf.Max(0, beatsUntilVulnerable);

        scheduledHandle = tempoManager.ScheduleAtBeat(targetBeat, EnterVulnerableState);
        vulnerabilityPrimed = scheduledHandle.IsValid;

        if (vulnerabilityPrimed)
        {
            onVulnerabilityPrimed?.Invoke();
        }
        else
        {
            Debug.LogWarning("VulnerableEnemy could not schedule vulnerability.", this);
        }
    }

    /**
     * Cancels the pending vulnerability and ends the current vulnerable state.
     * Example:
     * vulnerableEnemy.ResetVulnerability();
     */
    public void ResetVulnerability()
    {
        CancelPendingSchedule();
        if (isVulnerable)
        {
            isVulnerable = false;
            onVulnerabilityCancelled?.Invoke();
        }
    }

    /**
     * Invoked by the tempo manager when the scheduled beat arrives to open the vulnerability window.
     */
    void EnterVulnerableState()
    {
        vulnerabilityPrimed = false;
        isVulnerable = true;
        scheduledHandle = default;
        onVulnerabilityBecameAvailable?.Invoke();
    }

    /**
     * Removes any scheduled tempo callback and notifies listeners if a primed window was cancelled.
     */
    void CancelPendingSchedule()
    {
        if (!scheduledHandle.IsValid || tempoManager == null) return;
        tempoManager.CancelScheduledEvent(scheduledHandle);
        scheduledHandle = default;

        if (vulnerabilityPrimed)
        {
            vulnerabilityPrimed = false;
            onVulnerabilityCancelled?.Invoke();
        }
    }
}

