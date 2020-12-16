using UnityEngine;
using System.Collections.Generic;
using UnityEngine.VFX;

public class PhysarumManager : MonoBehaviour
{
    [Header("Behaviour ID")]
    public string ID;

    [Header("Physarum Material")]
    public Material physarumMaterial;

    [Header("Compute shader")]
    public ComputeShader shader;

    [Header("VFX")]
    public bool useVFX = false;
    public VisualEffect vfx;
    public int vfxTextureSize;

    [Header("Updates")]
    [Range(0, 10)] public int updatesPerFrame = 1;

    [Header("Trail Parameters")]
    public Vector2Int trailResolution = Vector2Int.one * 2048;
    public Vector2 trailSize = Vector2.one;
    [Range(0f, 1f)] public float decay = 0.002f;

    [Header("Stimuli Parameters")]
    public bool stimuliActive = false;
    public Texture stimuli;
    public float stimuliIntensity = 0.1f;
    public bool colorFromStimuli = false;

    [Header("Particles Parameters")]
    public bool synchronizeSensorAndRotation = false;
    [Range(-180f, 180f)] public float sensorAngleDegrees = 45f; 	//in degrees
    [Range(-180f, 180f)] public float rotationAngleDegrees = 45f;//in degrees
    [Range(0f, 1f)] public float sensorOffsetDistance = 0.01f;
    [Range(0f, 1f)] public float stepSize = 0.001f;
    public Vector2 lifetimeMinMax = Vector2.one;
    [Range(-180f, 180f)] public float gravityAngle;
    public float gravityStrength = 0;
    [Range(0, 360f)] public float directionAngle;
    public float directionStrength = 0;

    [Header("Debug")]
    public bool debugParticles = false;

    private List<PhysarumEmitter> _emittersList;

    private float sensorAngle; 				//in radians
    private float rotationAngle;   			//in radians
    private RenderTexture trail;
    private RenderTexture RWStimuli;
    private RenderTexture particleTexture;
    private int initParticlesKernel, spawnParticlesKernel, moveParticlesKernel, updateTrailKernel, cleanParticleTexture, writeParticleTexture, updateParticleMap;
    private ComputeBuffer[] particleBuffers;

    private static int _groupCount = 32;       // Group size has to be same with the compute shader group size

    struct Particle
    {
        public Vector2 point;
        public float angle;
		public Vector4 color;
        public float age;
        public float lifetime;
        public float stimuliIntensity;

        public Particle(Vector2 pos, float angle, Vector4 color, float age, float lifetime, float stimuliIntensity)
        {
            point = pos;
            this.angle = angle;
			this.color = color;
            this.age = age;
            this.lifetime = lifetime;
            this.stimuliIntensity = stimuliIntensity;
        }
    };

    private const int _particleStride = 10 * sizeof(float);

    private RenderTexture _particlePositionMap;

    private bool _initialized = false;

	#region MonoBehaviour Functions

	void OnValidate()
    {
        if (trailResolution.x < _groupCount) trailResolution.x = _groupCount;
        if (trailResolution.y < _groupCount) trailResolution.y = _groupCount;
    }

    void OnEnable()
    {
        if (!_initialized)
            Initialize();
    }

    void Update() {

        if (synchronizeSensorAndRotation)
            rotationAngleDegrees = sensorAngleDegrees;

        for (int i = 0; i < updatesPerFrame; i++) {
            UpdateParticles();
            UpdateTrail();

        }

        if (useVFX) {
            if (_emittersList.Count > 0)
                UpdateVFX(0);
        }

        if (debugParticles)
            UpdateParticleTexture();
    }

    void OnDisable() {
        CleanUp();
    }

	#endregion

	#region Initializations

	void Initialize() {

        if (shader == null) {
            Debug.LogError("PhysarumSurface shader has to be assigned for PhysarumBehaviour to work.");
            this.enabled = false;
            return;
        }

        initParticlesKernel = shader.FindKernel("InitParticles");
        spawnParticlesKernel = shader.FindKernel("SpawnParticles");
        moveParticlesKernel = shader.FindKernel("MoveParticles");
        updateTrailKernel = shader.FindKernel("UpdateTrail");
        cleanParticleTexture = shader.FindKernel("CleanParticleTexture");
        writeParticleTexture = shader.FindKernel("WriteParticleTexture");
        updateParticleMap = shader.FindKernel("UpdateParticleMap");

        InitializeTrail();
        InitializeStimuli();
        InitializeMaterial();
        InitializeEmitters();
        InitializeParticlesBuffer();

        if (debugParticles)
            InitializeParticleTexture();

        if(useVFX)
            InitializeVFX();

        _initialized = true;
    }

    void InitializeParticles(int index) {
        // allocate memory for the particles
        if (_emittersList[index].capacity > _groupCount * 65535) {
            _emittersList[index].capacity = _groupCount * 65535;
            Debug.Log("Reduced emitter " + _emittersList[index].name + " capacity to maximum capacity for " + _groupCount + " threadGroups (" + _groupCount * 65535 + ").");
        }

        Particle[] data = new Particle[_emittersList[index].capacity];
        particleBuffers[index] = new ComputeBuffer(data.Length, _particleStride);
        particleBuffers[index].SetData(data);

        shader.SetVector("_LifetimeMinMax", lifetimeMinMax);

        shader.SetInt("_EmitterCapacity", _emittersList[index].capacity);
        shader.SetVector("_EmitterPosition", _emittersList[index].position);
        shader.SetVector("_EmitterPreviousPosition", _emittersList[index].previousPosition);
        shader.SetFloat("_EmitterRadius", _emittersList[index].radius);
        shader.SetFloat("_EmitterSpawnRate", _emittersList[index].spawnRate);
        shader.SetVector("_EmitterMainColor", _emittersList[index].mainColor);
        shader.SetVector("_EmitterSecondaryColor", _emittersList[index].secondaryColor);
        shader.SetFloat("_EmitterSecondaryColorProbability", _emittersList[index].secondaryColorProbability);
        shader.SetBuffer(initParticlesKernel, "_ParticleBuffer", particleBuffers[index]);

        shader.Dispatch(initParticlesKernel, _emittersList[index].capacity / _groupCount, 1, 1);
    }

    void InitializeTrail()
    {
        trail = new RenderTexture(trailResolution.x, trailResolution.y, 24);
        trail.enableRandomWrite = true;
        trail.Create();

        shader.SetVector("_TrailDimension", new Vector2(trailResolution.x, trailResolution.y));
        shader.SetVector("_TrailSize", trailSize);

        shader.SetTexture(moveParticlesKernel, "_TrailBuffer", trail);
        shader.SetTexture(updateTrailKernel, "_TrailBuffer", trail);
    }

	void InitializeParticleTexture() {
		particleTexture = new RenderTexture(trailResolution.x, trailResolution.y, 24);
		particleTexture.enableRandomWrite = true;
		particleTexture.Create();

		var rend = GetComponent<Renderer>();
		rend.material.mainTexture = particleTexture;

		shader.SetTexture(cleanParticleTexture, "_ParticleTexture", particleTexture);
		shader.SetTexture(writeParticleTexture, "_ParticleTexture", particleTexture);
	}

	void InitializeStimuli()
    {
        if (stimuli == null)
        {
            RWStimuli = new RenderTexture(trailResolution.x, trailResolution.y, 24);
            RWStimuli.enableRandomWrite = true;
            RWStimuli.Create();
        }
        else
        {
            RWStimuli = new RenderTexture(stimuli.width, stimuli.height, 0);
            RWStimuli.enableRandomWrite = true;
            Graphics.Blit(stimuli, RWStimuli);
        }
        shader.SetBool("_StimuliActive", stimuliActive);
        shader.SetTexture(moveParticlesKernel, "_Stimuli", RWStimuli);
        shader.SetVector("_StimuliDimension", new Vector2(RWStimuli.width, RWStimuli.height));
    }

    void InitializeParticlesBuffer() {

        ReleaseParticlesBuffer();

        particleBuffers = new ComputeBuffer[_emittersList.Count];

        for (int i = 0; i < particleBuffers.Length; i++)
            InitializeParticles(i);
    }

    void InitializeMaterial() {

        physarumMaterial.SetTexture("PhysarumTrail", trail);
        physarumMaterial.SetTexture("PhysarumStimuli", stimuli);
	}

    void InitializeVFX() {

        _particlePositionMap = new RenderTexture(vfxTextureSize, vfxTextureSize, 1, RenderTextureFormat.ARGBFloat);
        _particlePositionMap.enableRandomWrite = true;
        _particlePositionMap.Create();

        shader.SetVector("_ParticlePositionMapSize", new Vector2(_particlePositionMap.width, _particlePositionMap.height));
        shader.SetTexture(updateParticleMap, "_ParticlePositionMap", _particlePositionMap);

        vfx.SetTexture("ParticlePosition", _particlePositionMap);
        vfx.SetFloat("TextureSize", vfxTextureSize);
    }

	#endregion

	#region Releases

    void CleanUp() {

        ReleaseParticlesBuffer();

        if(useVFX)
            ReleaseVFX();
    }

	void ReleaseParticlesBuffer() {

        if (particleBuffers == null)
            return;

        foreach (var particleBuffer in particleBuffers) {
            if (particleBuffer != null)
                particleBuffer.Release();
        }
    }

    void ReleaseVFX() {

        _particlePositionMap.Release();
	}

    #endregion

    #region Updates

    void UpdateParticles()
    {
        for (int i = 0; i < _emittersList.Count; i++) {
            SpawnParticles(i);
            MoveParticles(i);
        }
    }

    void SpawnParticles(int index) {

        shader.SetFloat("_DeltaTime", Time.deltaTime);
        shader.SetFloat("_AbsoluteTime", Time.time);

        shader.SetInt("_EmitterCapacity", _emittersList[index].capacity);
        shader.SetVector("_EmitterPosition", _emittersList[index].position);
        shader.SetVector("_EmitterPreviousPosition", _emittersList[index].previousPosition);
        shader.SetFloat("_EmitterRadius", _emittersList[index].radius);
        shader.SetFloat("_EmitterSpawnRate", _emittersList[index].spawnRate);
        shader.SetVector("_EmitterMainColor", _emittersList[index].mainColor);
        shader.SetVector("_EmitterSecondaryColor", _emittersList[index].secondaryColor);
        shader.SetFloat("_EmitterSecondaryColorProbability", _emittersList[index].secondaryColorProbability);

        shader.SetBuffer(spawnParticlesKernel, "_ParticleBuffer", particleBuffers[index]);

        shader.Dispatch(spawnParticlesKernel, _emittersList[index].capacity / _groupCount, 1, 1);
    }

    void MoveParticles(int index) {

        sensorAngle = sensorAngleDegrees * 0.0174533f;
        rotationAngle = rotationAngleDegrees * 0.0174533f;

        shader.SetFloat("_SensorAngle", sensorAngle);
        shader.SetFloat("_RotationAngle", rotationAngle);
        shader.SetFloat("_SensorOffsetDistance", sensorOffsetDistance);
        shader.SetFloat("_StepSize", stepSize * Time.deltaTime);
        shader.SetBool("_StimuliActive", stimuliActive);
        shader.SetFloat("_StimuliIntensity", stimuliIntensity);
        shader.SetBool("_StimuliToColor", colorFromStimuli);
        shader.SetFloat("_DirectionAngle", directionAngle * Mathf.Deg2Rad);
        shader.SetFloat("_DirectionStrength", directionStrength);

        shader.SetBuffer(moveParticlesKernel, "_ParticleBuffer", particleBuffers[index]);

        shader.Dispatch(moveParticlesKernel, _emittersList[index].capacity / _groupCount, 1, 1);
    }

    void UpdateTrail()
    {
        shader.SetFloat("_Decay", decay);
        shader.SetVector("_Gravity", new Vector2(Mathf.Cos(gravityAngle * Mathf.Deg2Rad), Mathf.Sin(gravityAngle * Mathf.Deg2Rad)) * gravityStrength);
        shader.Dispatch(updateTrailKernel, trailResolution.x / _groupCount, trailResolution.y / _groupCount, 1);
    }

    void UpdateParticleTexture() {

        shader.Dispatch(cleanParticleTexture, trailResolution.x / _groupCount, trailResolution.y / _groupCount, 1);

        for (int i = 0; i < particleBuffers.Length; i++) {
            shader.SetBuffer(writeParticleTexture, "_ParticleBuffer", particleBuffers[i]);
            shader.Dispatch(writeParticleTexture, _emittersList[i].capacity / _groupCount, 1, 1);
        }
    }

    void UpdateVFX(int index) {

        shader.SetBuffer(updateParticleMap, "_ParticleBuffer", particleBuffers[index]);
        shader.Dispatch(updateParticleMap, _emittersList[index].capacity / _groupCount, 1, 1);

    }

	#endregion

	#region Emitters

	void InitializeEmitters() {

        CreateEmittersList();
    }

    void CreateEmittersList() {

        _emittersList = new List<PhysarumEmitter>();
    }

    public void AddEmitter(PhysarumEmitter emitter) {

        if (!_initialized)
            Initialize();

        _emittersList.Add(emitter);

        InitializeParticlesBuffer();
    }

    public void RemoveEmitter(PhysarumEmitter emitter) {

        _emittersList.Remove(emitter);

        InitializeParticlesBuffer();
    }

    #endregion
}
