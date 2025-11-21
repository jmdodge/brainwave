using System.Collections;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Gameplay
{
    /// <summary>
    /// Procedurally generates and animates waveform visualizations using LineRenderer.
    /// Supports multiple wave types (sine, square, triangle, sawtooth) in both linear and circular modes.
    /// Includes flowing animation, modulation effects, and particle trails.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class WaveformVisualizer : MonoBehaviour
    {
        [TitleGroup("Wave Type & Quality")]
        [Tooltip("Type of waveform to generate")]
        [SerializeField] WaveType waveType = WaveType.Sine;

        [TitleGroup("Wave Type & Quality")]
        [Tooltip("Number of points used to draw the wave (higher = smoother)")]
        [Range(16, 512)]
        [SerializeField] int waveResolution = 64;

        [TitleGroup("Wave Type & Quality")]
        [Tooltip("Quality preset for quick selection")]
        [OnValueChanged("ApplyQualityPreset")]
        [SerializeField] QualityPreset qualityPreset = QualityPreset.Medium;

        [TitleGroup("Rendering Mode")]
        [Tooltip("How to render the waveform")]
        [SerializeField] RenderMode renderMode = RenderMode.StraightLine;

        [TitleGroup("Wave Properties")]
        [Tooltip("Length of the wave in world units (for straight line mode)")]
        [Range(1f, 20f)]
        [SerializeField] float waveLength = 5f;

        [TitleGroup("Wave Properties")]
        [Tooltip("Amplitude of the wave in world units")]
        [Range(0.1f, 5f)]
        [SerializeField] float amplitude = 1f;

        [TitleGroup("Wave Properties")]
        [Tooltip("Frequency of the wave (cycles per unit length)")]
        [Range(0.1f, 10f)]
        [SerializeField] float frequency = 2f;

        [TitleGroup("Circular Mode")]
        [ShowIf("renderMode", RenderMode.CircularBorder)]
        [Tooltip("Base radius of the circular wave")]
        [Range(0.5f, 10f)]
        [SerializeField] float circleRadius = 2f;

        [TitleGroup("Circular Mode")]
        [ShowIf("renderMode", RenderMode.CircularBorder)]
        [Tooltip("Rotation speed of the circle (degrees per second)")]
        [Range(-360f, 360f)]
        [SerializeField] float circleRotationSpeed = 30f;

        [TitleGroup("Animation")]
        [Tooltip("Enable wave flow animation")]
        [SerializeField] bool enableWaveFlow = true;

        [TitleGroup("Animation")]
        [ShowIf("enableWaveFlow")]
        [Tooltip("Speed of wave flow animation")]
        [Range(-5f, 5f)]
        [SerializeField] float waveFlowSpeed = 1f;

        [TitleGroup("Modulation")]
        [Tooltip("Enable amplitude modulation over time")]
        [SerializeField] bool enableAmplitudeModulation = false;

        [TitleGroup("Modulation")]
        [ShowIf("enableAmplitudeModulation")]
        [Tooltip("Speed of amplitude modulation")]
        [Range(0.1f, 5f)]
        [SerializeField] float amplitudeModulationSpeed = 1f;

        [TitleGroup("Modulation")]
        [ShowIf("enableAmplitudeModulation")]
        [Tooltip("Depth of amplitude modulation (0-1)")]
        [Range(0f, 1f)]
        [SerializeField] float amplitudeModulationDepth = 0.3f;

        [TitleGroup("Modulation")]
        [Tooltip("Enable frequency modulation over time")]
        [SerializeField] bool enableFrequencyModulation = false;

        [TitleGroup("Modulation")]
        [ShowIf("enableFrequencyModulation")]
        [Tooltip("Speed of frequency modulation")]
        [Range(0.1f, 5f)]
        [SerializeField] float frequencyModulationSpeed = 0.5f;

        [TitleGroup("Modulation")]
        [ShowIf("enableFrequencyModulation")]
        [Tooltip("Depth of frequency modulation")]
        [Range(0f, 2f)]
        [SerializeField] float frequencyModulationDepth = 0.5f;

        [TitleGroup("Visual Effects")]
        [Tooltip("Line width of the waveform")]
        [Range(0.01f, 1f)]
        [SerializeField] float lineWidth = 0.1f;

        [TitleGroup("Visual Effects")]
        [Tooltip("Base color of the waveform")]
        [ColorUsage(true, true)] // HDR for glow
        [SerializeField] Color waveColor = new Color(0.5f, 1f, 1f, 1f);

        [TitleGroup("Visual Effects")]
        [Tooltip("Glow intensity multiplier")]
        [Range(1f, 5f)]
        [SerializeField] float glowIntensity = 2f;

        [TitleGroup("Particle Trail")]
        [Tooltip("Enable particle trail effect")]
        [SerializeField] bool enableParticleTrail = true;

        [TitleGroup("Particle Trail")]
        [ShowIf("enableParticleTrail")]
        [Tooltip("Number of trail particles")]
        [Range(5, 50)]
        [SerializeField] int trailParticleCount = 20;

        [TitleGroup("Particle Trail")]
        [ShowIf("enableParticleTrail")]
        [Tooltip("Speed of particles along the wave")]
        [Range(0.1f, 5f)]
        [SerializeField] float trailSpeed = 1f;

        [TitleGroup("Particle Trail")]
        [ShowIf("enableParticleTrail")]
        [Tooltip("Size of trail particles")]
        [Range(0.01f, 0.5f)]
        [SerializeField] float trailParticleSize = 0.05f;

        [TitleGroup("Pickup State")]
        [Tooltip("Animation speed multiplier when picked up")]
        [Range(1f, 10f)]
        [SerializeField] float pickupSpeedMultiplier = 3f;

        [TitleGroup("Pickup State")]
        [Tooltip("Glow intensity multiplier when picked up")]
        [Range(1f, 5f)]
        [SerializeField] float pickupGlowMultiplier = 2f;

        [TitleGroup("Debug")]
        [Button("Regenerate Wave")]
        void RegenerateButton() => GenerateWaveform();

        [TitleGroup("Debug")]
        [Button("Toggle Pickup State")]
        void TogglePickupButton() => SetPickedUpState(!isPickedUp);

        // Components and state
        LineRenderer lineRenderer;
        GameObject[] trailParticles;
        SpriteRenderer[] trailRenderers;
        Vector3[] wavePoints;
        float[] trailPositions;
        bool isPickedUp;
        float currentPhase;
        float circleRotation;

        public enum WaveType
        {
            Sine,
            Square,
            Triangle,
            Sawtooth
        }

        public enum RenderMode
        {
            StraightLine,
            CircularBorder
        }

        public enum QualityPreset
        {
            Low,        // 32 points
            Medium,     // 64 points
            High,       // 128 points
            Ultra       // 256 points
        }

        void Awake()
        {
            // Get or add LineRenderer component
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null)
                lineRenderer = gameObject.AddComponent<LineRenderer>();

            SetupLineRenderer();
            GenerateWaveform();
        }

        void Update()
        {
            AnimateWaveform();
        }

        /// <summary>
        /// Sets whether the waveform is in "picked up" state (faster animation, more glow)
        /// </summary>
        public void SetPickedUpState(bool pickedUp)
        {
            isPickedUp = pickedUp;
            UpdateVisualEffects();
        }

        /// <summary>
        /// Applies quality preset to wave resolution
        /// </summary>
        void ApplyQualityPreset()
        {
            waveResolution = qualityPreset switch
            {
                QualityPreset.Low => 32,
                QualityPreset.Medium => 64,
                QualityPreset.High => 128,
                QualityPreset.Ultra => 256,
                _ => 64
            };
        }

        /// <summary>
        /// Configures the LineRenderer component with proper settings
        /// </summary>
        void SetupLineRenderer()
        {
            // Basic line renderer setup
            lineRenderer.useWorldSpace = false;
            lineRenderer.loop = renderMode == RenderMode.CircularBorder;
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;

            // Create a simple material with glow if none exists
            if (lineRenderer.material == null)
            {
                Material mat = new Material(Shader.Find("Sprites/Default"));
                mat.color = waveColor * glowIntensity;
                lineRenderer.material = mat;
            }

            UpdateVisualEffects();
        }

        /// <summary>
        /// Updates visual effects based on current state
        /// </summary>
        void UpdateVisualEffects()
        {
            if (lineRenderer == null) return;

            float currentGlowMultiplier = isPickedUp ? pickupGlowMultiplier : 1f;
            Color finalColor = waveColor * glowIntensity * currentGlowMultiplier;

            lineRenderer.material.color = finalColor;
            lineRenderer.startWidth = lineWidth;
            lineRenderer.endWidth = lineWidth;

            // Update trail particles if they exist
            if (trailRenderers != null)
            {
                foreach (var renderer in trailRenderers)
                {
                    if (renderer != null)
                        renderer.color = finalColor;
                }
            }
        }

        /// <summary>
        /// Generates the waveform points and creates trail particles
        /// </summary>
        void GenerateWaveform()
        {
            // Clean up existing trail particles
            CleanupTrailParticles();

            // Generate wave points array
            wavePoints = new Vector3[waveResolution];
            lineRenderer.positionCount = waveResolution;

            // Create trail particles if enabled
            if (enableParticleTrail)
                CreateTrailParticles();

            // Generate initial wave points
            UpdateWavePoints();
        }

        /// <summary>
        /// Updates the wave points based on current parameters and animation state
        /// </summary>
        void UpdateWavePoints()
        {
            float speedMultiplier = isPickedUp ? pickupSpeedMultiplier : 1f;
            float time = Time.time;

            // Calculate current modulated values
            float currentAmplitude = amplitude;
            float currentFrequency = frequency;

            if (enableAmplitudeModulation)
            {
                float ampMod = 1f + Mathf.Sin(time * amplitudeModulationSpeed * speedMultiplier) * amplitudeModulationDepth;
                currentAmplitude *= ampMod;
            }

            if (enableFrequencyModulation)
            {
                float freqMod = 1f + Mathf.Sin(time * frequencyModulationSpeed * speedMultiplier) * frequencyModulationDepth;
                currentFrequency *= freqMod;
            }

            // Update wave flow phase
            if (enableWaveFlow)
            {
                currentPhase += waveFlowSpeed * speedMultiplier * Time.deltaTime;
            }

            // Generate wave points based on render mode
            for (int i = 0; i < waveResolution; i++)
            {
                Vector3 point = renderMode switch
                {
                    RenderMode.StraightLine => CalculateStraightLinePoint(i, currentAmplitude, currentFrequency),
                    RenderMode.CircularBorder => CalculateCircularPoint(i, currentAmplitude, currentFrequency),
                    _ => Vector3.zero
                };

                wavePoints[i] = point;
            }

            // Apply points to LineRenderer
            lineRenderer.SetPositions(wavePoints);
        }

        /// <summary>
        /// Calculates a point for straight line wave rendering
        /// </summary>
        Vector3 CalculateStraightLinePoint(int index, float currentAmplitude, float currentFrequency)
        {
            float t = (float)index / (waveResolution - 1); // 0 to 1
            float x = (t - 0.5f) * waveLength; // Center the wave
            float phase = t * currentFrequency * Mathf.PI * 2f + currentPhase;
            float y = CalculateWaveValue(phase) * currentAmplitude;

            return new Vector3(x, y, 0f);
        }

        /// <summary>
        /// Calculates a point for circular wave rendering
        /// </summary>
        Vector3 CalculateCircularPoint(int index, float currentAmplitude, float currentFrequency)
        {
            float angle = (float)index / waveResolution * Mathf.PI * 2f;
            float wavePhase = angle * currentFrequency + currentPhase;
            float waveValue = CalculateWaveValue(wavePhase);
            
            // Radius varies based on wave amplitude
            float radius = circleRadius + waveValue * currentAmplitude;
            
            float x = Mathf.Cos(angle) * radius;
            float y = Mathf.Sin(angle) * radius;

            return new Vector3(x, y, 0f);
        }

        /// <summary>
        /// Calculates the wave value for a given phase based on wave type
        /// </summary>
        float CalculateWaveValue(float phase)
        {
            return waveType switch
            {
                WaveType.Sine => Mathf.Sin(phase),
                WaveType.Square => Mathf.Sign(Mathf.Sin(phase)),
                WaveType.Triangle => Mathf.Asin(Mathf.Sin(phase)) * 2f / Mathf.PI,
                WaveType.Sawtooth => 2f * (phase / (2f * Mathf.PI) - Mathf.Floor(phase / (2f * Mathf.PI) + 0.5f)),
                _ => 0f
            };
        }

        /// <summary>
        /// Creates trail particle GameObjects
        /// </summary>
        void CreateTrailParticles()
        {
            trailParticles = new GameObject[trailParticleCount];
            trailRenderers = new SpriteRenderer[trailParticleCount];
            trailPositions = new float[trailParticleCount];

            for (int i = 0; i < trailParticleCount; i++)
            {
                // Create particle GameObject
                GameObject particle = new GameObject($"TrailParticle_{i}");
                particle.transform.SetParent(transform);

                // Add sprite renderer
                SpriteRenderer sr = particle.AddComponent<SpriteRenderer>();
                sr.sprite = CreateCircleSprite();
                sr.color = waveColor * glowIntensity;

                // Set size
                particle.transform.localScale = Vector3.one * trailParticleSize;

                // Initialize position along the wave
                trailPositions[i] = (float)i / trailParticleCount;

                trailParticles[i] = particle;
                trailRenderers[i] = sr;
            }
        }

        /// <summary>
        /// Animates the waveform and trail particles
        /// </summary>
        void AnimateWaveform()
        {
            // Update wave points
            UpdateWavePoints();

            // Rotate circle if in circular mode
            if (renderMode == RenderMode.CircularBorder)
            {
                float speedMultiplier = isPickedUp ? pickupSpeedMultiplier : 1f;
                circleRotation += circleRotationSpeed * speedMultiplier * Time.deltaTime;
                transform.rotation = Quaternion.Euler(0f, 0f, circleRotation);
            }

            // Animate trail particles
            AnimateTrailParticles();
        }

        /// <summary>
        /// Updates positions of trail particles along the wave
        /// </summary>
        void AnimateTrailParticles()
        {
            if (!enableParticleTrail || trailParticles == null) return;

            float speedMultiplier = isPickedUp ? pickupSpeedMultiplier : 1f;

            for (int i = 0; i < trailParticles.Length; i++)
            {
                if (trailParticles[i] == null) continue;

                // Update trail position
                trailPositions[i] += trailSpeed * speedMultiplier * Time.deltaTime;
                if (trailPositions[i] > 1f) trailPositions[i] -= 1f;

                // Calculate position along the wave
                int waveIndex = Mathf.FloorToInt(trailPositions[i] * (wavePoints.Length - 1));
                waveIndex = Mathf.Clamp(waveIndex, 0, wavePoints.Length - 1);

                // Interpolate between wave points for smooth movement
                Vector3 position = wavePoints[waveIndex];
                if (waveIndex < wavePoints.Length - 1)
                {
                    float t = (trailPositions[i] * (wavePoints.Length - 1)) - waveIndex;
                    position = Vector3.Lerp(wavePoints[waveIndex], wavePoints[waveIndex + 1], t);
                }

                trailParticles[i].transform.localPosition = position;

                // Fade particles based on position for trail effect
                float alpha = 1f - (i / (float)trailParticleCount) * 0.7f;
                Color color = waveColor * glowIntensity * (isPickedUp ? pickupGlowMultiplier : 1f);
                color.a *= alpha;
                trailRenderers[i].color = color;
            }
        }

        /// <summary>
        /// Creates a simple circle sprite for trail particles
        /// </summary>
        Sprite CreateCircleSprite()
        {
            int resolution = 32; // Smaller resolution for trail particles
            Texture2D texture = new Texture2D(resolution, resolution);
            Color[] pixels = new Color[resolution * resolution];

            Vector2 center = new Vector2(resolution / 2f, resolution / 2f);
            float radius = resolution / 2f - 1f;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 pos = new Vector2(x, y);
                    float dist = Vector2.Distance(pos, center);

                    // Soft edge for glow effect
                    float alpha = 1f - Mathf.Clamp01((dist - radius + 2f) / 2f);
                    pixels[y * resolution + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply();
            texture.filterMode = FilterMode.Bilinear;

            return Sprite.Create(
                texture,
                new Rect(0, 0, resolution, resolution),
                new Vector2(0.5f, 0.5f),
                resolution / 2f);
        }

        /// <summary>
        /// Cleans up trail particle GameObjects
        /// </summary>
        void CleanupTrailParticles()
        {
            if (trailParticles != null)
            {
                foreach (GameObject particle in trailParticles)
                {
                    if (particle != null)
                    {
                        if (Application.isPlaying)
                            Destroy(particle);
                        else
                            DestroyImmediate(particle);
                    }
                }
            }
        }

        void OnValidate()
        {
            // Apply quality preset if changed
            if (qualityPreset != QualityPreset.Medium || waveResolution == 64)
            {
                ApplyQualityPreset();
            }

            // Update line renderer if it exists
            if (lineRenderer != null)
            {
                SetupLineRenderer();
                if (Application.isPlaying)
                {
                    GenerateWaveform();
                }
            }
        }

        void OnDestroy()
        {
            CleanupTrailParticles();
        }
    }
}
