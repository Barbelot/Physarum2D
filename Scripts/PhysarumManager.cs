using UnityEngine;
using System.Collections.Generic;

public class PhysarumManager : MonoBehaviour
{
    [Header("Behaviour ID")]
    public string ID;

    [Header("Physarum Material")]
    public Material physarumMaterial;

    [Header("Compute shader")]
    public ComputeShader shader;

    [Header("Initial Values")]
    public Vector2Int trailResolution = Vector2Int.one * 2048;
    public Vector2 trailSize = Vector2.one;
    public bool stimuliActive = true;
    public Texture stimuli;

    [Header("Updates")]
    [Range(0, 10)] public int updatesPerFrame = 1;

    [Header("Trail Parameters")]
    [Range(0f, 1f)] public float decay = 0.002f;
    public float stimuliIntensity = 0.1f;

    [Header("Particles Parameters")]
    public bool synchronizeSensorAndRotation = false;
    [Range(-180f, 180f)] public float sensorAngleDegrees = 45f; 	//in degrees
    [Range(-180f, 180f)] public float rotationAngleDegrees = 45f;//in degrees
    [Range(0f, 1f)] public float sensorOffsetDistance = 0.01f;
    [Range(0f, 1f)] public float stepSize = 0.001f;
    public Vector2 lifetimeMinMax = Vector2.one;

    [Header("Debug")]
    public bool debugParticles = false;

    private List<PhysarumEmitter> _emittersList;

    private float sensorAngle; 				//in radians
    private float rotationAngle;   			//in radians
    private RenderTexture trail;
    private RenderTexture RWStimuli;
    private RenderTexture particleTexture;
    private int initParticlesKernel, spawnParticlesKernel, moveParticlesKernel, updateTrailKernel, cleanParticleTexture, writeParticleTexture;
    private ComputeBuffer[] particleBuffers;

    private static int _groupCount = 32;       // Group size has to be same with the compute shader group size

    struct Particle
    {
        public Vector2 point;
        public float angle;
		public Vector4 color;
        public float age;
        public float lifetime;

        public Particle(Vector2 pos, float angle, Vector4 color, float age, float lifetime)
        {
            point = pos;
            this.angle = angle;
			this.color = color;
            this.age = age;
            this.lifetime = lifetime;
        }
    };

    private const int _particleStride = 9 * sizeof(float);

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

        if (debugParticles)
            UpdateParticleTexture();
    }

    void OnDisable() {
        ReleaseParticlesBuffer();
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

        InitializeTrail();
        InitializeStimuli();
        InitializeMaterial();
        InitializeEmitters();
        InitializeParticlesBuffer();

        if (debugParticles)
            InitializeParticleTexture();

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

	#endregion

	#region Releases

	void ReleaseParticlesBuffer() {

        if (particleBuffers == null)
            return;

        foreach (var particleBuffer in particleBuffers) {
            if (particleBuffer != null)
                particleBuffer.Release();
        }
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
        shader.SetFloat("_StimuliIntensity", stimuliIntensity);

        shader.SetBuffer(moveParticlesKernel, "_ParticleBuffer", particleBuffers[index]);

        shader.Dispatch(moveParticlesKernel, _emittersList[index].capacity / _groupCount, 1, 1);
    }

    void UpdateTrail()
    {
        shader.SetFloat("_Decay", decay);
        shader.Dispatch(updateTrailKernel, trailResolution.x / _groupCount, trailResolution.y / _groupCount, 1);
    }

    void UpdateParticleTexture() {

        shader.Dispatch(cleanParticleTexture, trailResolution.x / _groupCount, trailResolution.y / _groupCount, 1);

        for (int i = 0; i < particleBuffers.Length; i++) {
            shader.SetBuffer(writeParticleTexture, "_ParticleBuffer", particleBuffers[i]);
            shader.Dispatch(writeParticleTexture, _emittersList[i].capacity / _groupCount, 1, 1);
        }
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
