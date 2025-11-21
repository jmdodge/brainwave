using System.Collections;
using UnityEngine;
using Sirenix.OdinInspector;

namespace Gameplay
{
    /// <summary>
    /// Procedurally generates and animates a molecule visual using simple shapes.
    /// Creates orbiting particles with glow effects that can be accelerated.
    /// </summary>
    public class MoleculeVisualizer : MonoBehaviour, IPickable
    {
        [TitleGroup("Shape Generation")]
        [Tooltip("Number of particles/atoms in the molecule")]
        [Range(3, 12)]
        [SerializeField] int particleCount = 5;

        [TitleGroup("Shape Generation")]
        [Tooltip("Radius of the molecule")]
        [Range(0.1f, 2f)]
        [SerializeField] float moleculeRadius = 0.5f;

        [TitleGroup("Shape Generation")]
        [Tooltip("Size of individual particles")]
        [Range(0.05f, 0.5f)]
        [SerializeField] float particleSize = 0.15f;

        [TitleGroup("Shape Generation")]
        [Tooltip("Pattern to arrange particles")]
        [SerializeField] MoleculePattern pattern = MoleculePattern.Ring;

        [TitleGroup("Colors")]
        [Tooltip("Base color of the molecule")]
        [ColorUsage(true, true)] // HDR color for glow
        [SerializeField] Color baseColor = new Color(0.5f, 1f, 1f, 1f);

        [TitleGroup("Colors")]
        [Tooltip("Core/center particle color")]
        [ColorUsage(true, true)]
        [SerializeField] Color coreColor = new Color(1f, 0.5f, 1f, 1f);

        [TitleGroup("Rendering")]
        [Tooltip("Sorting layer name for the particle sprites")]
        [ValueDropdown("GetSortingLayerNames")]
        [SerializeField] string sortingLayerName = "Default";

        [HideInInspector]
        [SerializeField] int sortingLayerID = 0;

        [TitleGroup("Rendering")]
        [Tooltip("Order in layer (higher values render on top)")]
        [SerializeField] int sortingOrder = 0;

        [TitleGroup("Animation")]
        [Tooltip("Base rotation speed (degrees per second)")]
        [Range(0f, 180f)]
        [SerializeField] float baseRotationSpeed = 20f;

        [TitleGroup("Animation")]
        [Tooltip("Pulse speed (oscillation frequency)")]
        [Range(0f, 5f)]
        [SerializeField] float pulseSpeed = 1f;

        [TitleGroup("Animation")]
        [Tooltip("Pulse magnitude (how much particles move)")]
        [Range(0f, 0.5f)]
        [SerializeField] float pulseMagnitude = 0.1f;

        [TitleGroup("Animation")]
        [Tooltip("Individual particle orbit speed")]
        [Range(0f, 360f)]
        [SerializeField] float orbitSpeed = 30f;

        [TitleGroup("Pickup State")]
        [Tooltip("Rotation speed multiplier when picked up")]
        [Range(1f, 10f)]
        [SerializeField] float pickupSpeedMultiplier = 3f;

        [TitleGroup("Pickup State")]
        [Tooltip("Glow intensity multiplier when picked up")]
        [Range(1f, 5f)]
        [SerializeField] float pickupGlowMultiplier = 2f;

        [TitleGroup("Debug")]
        [Button("Regenerate Molecule")]
        void RegenerateButton() => GenerateMolecule();

        GameObject[] particles;
        SpriteRenderer[] particleRenderers;
        Vector3[] particleBasePositions;
        float[] particlePhaseOffsets;
        bool isPickedUp;
        float currentRotation;

        public enum MoleculePattern
        {
            Ring,           // Particles arranged in a circle
            Orbital,        // Particles with varying orbital radii
            Cluster,        // Random cluster arrangement
            Linear,         // Linear arrangement
            DoubleBond      // Two parallel lines
        }

        void Awake()
        {
            GenerateMolecule();
        }

        void Update()
        {
            AnimateMolecule();
        }

        /// <summary>
        /// Sets whether the molecule is in "picked up" state (faster animation, more glow)
        /// </summary>
        public void SetPickedUpState(bool pickedUp)
        {
            isPickedUp = pickedUp;

            // Update glow intensity
            if (particleRenderers != null)
            {
                float glowMultiplier = pickedUp ? pickupGlowMultiplier : 1f;
                for (int i = 0; i < particleRenderers.Length; i++)
                {
                    if (particleRenderers[i] != null)
                    {
                        Color color = i == 0 && pattern != MoleculePattern.Linear ? coreColor : baseColor;
                        particleRenderers[i].color = color * glowMultiplier;
                    }
                }
            }
        }

        void GenerateMolecule()
        {
            // Clean up existing particles
            if (particles != null)
            {
                foreach (GameObject particle in particles)
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

            // Determine particle count based on pattern
            int actualParticleCount = pattern switch
            {
                MoleculePattern.Ring => particleCount,
                MoleculePattern.Orbital => particleCount,
                MoleculePattern.Cluster => particleCount,
                MoleculePattern.Linear => Mathf.Max(3, particleCount / 2),
                MoleculePattern.DoubleBond => particleCount,
                _ => particleCount
            };

            particles = new GameObject[actualParticleCount];
            particleRenderers = new SpriteRenderer[actualParticleCount];
            particleBasePositions = new Vector3[actualParticleCount];
            particlePhaseOffsets = new float[actualParticleCount];

            for (int i = 0; i < actualParticleCount; i++)
            {
                // Create particle GameObject
                GameObject particle = new GameObject($"Particle_{i}");
                particle.transform.SetParent(transform);

                // Add sprite renderer
                SpriteRenderer sr = particle.AddComponent<SpriteRenderer>();
                sr.sprite = CreateCircleSprite();

                // Apply rendering settings
                sr.sortingLayerID = sortingLayerID;
                sr.sortingOrder = sortingOrder;

                // Set color (first particle is core for some patterns)
                bool isCore = i == 0 && pattern != MoleculePattern.Linear && pattern != MoleculePattern.DoubleBond;
                sr.color = isCore ? coreColor : baseColor;

                // Set size
                float size = isCore ? particleSize * 1.5f : particleSize;
                particle.transform.localScale = Vector3.one * size;

                // Calculate position based on pattern
                Vector3 pos = CalculateParticlePosition(i, actualParticleCount);
                particle.transform.localPosition = pos;
                particleBasePositions[i] = pos;

                // Random phase offset for animation variation
                particlePhaseOffsets[i] = Random.Range(0f, Mathf.PI * 2f);

                particles[i] = particle;
                particleRenderers[i] = sr;
            }
        }

        Vector3 CalculateParticlePosition(int index, int total)
        {
            switch (pattern)
            {
                case MoleculePattern.Ring:
                    if (index == 0) return Vector3.zero; // Core particle
                    float angle = (index - 1) * (360f / (total - 1)) * Mathf.Deg2Rad;
                    return new Vector3(
                        Mathf.Cos(angle) * moleculeRadius,
                        Mathf.Sin(angle) * moleculeRadius,
                        0f);

                case MoleculePattern.Orbital:
                    if (index == 0) return Vector3.zero; // Core
                    float orbitAngle = (index - 1) * (360f / (total - 1)) * Mathf.Deg2Rad;
                    float orbitRadius = moleculeRadius * (0.5f + (index % 2) * 0.5f);
                    return new Vector3(
                        Mathf.Cos(orbitAngle) * orbitRadius,
                        Mathf.Sin(orbitAngle) * orbitRadius,
                        0f);

                case MoleculePattern.Cluster:
                    if (index == 0) return Vector3.zero;
                    return Random.insideUnitCircle * moleculeRadius;

                case MoleculePattern.Linear:
                    float spacing = (moleculeRadius * 2f) / (total - 1);
                    return new Vector3((index - total / 2f) * spacing, 0f, 0f);

                case MoleculePattern.DoubleBond:
                    int row = index % 2;
                    int col = index / 2;
                    float xSpacing = (moleculeRadius * 2f) / Mathf.Max(1, (total / 2) - 1);
                    float yOffset = particleSize * 1.5f;
                    return new Vector3(
                        (col - (total / 4f)) * xSpacing,
                        (row - 0.5f) * yOffset,
                        0f);

                default:
                    return Vector3.zero;
            }
        }

        void AnimateMolecule()
        {
            if (particles == null || particles.Length == 0) return;

            float speedMultiplier = isPickedUp ? pickupSpeedMultiplier : 1f;
            float time = Time.time;

            // Rotate entire molecule
            currentRotation += baseRotationSpeed * speedMultiplier * Time.deltaTime;
            transform.rotation = Quaternion.Euler(0f, 0f, currentRotation);

            // Animate individual particles
            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i] == null) continue;

                // Pulsing effect (radial movement)
                float pulse = Mathf.Sin(time * pulseSpeed * speedMultiplier + particlePhaseOffsets[i]) * pulseMagnitude;
                Vector3 pulseOffset = particleBasePositions[i].normalized * pulse;

                // Orbital rotation for non-core particles
                Vector3 orbitOffset = Vector3.zero;
                if (i > 0 && (pattern == MoleculePattern.Ring || pattern == MoleculePattern.Orbital))
                {
                    float orbitAngle = time * orbitSpeed * speedMultiplier * Mathf.Deg2Rad;
                    float currentAngle = Mathf.Atan2(particleBasePositions[i].y, particleBasePositions[i].x) + orbitAngle;
                    float radius = particleBasePositions[i].magnitude;
                    orbitOffset = new Vector3(Mathf.Cos(currentAngle) * radius, Mathf.Sin(currentAngle) * radius, 0f)
                        - particleBasePositions[i];
                }

                particles[i].transform.localPosition = particleBasePositions[i] + pulseOffset + orbitOffset;

                // Scale pulsing for extra visual interest
                float scalePulse = 1f + Mathf.Sin(time * pulseSpeed * 2f * speedMultiplier + particlePhaseOffsets[i]) * 0.1f;
                particles[i].transform.localScale = Vector3.one * (i == 0 ? particleSize * 1.5f : particleSize) * scalePulse;
            }
        }

        /// <summary>
        /// Creates a simple circle sprite programmatically
        /// </summary>
        Sprite CreateCircleSprite()
        {
            int resolution = 64;
            Texture2D texture = new Texture2D(resolution, resolution);
            Color[] pixels = new Color[resolution * resolution];

            Vector2 center = new Vector2(resolution / 2f, resolution / 2f);
            float radius = resolution / 2f - 2f;

            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    Vector2 pos = new Vector2(x, y);
                    float dist = Vector2.Distance(pos, center);

                    // Soft edge for glow effect
                    float alpha = 1f - Mathf.Clamp01((dist - radius + 4f) / 4f);
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

        IEnumerable GetSortingLayerNames()
        {
            return System.Array.ConvertAll(SortingLayer.layers, l => l.name);
        }

        void OnValidate()
        {
            // Sync sorting layer name to ID
            sortingLayerID = SortingLayer.NameToID(sortingLayerName);

            // Validate layer exists, reset to Default if not
            if (!System.Array.Exists(SortingLayer.layers, l => l.name == sortingLayerName))
            {
                sortingLayerName = "Default";
                sortingLayerID = 0;
            }

            if (Application.isPlaying && particles != null)
            {
                GenerateMolecule();
            }
        }
    }
}
