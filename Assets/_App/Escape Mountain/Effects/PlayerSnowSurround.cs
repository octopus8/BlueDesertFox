using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Always-on snowflake volume around the player that moves with scrolling terrain
/// (environment-relative), not glued to the camera transform.
/// </summary>
[AddComponentMenu("Ace of Ages/Player Snow Surround")]
[DefaultExecutionOrder(200)]
public class PlayerSnowSurround : MonoBehaviour
{
    [Tooltip("Center of the emission volume. If empty, uses Camera.main (XR head / Main Camera).")]
    [SerializeField] private Transform followTarget;

    [SerializeField] private Material snowMaterial;

    [Header("Emission")]
    [SerializeField] private float emissionRate = 60f;
    [SerializeField] private int maxParticles = 300;

    [Header("Volume")]
    [SerializeField] private Vector3 boxSize = new Vector3(8f, 10f, 8f);

    [Header("Motion")]
    [SerializeField] private float fallSpeed = 0.8f;
    [SerializeField] private float driftSpeed = 0.25f;
    [SerializeField] private float startSize = 0.04f;
    [SerializeField] private float startLifetime = 4.5f;

    [Header("Speed Lead")]
    [Tooltip("Emitter shifts ahead by Player Follow Object terrain-relative velocity * leadSeconds.")]
    [SerializeField] private float leadSeconds = 0.75f;
    [Tooltip("Maximum emitter lead distance in meters.")]
    [SerializeField] private float maxLeadDistance = 12f;

    private ParticleSystem _particles;
    private ParticleSystem.Particle[] _particleBuffer;
    private EntityQuery _scrollOffsetQuery;
    private EntityQuery _followObjectMotionQuery;
    private bool _hasScrollOffsetQuery;
    private bool _hasFollowObjectMotionQuery;
    private bool _hasPreviousScrollOffset;
    private float3 _previousScrollOffset;
    private bool _loggedMissingCamera;

    private void Awake()
    {
        _particles = GetComponent<ParticleSystem>();
        if (_particles == null)
            _particles = gameObject.AddComponent<ParticleSystem>();

        ConfigureParticleSystem();
        TryCreateScrollQueries();
    }

    private void OnEnable()
    {
        if (_particles == null)
            _particles = GetComponent<ParticleSystem>();

        if (_particles != null && !_particles.isPlaying)
            _particles.Play(true);

        _hasPreviousScrollOffset = false;
    }

    private void OnDestroy()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        bool worldOk = world != null && world.IsCreated;

        if (_hasScrollOffsetQuery && worldOk && _scrollOffsetQuery != default)
            _scrollOffsetQuery.Dispose();
        if (_hasFollowObjectMotionQuery && worldOk && _followObjectMotionQuery != default)
            _followObjectMotionQuery.Dispose();

        _hasScrollOffsetQuery = false;
        _hasFollowObjectMotionQuery = false;
    }

    private void LateUpdate()
    {
        Transform target = ResolveFollowTarget();
        if (target != null)
        {
            // Emission volume stays near the view, shifted ahead along travel at higher speeds.
            // Particles themselves live in world/terrain space.
            Vector3 emitterPosition = target.position + GetSpeedLeadOffset();
            transform.SetPositionAndRotation(emitterPosition, Quaternion.identity);
        }

        ApplyTerrainScrollToParticles();
    }

    private Transform ResolveFollowTarget()
    {
        if (followTarget != null)
            return followTarget;

        Camera main = Camera.main;
        if (main != null)
        {
            followTarget = main.transform;
            return followTarget;
        }

        if (!_loggedMissingCamera)
        {
            _loggedMissingCamera = true;
            Debug.LogWarning("[PlayerSnowSurround] No follow target and Camera.main is missing; snow will emit at origin until a camera appears.", this);
        }

        return null;
    }

    private void TryCreateScrollQueries()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        if (!_hasScrollOffsetQuery)
        {
            _scrollOffsetQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<ScrollOffset>());
            _hasScrollOffsetQuery = true;
        }

        if (!_hasFollowObjectMotionQuery)
        {
            _followObjectMotionQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PlayerFollowObjectTag>(),
                ComponentType.ReadOnly<PlayerFollowObjectMotionState>());
            _hasFollowObjectMotionQuery = true;
        }
    }

    private bool TryGetScrollOffset(out float3 accumulatedOffset)
    {
        accumulatedOffset = float3.zero;

        if (!_hasScrollOffsetQuery || _scrollOffsetQuery == default)
        {
            TryCreateScrollQueries();
            if (!_hasScrollOffsetQuery)
                return false;
        }

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated || _scrollOffsetQuery.IsEmptyIgnoreFilter)
            return false;

        accumulatedOffset = _scrollOffsetQuery.GetSingleton<ScrollOffset>().accumulatedOffset;
        return true;
    }

    private bool TryGetFollowObjectTravelVelocity(out float3 terrainRelativeVelocity)
    {
        terrainRelativeVelocity = float3.zero;

        if (!_hasFollowObjectMotionQuery || _followObjectMotionQuery == default)
        {
            TryCreateScrollQueries();
            if (!_hasFollowObjectMotionQuery)
                return false;
        }

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated || _followObjectMotionQuery.IsEmptyIgnoreFilter)
            return false;

        terrainRelativeVelocity = _followObjectMotionQuery
            .GetSingleton<PlayerFollowObjectMotionState>()
            .terrainRelativeVelocity;
        return true;
    }

    /// <summary>
    /// Offsets the emitter ahead along the Player Follow Object's terrain-relative travel
    /// velocity so flakes seed in the flight path through the environment.
    /// </summary>
    private Vector3 GetSpeedLeadOffset()
    {
        if (leadSeconds <= 0f || maxLeadDistance <= 0f)
            return Vector3.zero;

        if (!TryGetFollowObjectTravelVelocity(out float3 travelVelocity))
            return Vector3.zero;

        float3 lead = travelVelocity * leadSeconds;
        float leadSq = math.lengthsq(lead);
        if (leadSq < 1e-8f)
            return Vector3.zero;

        float maxLead = maxLeadDistance;
        float maxLeadSq = maxLead * maxLead;
        if (leadSq > maxLeadSq)
            lead *= maxLead / math.sqrt(leadSq);

        return new Vector3(lead.x, lead.y, lead.z);
    }

    /// <summary>
    /// Moves live flakes by the same world delta as terrain tiles
    /// (<c>-ΔScrollOffset</c>), so snow stays locked to the environment.
    /// </summary>
    private void ApplyTerrainScrollToParticles()
    {
        if (_particles == null || !TryGetScrollOffset(out float3 scrollOffset))
            return;

        if (!_hasPreviousScrollOffset)
        {
            _previousScrollOffset = scrollOffset;
            _hasPreviousScrollOffset = true;
            return;
        }

        float3 scrollDelta = scrollOffset - _previousScrollOffset;
        _previousScrollOffset = scrollOffset;

        if (math.lengthsq(scrollDelta) < 1e-12f)
            return;

        Vector3 terrainMotion = new Vector3(-scrollDelta.x, -scrollDelta.y, -scrollDelta.z);

        int capacity = _particles.main.maxParticles;
        if (_particleBuffer == null || _particleBuffer.Length < capacity)
            _particleBuffer = new ParticleSystem.Particle[capacity];

        int count = _particles.GetParticles(_particleBuffer);
        if (count <= 0)
            return;

        for (int i = 0; i < count; i++)
            _particleBuffer[i].position += terrainMotion;

        _particles.SetParticles(_particleBuffer, count);
    }

    private void ConfigureParticleSystem()
    {
        // Duration/prewarm cannot be changed while playing (AddComponent defaults playOnAwake).
        _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = _particles.main;
        main.playOnAwake = true;
        main.loop = true;
        main.prewarm = true;
        main.duration = 5f;
        main.startLifetime = startLifetime;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(startSize * 0.6f, startSize * 1.4f);
        main.startColor = new Color(0.95f, 0.97f, 1f, 0.85f);
        main.maxParticles = maxParticles;
        // World space: flakes stay in the environment; emitter only places new ones near the player.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 0f;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

        var emission = _particles.emission;
        emission.enabled = true;
        emission.rateOverTime = emissionRate;

        var shape = _particles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = boxSize;
        shape.position = Vector3.zero;
        shape.rotation = Vector3.zero;

        var velocity = _particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.x = new ParticleSystem.MinMaxCurve(-driftSpeed, driftSpeed);
        velocity.y = new ParticleSystem.MinMaxCurve(-fallSpeed * 1.2f, -fallSpeed * 0.6f);
        velocity.z = new ParticleSystem.MinMaxCurve(-driftSpeed, driftSpeed);

        var noise = _particles.noise;
        noise.enabled = true;
        noise.strength = 0.15f;
        noise.frequency = 0.4f;
        noise.scrollSpeed = 0.2f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        var colorOverLifetime = _particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(0f, 0f),
                new GradientAlphaKey(0.9f, 0.15f),
                new GradientAlphaKey(0.9f, 0.8f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;

        var renderer = _particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        renderer.minParticleSize = 0f;
        renderer.maxParticleSize = 0.15f;
        renderer.allowRoll = true;
        renderer.enableGPUInstancing = true;

        if (snowMaterial != null)
            renderer.sharedMaterial = snowMaterial;

        _particles.Play(true);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        emissionRate = Mathf.Max(0f, emissionRate);
        maxParticles = Mathf.Max(1, maxParticles);
        fallSpeed = Mathf.Max(0f, fallSpeed);
        driftSpeed = Mathf.Max(0f, driftSpeed);
        startSize = Mathf.Max(0.001f, startSize);
        startLifetime = Mathf.Max(0.1f, startLifetime);
        leadSeconds = Mathf.Max(0f, leadSeconds);
        maxLeadDistance = Mathf.Max(0f, maxLeadDistance);
        boxSize = new Vector3(
            Mathf.Max(0.1f, boxSize.x),
            Mathf.Max(0.1f, boxSize.y),
            Mathf.Max(0.1f, boxSize.z));

        if (!Application.isPlaying)
            return;

        if (_particles == null)
            _particles = GetComponent<ParticleSystem>();
        if (_particles != null)
            ConfigureParticleSystem();
    }
#endif
}
