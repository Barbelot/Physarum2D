using UnityEngine;
using System.Collections.Generic;
using UnityEngine.VFX;

public class PhysarumManager : MonoBehaviour
{
    [Header("Behaviour ID")]
    public string ID = "Main";

	[Header("Compute shader")]
    public ComputeShader shader;

    [Header("Updates")]
    [Range(0, 20)] public int updatesPerFrame = 3;

    [Header("Trail Settings")]
    public Vector2Int trailResolution = Vector2Int.one * 2048;
    public Vector2 trailSize = Vector2.one;
    [Range(0f, 1f)] public float decay = 0.002f;
    [Range(0f, 2f)] public float trailRepulsionLimit = 0.5f;

    [Header("Fluid")]
    public RenderTexture fluidTexture;
    public float fluidStrength;

    public bool advectTrailFromFluid = false;
    public float trailAdvectionFromFluid = 0;

    [Header("Velocities Settings")]
    public bool useVelocities = false;
    [Range(0f, 1f)] public float velocitiesDecay = 0.002f;

    [Header("Influence Settings")]
    public bool useInfluenceMap = false;
    public Texture influenceMap;
    public float influenceStrength = 1;

    [Header("Stimuli Settings")]
    public bool useStimuli = false;
    public Texture stimuli;
    public float stimuliIntensity = 0.1f;
    public bool colorFromStimuli = false;

    [Header("Particles Settings")]
    [Range(-180f, 180f)] public float gravityAngle;
    public float gravityStrength = 0;
    [Range(0, 360f)] public float directionAngle;
    public float directionStrength = 0;

    [Header("Debug")]
    public bool debugParticles = false;
    public bool test = false;

    public RenderTexture trail { 
        get {
            if (!_initialized)
                Initialize();

            return _trailRead; 
        }
    }

    private List<PhysarumEmitter> _emittersList;

    private RenderTexture _trailRead;
    private RenderTexture _trailWrite;
    private RenderTexture _velocities;
    private RenderTexture _RWInfluenceMap;
    private RenderTexture _particleTexture;
    private RenderTexture _defaultTexture;
    private int _initParticlesKernel, _spawnParticlesKernel, _moveParticlesKernel, _updateTrailKernel, _advectTrailKernel, _cleanParticleTexture, _writeParticleTexture, _updateParticleMap, _updateVelocitiesKernel;
    private List<ComputeBuffer> _particleBuffers;

    private const int _groupCount = 32;       // Group size has to be same with the compute shader group size

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

    private const int _gradientLength = 8;
    private float _gradientStepSize;
    private Vector4[] _colorOverLife;

    private int _groupsCountX;
    private int _groupsCountY;

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


        //int updates = Mathf.FloorToInt(updatesPerSecond * Time.deltaTime);
        //updates = Mathf.Min(updates, maxUpdatedPerFrames);
        //if(updates == 0) {
        //    updates = Random.value < updatesPerSecond * Time.deltaTime ? 1 : 0; 
        //}
        //Debug.Log("Doing " + updates + " updates");

        for (int i = 0; i < updatesPerFrame; i++) {
            UpdateStimuli();
            UpdateParticles();
            UpdateTrail();

			if (advectTrailFromFluid) {
                if (trailAdvectionFromFluid != 0)
                    AdvectTrail();
			}

            if(useVelocities)
                UpdateVelocities();
        }

        if (debugParticles)
            UpdateParticleTexture();
    }

    void OnDisable() {

        //if(_initialized) CleanUp();
    }

	#endregion

	#region Initializations

	void Initialize() {

        if (shader == null) {
            Debug.LogError("PhysarumSurface shader has to be assigned for PhysarumBehaviour to work.");
            this.enabled = false;
            return;
        }

        _groupsCountX = Mathf.CeilToInt((float)trailResolution.x / _groupCount);
        _groupsCountY = Mathf.CeilToInt((float)trailResolution.y / _groupCount);

        _initParticlesKernel = shader.FindKernel("InitParticles");
        _spawnParticlesKernel = shader.FindKernel("SpawnParticles");
        _moveParticlesKernel = shader.FindKernel("MoveParticles");
        _updateTrailKernel = shader.FindKernel("UpdateTrail");
        _advectTrailKernel = shader.FindKernel("AdvectTrail");
        _updateVelocitiesKernel = shader.FindKernel("UpdateVelocities");
        _cleanParticleTexture = shader.FindKernel("CleanParticleTexture");
        _writeParticleTexture = shader.FindKernel("WriteParticleTexture");
        _updateParticleMap = shader.FindKernel("UpdateParticleMap");

        InitializeDefaultTexture();
        InitializeTrail();
        InitializeVelocities();
        InitializeStimuli();
        InitializeInfluenceMap();
        InitializeEmitters();
        InitializeParticlesBuffer();

        if (debugParticles)
            InitializeParticleTexture();

        InitializeFluid();

        _initialized = true;
    }

    void InitializeDefaultTexture() {
        _defaultTexture = new RenderTexture(16, 16, 0);
        _defaultTexture.enableRandomWrite = true;
        _defaultTexture.Create();
	}

    void InitializeParticles(PhysarumEmitter emitter) {
        // allocate memory for the particles
        if (emitter.capacity > _groupCount * 65535) {
            emitter.capacity = _groupCount * 65535;
            Debug.Log("Reduced emitter " + emitter.name + " capacity to maximum capacity for " + _groupCount + " threadGroups (" + _groupCount * 65535 + ").");
        }

        Particle[] data = new Particle[emitter.capacity];
        ComputeBuffer particleBuffer = new ComputeBuffer(data.Length, _particleStride);
        particleBuffer.SetData(data);
        _particleBuffers.Add(particleBuffer);

        shader.SetVector("_EmitterLifetimeMinMax", emitter.lifetimeMinMax);
        shader.SetInt("_EmitterCapacity", emitter.capacity);
        shader.SetVector("_EmitterPosition", emitter.position);
        shader.SetVector("_EmitterPreviousPosition", emitter.previousPosition);
        shader.SetFloat("_EmitterRadius", emitter.radius);
        shader.SetFloat("_EmitterRadiusWidth", emitter.radiusWidth);
        shader.SetFloat("_EmitterArcLength", emitter.arcLength * Mathf.PI * 2);
        shader.SetFloat("_EmitterArcOffset", emitter.arcOffset * Mathf.PI * 2);
        shader.SetFloat("_EmitterArcFeathering", emitter.arcFeathering);
        shader.SetFloat("_EmitterSpawnRate", emitter.spawnRate);
        shader.SetFloat("_EmitterColorIntensity", emitter.colorIntensity);
        shader.SetVector("_EmitterMainColor", emitter.mainColor);
        shader.SetVector("_EmitterSecondaryColor", emitter.secondaryColor);
        shader.SetFloat("_EmitterSecondaryColorProbability", emitter.secondaryColorProbability);
        shader.SetBuffer(_initParticlesKernel, "_ParticleBuffer", particleBuffer);

        shader.Dispatch(_initParticlesKernel, Mathf.CeilToInt((float)emitter.capacity / _groupCount), 1, 1);
    }

    void InitializeTrail()
    {
        _trailRead = new RenderTexture(trailResolution.x, trailResolution.y, 0, RenderTextureFormat.ARGBFloat);
        _trailRead.enableRandomWrite = true;
        _trailRead.Create();

        _trailWrite = new RenderTexture(trailResolution.x, trailResolution.y, 0, RenderTextureFormat.ARGBFloat);
        _trailWrite.enableRandomWrite = true;
        _trailWrite.Create();

        shader.SetVector("_TrailResolution", new Vector2(trailResolution.x, trailResolution.y));
        shader.SetVector("_TrailSize", trailSize);

        shader.SetTexture(_moveParticlesKernel, "_TrailRead", _trailRead);
        shader.SetTexture(_moveParticlesKernel, "_TrailWrite", _trailWrite);
        shader.SetTexture(_updateTrailKernel, "_TrailRead", _trailRead);
        shader.SetTexture(_updateTrailKernel, "_TrailWrite", _trailWrite);
        shader.SetTexture(_advectTrailKernel, "_TrailRead", _trailRead);
        shader.SetTexture(_advectTrailKernel, "_TrailWrite", _trailWrite);
    }

    void InitializeVelocities() {
        _velocities = new RenderTexture(trailResolution.x, trailResolution.y, 0);
        _velocities.enableRandomWrite = true;
        _velocities.Create();

        shader.SetTexture(_moveParticlesKernel, "_VelocitiesBuffer", _velocities);
        shader.SetTexture(_updateVelocitiesKernel, "_VelocitiesBuffer", _velocities);
    }

    void InitializeParticleTexture() {
		_particleTexture = new RenderTexture(trailResolution.x, trailResolution.y, 0);
		_particleTexture.enableRandomWrite = true;
		_particleTexture.Create();

		var rend = GetComponent<Renderer>();
		rend.material.mainTexture = _particleTexture;

		shader.SetTexture(_cleanParticleTexture, "_ParticleTexture", _particleTexture);
		shader.SetTexture(_writeParticleTexture, "_ParticleTexture", _particleTexture);
	}

	public void InitializeStimuli()
    {
		if (stimuli == null) {
            stimuli = _defaultTexture;
		}

		shader.SetBool("_StimuliActive", useStimuli);
		shader.SetTexture(_moveParticlesKernel, "_Stimuli", stimuli);
        shader.SetTexture(_spawnParticlesKernel, "_Stimuli", stimuli);
		shader.SetVector("_StimuliResolution", new Vector2(stimuli.width, stimuli.height));
	}

    public void InitializeInfluenceMap() {

        if (_RWInfluenceMap != null)
            _RWInfluenceMap.Release();

        if (influenceMap == null) {
            _RWInfluenceMap = new RenderTexture(trailResolution.x, trailResolution.y, 0);
            _RWInfluenceMap.enableRandomWrite = true;
            _RWInfluenceMap.Create();
        } else {
            _RWInfluenceMap = new RenderTexture(influenceMap.width, influenceMap.height, 0);
            _RWInfluenceMap.enableRandomWrite = true;
            Graphics.Blit(influenceMap, _RWInfluenceMap);
        }

        shader.SetTexture(_moveParticlesKernel, "_InfluenceMap", _RWInfluenceMap);
    }

    public void InitializeFluid() {

        shader.SetTexture(_moveParticlesKernel, "_FluidTexture", fluidTexture ? fluidTexture : _defaultTexture);
        shader.SetTexture(_advectTrailKernel, "_FluidTexture", fluidTexture ? fluidTexture : _defaultTexture);
        shader.SetVector("_FluidResolution", fluidTexture ? new Vector2(fluidTexture.width, fluidTexture.height) : new Vector2(_defaultTexture.width, _defaultTexture.height));
	}

    void InitializeParticlesBuffer() {

        ReleaseParticlesBuffer();

        _particleBuffers = new List<ComputeBuffer>();
    }

	#endregion

	#region Releases

    void CleanUp() {

        _emittersList.Clear();
        ReleaseParticlesBuffer();

        _defaultTexture.Release();
        _trailRead.Release();
        _trailWrite.Release();
        _velocities.Release();
        _RWInfluenceMap.Release();

        if (_particleTexture)
            _particleTexture.Release();

        _initialized = false;
    }

	void ReleaseParticlesBuffer() {

        if (_particleBuffers == null)
            return;

        foreach (var particleBuffer in _particleBuffers) {
            if (particleBuffer != null)
                particleBuffer.Release();
        }

        _particleBuffers.Clear();
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

        shader.SetVector("_EmitterLifetimeMinMax", _emittersList[index].lifetimeMinMax);
        shader.SetInt("_EmitterCapacity", _emittersList[index].capacity);
        shader.SetVector("_EmitterPosition", _emittersList[index].position);
        shader.SetVector("_EmitterPreviousPosition", _emittersList[index].previousPosition);
        shader.SetFloat("_EmitterRadius", _emittersList[index].radius);
        shader.SetFloat("_EmitterRadiusWidth", _emittersList[index].radiusWidth);
        shader.SetFloat("_EmitterArcLength", _emittersList[index].arcLength * Mathf.PI * 2);
        shader.SetFloat("_EmitterArcOffset", _emittersList[index].arcOffset * Mathf.PI * 2);
        shader.SetFloat("_EmitterArcFeathering", _emittersList[index].arcFeathering);
        shader.SetFloat("_EmitterSpawnRate", _emittersList[index].spawnRate);
        shader.SetFloat("_EmitterColorIntensity", _emittersList[index].colorIntensity);
        shader.SetVector("_EmitterMainColor", _emittersList[index].mainColor);
        shader.SetVector("_EmitterSecondaryColor", _emittersList[index].secondaryColor);
        shader.SetFloat("_EmitterSecondaryColorProbability", _emittersList[index].secondaryColorProbability);
        shader.SetBool("_EmitterUseColorOverLife", _emittersList[index].useColorOverLife);
        shader.SetBool("_EmitterSpawnInStimuliOnly", _emittersList[index].spawnInStimuliOnly);

        if (_emittersList[index].useColorOverLife) {
            UpdateColorOverLife(index);
            shader.SetVectorArray("_EmitterColorOverLife", _colorOverLife);
        }

        shader.SetBuffer(_spawnParticlesKernel, "_ParticleBuffer", _particleBuffers[index]);

        shader.Dispatch(_spawnParticlesKernel, Mathf.CeilToInt((float)_emittersList[index].capacity / _groupCount), 1, 1);
    }

    void MoveParticles(int index) {

        shader.SetFloat("_EmitterSensorAngle", _emittersList[index].sensorAngleDegrees * Mathf.Deg2Rad);
        //shader.SetFloat("_RotationAngle", rotationAngle);
        shader.SetFloat("_EmitterSensorOffsetDistance", _emittersList[index].propagationScale);
        shader.SetFloat("_EmitterStepSize", _emittersList[index].propagationScale * Time.deltaTime);
        shader.SetBool("_StimuliActive", useStimuli);
        shader.SetFloat("_StimuliIntensity", stimuliIntensity);
        shader.SetBool("_StimuliToColor", colorFromStimuli);
        shader.SetFloat("_DirectionAngle", directionAngle * Mathf.Deg2Rad);
        shader.SetFloat("_DirectionStrength", directionStrength);

        shader.SetFloat("_FluidStrength", fluidStrength * _emittersList[index].fluidStrength);

        shader.SetBool("_UseInfluenceMap", useInfluenceMap);
		shader.SetFloat("_InfluenceStrength", influenceStrength);

		shader.SetBool("_Test", test);

        shader.SetBuffer(_moveParticlesKernel, "_ParticleBuffer", _particleBuffers[index]);

        shader.Dispatch(_moveParticlesKernel, Mathf.CeilToInt((float)_emittersList[index].capacity / _groupCount), 1, 1);

        SwapTrailTextures();
    }

    void UpdateTrail()
    {
        shader.SetFloat("_Decay", decay);
        shader.SetFloat("_RepulsionLimit", trailRepulsionLimit);
        shader.SetVector("_Gravity", new Vector2(Mathf.Cos(gravityAngle * Mathf.Deg2Rad), Mathf.Sin(gravityAngle * Mathf.Deg2Rad)) * gravityStrength);
        shader.Dispatch(_updateTrailKernel, _groupsCountX, _groupsCountY, 1);

        SwapTrailTextures();
    }

    void AdvectTrail() {

        shader.SetFloat("_FluidAdvection", trailAdvectionFromFluid);
        shader.Dispatch(_advectTrailKernel, _groupsCountX, _groupsCountY, 1);

        SwapTrailTextures();
    }

    void UpdateVelocities() {
        shader.SetFloat("_VelocitiesDecay", velocitiesDecay);
        shader.Dispatch(_updateVelocitiesKernel, _groupsCountX, _groupsCountY, 1);
    }

    void UpdateParticleTexture() {

        shader.Dispatch(_cleanParticleTexture, _groupsCountX, _groupsCountY, 1);

        for (int i = 0; i < _particleBuffers.Count; i++) {
            shader.SetBuffer(_writeParticleTexture, "_ParticleBuffer", _particleBuffers[i]);
            shader.Dispatch(_writeParticleTexture, Mathf.CeilToInt((float)_emittersList[i].capacity / _groupCount), 1, 1);
        }
    }

    void UpdateStimuli() {

        InitializeStimuli();
	}

    void SwapTrailTextures() {

        Graphics.Blit(_trailWrite, _trailRead);
	}

	#endregion

	#region Emitters

	void InitializeEmitters() {

        CreateEmittersList();

        _colorOverLife = new Vector4[_gradientLength];
        _gradientStepSize = 1.0f / (_gradientLength - 1);
    }

    void CreateEmittersList() {

        _emittersList = new List<PhysarumEmitter>();
    }

    public void AddEmitter(PhysarumEmitter emitter) {

        if (!_initialized)
            Initialize();

        _emittersList.Add(emitter);

        InitializeParticles(emitter);
    }

    public void RemoveEmitter(PhysarumEmitter emitter) {

        if (!_emittersList.Contains(emitter))
            return;

        int index = _emittersList.IndexOf(emitter);

        _particleBuffers[index].Release();
        _particleBuffers.RemoveAt(index);

        _emittersList.Remove(emitter);
    }

    void UpdateColorOverLife(int index) {

        for (int i = 0; i < _gradientLength; i++)
            _colorOverLife[i] = _emittersList[index].colorOverLife.Evaluate(i * _gradientStepSize);
	}

    #endregion
}
