using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Gameplay
{
    /// <summary>
    /// Advanced molecule visualizer with tempo synchronization and per-shape control.
    /// Creates orbiting shapes that can represent musical rhythm patterns.
    /// </summary>
    public class RhythmMoleculeVisualizer : MonoBehaviour, IPickable
    {
        [TitleGroup("Tempo Integration")]
        [Tooltip("Reference to the TempoManager for beat synchronization")]
        [SerializeField]
        TempoManager tempoManager;

        [TitleGroup("Tempo Integration")] [Tooltip("Use tempo-based rotation instead of fixed speed")] [SerializeField]
        bool useTempoSync = true;

        [TitleGroup("Tempo Integration")]
        [Tooltip("Number of full rotations per bar (e.g., 1.0 = one rotation per 4 beats in 4/4 time)")]
        [ShowIf("useTempoSync")]
        [Range(0.1f, 8f)]
        [SerializeField]
        float rotationsPerBar = 1f;

        [TitleGroup("Tempo Integration")]
        [Tooltip("Fixed rotation speed in degrees per second (used when tempo sync is disabled)")]
        [HideIf("useTempoSync")]
        [Range(0f, 360f)]
        [SerializeField]
        float fixedRotationSpeed = 30f;

        [TitleGroup("Central Shape")]
        [Tooltip("Configuration for the central shape that others orbit around")]
        [SerializeField]
        CentralShapeConfig centralShape = new CentralShapeConfig();

        [TitleGroup("Orbiting Shapes")]
        [Tooltip("List of shapes that orbit around the central shape")]
        [ListDrawerSettings(ShowIndexLabels = true, ListElementLabelName = "GetShapeLabel")]
        [SerializeField]
        List<OrbitingShapeConfig> orbitingShapes = new List<OrbitingShapeConfig>();

        [TitleGroup("Global Animation")] [Tooltip("Use tempo-based pulsing instead of fixed speed")] [SerializeField]
        bool useTempoPulse = true;

        [TitleGroup("Global Animation")]
        [Tooltip("Number of pulses per bar (e.g., 4.0 = four pulses per bar)")]
        [ShowIf("useTempoPulse")]
        [Range(0.1f, 16f)]
        [SerializeField]
        float pulsesPerBar = 4f;

        [TitleGroup("Global Animation")]
        [Tooltip("Fixed pulse speed (oscillations per second, used when tempo pulse is disabled)")]
        [HideIf("useTempoPulse")]
        [Range(0f, 10f)]
        [SerializeField]
        float fixedPulseSpeed = 2f;

        [TitleGroup("Global Animation")] [Tooltip("Global pulse magnitude")] [Range(0f, 0.5f)] [SerializeField]
        float globalPulseMagnitude = 0.1f;

        [TitleGroup("Pickup State")]
        [Tooltip("Animation speed multiplier when picked up")]
        [Range(1f, 10f)]
        [SerializeField]
        float pickupSpeedMultiplier = 3f;

        [TitleGroup("Pickup State")]
        [Tooltip("Glow intensity multiplier when picked up")]
        [Range(1f, 5f)]
        [SerializeField]
        float pickupGlowMultiplier = 2f;

        [TitleGroup("Rendering")]
        [Tooltip("Sorting layer for all shapes")]
        [ValueDropdown("GetSortingLayerNames")]
        [SerializeField]
        string sortingLayerName = "Default";

        [HideInInspector] [SerializeField] int sortingLayerID = 0;

        [TitleGroup("Rendering")] [Tooltip("Base sorting order")] [SerializeField]
        int baseSortingOrder = 0;

        [TitleGroup("Debug")]
        [Button("Regenerate All Shapes")]
        void RegenerateButton() => GenerateAllShapes();

        [TitleGroup("Debug")]
        [Button("Find TempoManager")]
        void FindTempoManagerButton()
        {
            tempoManager = FindAnyObjectByType<TempoManager>();
            if (tempoManager == null)
                Debug.LogWarning("No TempoManager found in scene!");
            else
                Debug.Log($"Found TempoManager: {tempoManager.name}");
        }

        // Runtime data
        GameObject centralShapeObject;
        SpriteRenderer centralShapeRenderer;
        List<GameObject> orbitingShapeObjects = new List<GameObject>();
        List<SpriteRenderer> orbitingShapeRenderers = new List<SpriteRenderer>();
        bool isPickedUp;
        float currentRotation;

        void Awake()
        {
            if (tempoManager == null)
                tempoManager = FindAnyObjectByType<TempoManager>();

            GenerateAllShapes();
        }

        void Update()
        {
            AnimateShapes();
        }

        /// <summary>
        /// Sets whether the molecule is in "picked up" state
        /// </summary>
        public void SetPickedUpState(bool pickedUp)
        {
            isPickedUp = pickedUp;
            UpdateGlowIntensity();
        }

        /// <summary>
        /// Generates all shapes (central + orbiting) based on current configuration
        /// </summary>
        void GenerateAllShapes()
        {
            CleanupExistingShapes();
            CreateCentralShape();
            CreateOrbitingShapes();
        }

        /// <summary>
        /// Removes all existing shape GameObjects
        /// </summary>
        void CleanupExistingShapes()
        {
            // Clean up central shape
            if (centralShapeObject != null)
            {
                if (Application.isPlaying)
                    Destroy(centralShapeObject);
                else
                    DestroyImmediate(centralShapeObject);
            }

            // Clean up orbiting shapes
            foreach (GameObject shape in orbitingShapeObjects)
            {
                if (shape != null)
                {
                    if (Application.isPlaying)
                        Destroy(shape);
                    else
                        DestroyImmediate(shape);
                }
            }

            orbitingShapeObjects.Clear();
            orbitingShapeRenderers.Clear();
        }

        /// <summary>
        /// Creates the central shape GameObject
        /// </summary>
        void CreateCentralShape()
        {
            if (!centralShape.enabled) return;

            centralShapeObject = new GameObject("CentralShape");
            centralShapeObject.transform.SetParent(transform);
            centralShapeObject.transform.localPosition = Vector3.zero;

            centralShapeRenderer = centralShapeObject.AddComponent<SpriteRenderer>();
            centralShapeRenderer.sprite = CreateShapeSprite(centralShape.shapeType, centralShape.size);
            centralShapeRenderer.color = centralShape.color;
            centralShapeRenderer.sortingLayerID = sortingLayerID;
            centralShapeRenderer.sortingOrder = baseSortingOrder + 10; // Central shape on top

            centralShapeObject.transform.localScale = Vector3.one * centralShape.size;
            centralShapeObject.transform.rotation = Quaternion.Euler(0f, 0f, centralShape.orientation);
        }

        /// <summary>
        /// Creates all orbiting shape GameObjects
        /// </summary>
        void CreateOrbitingShapes()
        {
            for (int i = 0; i < orbitingShapes.Count; i++)
            {
                var shapeConfig = orbitingShapes[i];

                GameObject shapeObj = new GameObject($"OrbitingShape_{i}");
                shapeObj.transform.SetParent(transform);

                SpriteRenderer sr = shapeObj.AddComponent<SpriteRenderer>();
                sr.sprite = CreateShapeSprite(shapeConfig.shapeType, shapeConfig.size);
                sr.color = shapeConfig.color;
                sr.sortingLayerID = sortingLayerID;
                sr.sortingOrder = baseSortingOrder + i;

                // Calculate position based on rhythm position and radius
                Vector3 position = CalculateOrbitingShapePosition(shapeConfig);
                shapeObj.transform.localPosition = position;
                shapeObj.transform.localScale = Vector3.one * shapeConfig.size;
                shapeObj.transform.rotation = Quaternion.Euler(0f, 0f, shapeConfig.orientation);

                orbitingShapeObjects.Add(shapeObj);
                orbitingShapeRenderers.Add(sr);
            }
        }

        /// <summary>
        /// Calculates the position of an orbiting shape based on its rhythm position
        /// </summary>
        Vector3 CalculateOrbitingShapePosition(OrbitingShapeConfig shapeConfig)
        {
            // Convert rhythm position (in beats) to angle
            float beatsPerBar = tempoManager != null ? tempoManager.BeatsPerBar : 4f;
            float normalizedPosition = shapeConfig.rhythmPositionInBeats / beatsPerBar;
            float angle = normalizedPosition * 360f * Mathf.Deg2Rad;

            return new Vector3(
                Mathf.Cos(angle) * shapeConfig.radius,
                Mathf.Sin(angle) * shapeConfig.radius,
                0f
            );
        }

        /// <summary>
        /// Animates all shapes based on tempo and configuration
        /// </summary>
        void AnimateShapes()
        {
            float speedMultiplier = isPickedUp ? pickupSpeedMultiplier : 1f;
            float time = Time.time;

            // Calculate rotation speed
            float rotationSpeed = GetCurrentRotationSpeed() * speedMultiplier;

            // Update global rotation
            currentRotation += rotationSpeed * Time.deltaTime;
            transform.rotation = Quaternion.Euler(0f, 0f, currentRotation);

            // Animate central shape
            AnimateCentralShape(time, speedMultiplier);

            // Animate orbiting shapes
            AnimateOrbitingShapes(time, speedMultiplier);
        }

        /// <summary>
        /// Gets the current rotation speed in degrees per second
        /// </summary>
        float GetCurrentRotationSpeed()
        {
            if (!useTempoSync || tempoManager == null)
                return fixedRotationSpeed;

            // Calculate degrees per second based on rotations per bar
            float beatsPerBar = tempoManager.BeatsPerBar;
            float secondsPerBar = beatsPerBar * (float)tempoManager.SecondsPerBeat;
            return (rotationsPerBar * 360f) / secondsPerBar;
        }

        /// <summary>
        /// Gets the current pulse speed in oscillations per second
        /// </summary>
        float GetCurrentPulseSpeed()
        {
            if (!useTempoPulse || tempoManager == null)
                return fixedPulseSpeed;

            // Calculate oscillations per second based on pulses per bar
            float beatsPerBar = tempoManager.BeatsPerBar;
            float secondsPerBar = beatsPerBar * (float)tempoManager.SecondsPerBeat;
            return pulsesPerBar / secondsPerBar;
        }

        /// <summary>
        /// Animates the central shape with pulsing effects
        /// </summary>
        void AnimateCentralShape(float time, float speedMultiplier)
        {
            if (centralShapeObject == null || !centralShape.enabled) return;

            // Pulsing scale animation
            if (centralShape.enablePulse)
            {
                float pulseSpeed = GetCurrentPulseSpeed() * speedMultiplier;
                float pulse = Mathf.Sin(time * pulseSpeed * 2f * Mathf.PI) * centralShape.pulseAmount;
                float scale = centralShape.size * (1f + pulse);
                centralShapeObject.transform.localScale = Vector3.one * scale;
            }
        }

        /// <summary>
        /// Animates orbiting shapes with individual orbital motion
        /// </summary>
        void AnimateOrbitingShapes(float time, float speedMultiplier)
        {
            for (int i = 0; i < orbitingShapeObjects.Count && i < orbitingShapes.Count; i++)
            {
                GameObject shapeObj = orbitingShapeObjects[i];
                OrbitingShapeConfig config = orbitingShapes[i];

                if (shapeObj == null) continue;

                // Base position from rhythm positioning
                Vector3 basePosition = CalculateOrbitingShapePosition(config);

                // Add orbital motion if enabled
                Vector3 orbitOffset = Vector3.zero;
                if (config.enableOrbit)
                {
                    // Use base rotation speed multiplied by individual shape's multiplier
                    float baseRotationSpeed = GetCurrentRotationSpeed();
                    float individualOrbitSpeed = baseRotationSpeed * config.orbitSpeedMultiplier * speedMultiplier;
                    float orbitAngle = time * individualOrbitSpeed * Mathf.Deg2Rad;
                    float currentAngle = Mathf.Atan2(basePosition.y, basePosition.x) + orbitAngle;
                    float radius = basePosition.magnitude;
                    orbitOffset = new Vector3(Mathf.Cos(currentAngle) * radius, Mathf.Sin(currentAngle) * radius, 0f) -
                                  basePosition;
                }

                // Add pulsing effect
                Vector3 pulseOffset = Vector3.zero;
                if (config.enablePulse)
                {
                    float pulseSpeed = GetCurrentPulseSpeed() * speedMultiplier;
                    float pulse = Mathf.Sin(time * pulseSpeed * 2f * Mathf.PI + i * 0.5f) * globalPulseMagnitude;
                    pulseOffset = basePosition.normalized * pulse;
                }

                shapeObj.transform.localPosition = basePosition + orbitOffset + pulseOffset;

                // Scale pulsing
                if (config.enablePulse)
                {
                    float pulseSpeed = GetCurrentPulseSpeed() * speedMultiplier;
                    float scalePulse = 1f + Mathf.Sin(time * pulseSpeed * 4f * Mathf.PI + i * 0.3f) * 0.1f;
                    shapeObj.transform.localScale = Vector3.one * config.size * scalePulse;
                }
            }
        }

        /// <summary>
        /// Updates glow intensity based on pickup state
        /// </summary>
        void UpdateGlowIntensity()
        {
            float glowMultiplier = isPickedUp ? pickupGlowMultiplier : 1f;

            // Update central shape
            if (centralShapeRenderer != null)
            {
                centralShapeRenderer.color = centralShape.color * glowMultiplier;
            }

            // Update orbiting shapes
            for (int i = 0; i < orbitingShapeRenderers.Count && i < orbitingShapes.Count; i++)
            {
                if (orbitingShapeRenderers[i] != null)
                {
                    orbitingShapeRenderers[i].color = orbitingShapes[i].color * glowMultiplier;
                }
            }
        }

        /// <summary>
        /// Creates a sprite for the specified shape type
        /// </summary>
        Sprite CreateShapeSprite(ShapeType shapeType, float size)
        {
            int resolution = 64;
            Texture2D texture = new Texture2D(resolution, resolution);
            Color[] pixels = new Color[resolution * resolution];

            // Clear to transparent
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = Color.clear;

            Vector2 center = new Vector2(resolution / 2f, resolution / 2f);
            float shapeSize = (resolution / 2f) - 4f;

            switch (shapeType)
            {
                case ShapeType.Circle:
                    DrawCircle(pixels, resolution, center, shapeSize);
                    break;
                case ShapeType.Square:
                    DrawSquare(pixels, resolution, center, shapeSize);
                    break;
                case ShapeType.Triangle:
                    DrawTriangle(pixels, resolution, center, shapeSize);
                    break;
                case ShapeType.Line:
                    DrawLine(pixels, resolution, center, shapeSize);
                    break;
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

        void DrawCircle(Color[] pixels, int resolution, Vector2 center, float radius)
        {
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 pos = new Vector2(x, y);
                    float dist = Vector2.Distance(pos, center);

                    if (dist <= radius)
                    {
                        float alpha = 1f - Mathf.Clamp01((dist - radius + 2f) / 2f);
                        pixels[y * resolution + x] = new Color(1f, 1f, 1f, alpha);
                    }
                }
            }
        }

        void DrawSquare(Color[] pixels, int resolution, Vector2 center, float size)
        {
            int halfSize = Mathf.RoundToInt(size);
            int minX = Mathf.RoundToInt(center.x - halfSize);
            int maxX = Mathf.RoundToInt(center.x + halfSize);
            int minY = Mathf.RoundToInt(center.y - halfSize);
            int maxY = Mathf.RoundToInt(center.y + halfSize);

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (x >= 0 && x < resolution && y >= 0 && y < resolution)
                    {
                        pixels[y * resolution + x] = Color.white;
                    }
                }
            }
        }

        void DrawTriangle(Color[] pixels, int resolution, Vector2 center, float size)
        {
            // Draw equilateral triangle pointing up
            Vector2[] vertices = new Vector2[]
            {
                center + new Vector2(0, size), // Top
                center + new Vector2(-size * 0.866f, -size * 0.5f), // Bottom left
                center + new Vector2(size * 0.866f, -size * 0.5f) // Bottom right
            };

            // Simple triangle fill using barycentric coordinates
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 point = new Vector2(x, y);
                    if (IsPointInTriangle(point, vertices[0], vertices[1], vertices[2]))
                    {
                        pixels[y * resolution + x] = Color.white;
                    }
                }
            }
        }

        void DrawLine(Color[] pixels, int resolution, Vector2 center, float length)
        {
            // Draw horizontal line
            int thickness = 3;
            int startX = Mathf.RoundToInt(center.x - length);
            int endX = Mathf.RoundToInt(center.x + length);
            int centerY = Mathf.RoundToInt(center.y);

            for (int x = startX; x <= endX; x++)
            {
                for (int y = centerY - thickness / 2; y <= centerY + thickness / 2; y++)
                {
                    if (x >= 0 && x < resolution && y >= 0 && y < resolution)
                    {
                        pixels[y * resolution + x] = Color.white;
                    }
                }
            }
        }

        bool IsPointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float denom = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(denom) < 0.001f) return false;

            float alpha = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / denom;
            float beta = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / denom;
            float gamma = 1 - alpha - beta;

            return alpha >= 0 && beta >= 0 && gamma >= 0;
        }

        IEnumerable GetSortingLayerNames()
        {
            return System.Array.ConvertAll(SortingLayer.layers, l => l.name);
        }

        void OnValidate()
        {
            // Sync sorting layer name to ID
            sortingLayerID = SortingLayer.NameToID(sortingLayerName);

            // Validate layer exists
            if (!System.Array.Exists(SortingLayer.layers, l => l.name == sortingLayerName))
            {
                sortingLayerName = "Default";
                sortingLayerID = 0;
            }

            if (Application.isPlaying && (centralShapeObject != null || orbitingShapeObjects.Count > 0))
            {
                GenerateAllShapes();
            }
        }
    }

    /// <summary>
    /// Configuration for the central shape
    /// </summary>
    [System.Serializable]
    public class CentralShapeConfig
    {
        [Tooltip("Enable/disable the central shape")]
        public bool enabled = true;

        [Tooltip("Shape type for the central element")]
        public ShapeType shapeType = ShapeType.Circle;

        [Tooltip("Size of the central shape")] [Range(0.1f, 2f)]
        public float size = 0.3f;

        [Tooltip("Color of the central shape")] [ColorUsage(true, true)]
        public Color color = new Color(1f, 0.5f, 1f, 1f);

        [Tooltip("Rotation orientation in degrees")] [Range(0f, 360f)]
        public float orientation = 0f;

        [Tooltip("Enable pulsing animation")] public bool enablePulse = true;

        [Tooltip("Amount of pulsing (0-1)")] [ShowIf("enablePulse")] [Range(0f, 1f)]
        public float pulseAmount = 0.2f;

        public string GetShapeLabel() => $"{shapeType} (Central)";
    }

    /// <summary>
    /// Configuration for individual orbiting shapes
    /// </summary>
    [System.Serializable]
    public class OrbitingShapeConfig
    {
        [Tooltip("Shape type")] public ShapeType shapeType = ShapeType.Circle;

        [Tooltip("Distance from center")] [Range(0.2f, 3f)]
        public float radius = 1f;

        [Tooltip("Size of the shape")] [Range(0.05f, 1f)]
        public float size = 0.15f;

        [Tooltip("Shape color")] [ColorUsage(true, true)]
        public Color color = new Color(0.5f, 1f, 1f, 1f);

        [Tooltip("Position within the bar (in beats, e.g., 0=beat 1, 1=beat 2, 0.5=halfway between beats 1&2)")]
        [Range(0f, 8f)]
        public float rhythmPositionInBeats = 0f;

        [Tooltip("Rotation orientation in degrees")] [Range(0f, 360f)]
        public float orientation = 0f;

        [Tooltip("Enable orbital motion around the rhythm position")]
        public bool enableOrbit = true;

        [Tooltip("Orbital speed multiplier (relative to base rotation speed, e.g., 2.0 = twice as fast)")]
        [ShowIf("enableOrbit")]
        [Range(0.1f, 8f)]
        public float orbitSpeedMultiplier = 1f;

        [Tooltip("Enable pulsing animation")] public bool enablePulse = true;

        public string GetShapeLabel() => $"{shapeType} @ {rhythmPositionInBeats:F1}b (×{orbitSpeedMultiplier:F1})";
    }

    /// <summary>
    /// Available shape types for rendering
    /// </summary>
    public enum ShapeType
    {
        Circle,
        Square,
        Triangle,
        Line
    }
}
