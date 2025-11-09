using System.Collections.Generic;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [SerializeField] private List<GameObject> targetObjects; // Assign in Inspector
    [SerializeField] private List<int> intervalsMs; // Time between activations, in ms, assign in Inspector

    private class TimerData
    {
        public GameObject obj;
        public int intervalMs;
        public float nextEnableTime;
        public bool isActive;
        public float disableTime;
    }

    private List<TimerData> timers = new List<TimerData>();
    private const float enableDuration = 1f; // 1000ms

    void Start()
    {
        // Validate data
        if (targetObjects == null || intervalsMs == null || targetObjects.Count != intervalsMs.Count)
        {
            Debug.LogError("GameManager: targetObjects and intervalsMs must be non-null and have the same count.");
            enabled = false;
            return;
        }
        // Initialize timers
        for (int i = 0; i < targetObjects.Count; i++)
        {
            timers.Add(new TimerData
            {
                obj = targetObjects[i],
                intervalMs = intervalsMs[i],
                nextEnableTime = Time.time,
                isActive = false,
                disableTime = 0f
            });
            if (targetObjects[i] != null)
                targetObjects[i].SetActive(false); // Ensure starts disabled
        }
    }

    void Update()
    {
        float currentTime = Time.time;

        foreach (var timer in timers)
        {
            if (!timer.isActive)
            {
                // Time to enable
                if (currentTime >= timer.nextEnableTime)
                {
                    if (timer.obj != null)
                        timer.obj.SetActive(true);
                    timer.isActive = true;
                    timer.disableTime = currentTime + enableDuration;
                }
            }
            else
            {
                // Currently active, check if needs to be disabled
                if (currentTime >= timer.disableTime)
                {
                    if (timer.obj != null)
                        timer.obj.SetActive(false);
                    timer.isActive = false;
                    timer.nextEnableTime = currentTime + (timer.intervalMs / 1000f);
                }
            }
        }
    }
}
