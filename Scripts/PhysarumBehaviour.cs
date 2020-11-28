using UnityEngine;
using System.Collections.Generic;

public class PhysarumBehaviour : MonoBehaviour
{
    [Header("Behaviour ID")]
    public string ID;

    [Header("Initial Values")]
    [SerializeField] private int dimension = 256;
    [SerializeField] private ComputeShader shader;
    [SerializeField] private bool stimuliActive = true;
    [SerializeField] private Texture Stimuli;

    [Header("Trail Parameters")]
    [Range(0f, 1f)] public float decay = 0.002f;
    [Range(0f, 1f)] public float wProj = 0.1f;

    [Header("Particles Parameters")]
    public bool synchronizeSensorAndRotation = false;
    [Range(-180f, 180f)] public float sensorAngleDegrees = 45f; 	//in degrees
    [Range(-180f, 180f)] public float rotationAngleDegrees = 45f;//in degrees
    [Range(0f, 1f)] public float sensorOffsetDistance = 0.01f;
    [Range(0f, 1f)] public float stepSize = 0.001f;
    public float lifetime = 0;

    [Header("Debug")]
    public bool debugParticles = false;

    public struct Emitter
    {
        public Vector2 position;
        public float radius;
        public float spawnRate;
        public int capacity;
    }

    private const int _physarumEmitterStride = 4 * sizeof(float) + 1 * sizeof(int);

    private List<PhysarumEmitter> _emittersList;
    //private Emitter[] _emittersArray;
    //private ComputeBuffer _emittersBuffer;

    //private int numberOfParticles;
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
        if (dimension < _groupCount) dimension = _groupCount;
    }

    void OnEnable()
    {
        if (!_initialized)
            Initialize();
    }

    void Update() {

        if (synchronizeSensorAndRotation)
            rotationAngleDegrees = sensorAngleDegrees;

        //UpdateEmittersBuffer();
        UpdateParticles();
        UpdateTrail();

        if (debugParticles)
            UpdateParticleTexture();
    }

    void OnDisable() {
        ReleaseParticlesBuffer();
        //ReleaseEmittersBuffer();
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

        shader.SetFloat("_Lifetime", lifetime);

        shader.SetInt("_EmitterCapacity", _emittersList[index].capacity);
        shader.SetVector("_EmitterPosition", _emittersList[index].position);
        shader.SetFloat("_EmitterRadius", _emittersList[index].radius);
        shader.SetFloat("_EmitterSpawnRate", _emittersList[index].spawnRate);

        shader.SetBuffer(initParticlesKernel, "_ParticleBuffer", particleBuffers[index]);

        shader.Dispatch(initParticlesKernel, _emittersList[index].capacity / _groupCount, 1, 1);
    }

    void InitializeTrail()
    {
        trail = new RenderTexture(dimension, dimension, 24);
        trail.enableRandomWrite = true;
        trail.Create();

        var rend = GetComponent<Renderer>();
        rend.material.mainTexture = trail;

        shader.SetVector("_TrailDimension", Vector2.one * dimension);

        shader.SetTexture(moveParticlesKernel, "_TrailBuffer", trail);
        shader.SetTexture(updateTrailKernel, "_TrailBuffer", trail);
    }

	void InitializeParticleTexture() {
		particleTexture = new RenderTexture(dimension, dimension, 24);
		particleTexture.enableRandomWrite = true;
		particleTexture.Create();

		var rend = GetComponent<Renderer>();
		rend.material.mainTexture = particleTexture;

		shader.SetTexture(cleanParticleTexture, "_ParticleTexture", particleTexture);
		shader.SetTexture(writeParticleTexture, "_ParticleTexture", particleTexture);
	}

	void InitializeStimuli()
    {
        if (Stimuli == null)
        {
            RWStimuli = new RenderTexture(dimension, dimension, 24);
            RWStimuli.enableRandomWrite = true;
            RWStimuli.Create();
        }
        else
        {
            RWStimuli = new RenderTexture(Stimuli.width, Stimuli.height, 0);
            RWStimuli.enableRandomWrite = true;
            Graphics.Blit(Stimuli, RWStimuli);
        }
        shader.SetBool("_StimuliActive", stimuliActive);
        shader.SetTexture(updateTrailKernel, "_Stimuli", RWStimuli);
    }

    void InitializeParticlesBuffer() {

        ReleaseParticlesBuffer();

        particleBuffers = new ComputeBuffer[_emittersList.Count];

        for (int i = 0; i < particleBuffers.Length; i++)
            InitializeParticles(i);
    }

	#endregion

	#region Release

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
        shader.SetFloat("_EmitterRadius", _emittersList[index].radius);
        shader.SetFloat("_EmitterSpawnRate", _emittersList[index].spawnRate);

        shader.SetBuffer(spawnParticlesKernel, "_ParticleBuffer", particleBuffers[index]);

        shader.Dispatch(spawnParticlesKernel, _emittersList[index].capacity / _groupCount, 1, 1);
    }

    void MoveParticles(int index) {

        sensorAngle = sensorAngleDegrees * 0.0174533f;
        rotationAngle = rotationAngleDegrees * 0.0174533f;

        shader.SetFloat("_SensorAngle", sensorAngle);
        shader.SetFloat("_RotationAngle", rotationAngle);
        shader.SetFloat("_SensorOffsetDistance", sensorOffsetDistance);
        shader.SetFloat("_StepSize", stepSize);

        shader.SetBuffer(moveParticlesKernel, "_ParticleBuffer", particleBuffers[index]);

        shader.Dispatch(moveParticlesKernel, _emittersList[index].capacity / _groupCount, 1, 1);
    }

    void UpdateTrail()
    {
        shader.SetFloat("_Decay", decay);
        shader.SetFloat("_WProj", wProj);
        shader.Dispatch(updateTrailKernel, dimension / _groupCount, dimension / _groupCount, 1);
    }

    void UpdateParticleTexture() {

        shader.Dispatch(cleanParticleTexture, dimension / _groupCount, dimension / _groupCount, 1);

        for (int i = 0; i < particleBuffers.Length; i++) {
            shader.SetBuffer(writeParticleTexture, "_ParticleBuffer", particleBuffers[i]);
            shader.Dispatch(writeParticleTexture, _emittersList[i].capacity / _groupCount, 1, 1);
        }
    }

	#endregion

	#region Emitters

	void InitializeEmitters() {

        CreateEmittersList();
        //CreateEmittersArray();
        //CreateEmittersBuffer();
    }

    void CreateEmittersList() {

        _emittersList = new List<PhysarumEmitter>();
    }

    //void CreateEmittersArray() {

    //    _emittersArray = _emittersList.Count > 0 ? new Emitter[_emittersList.Count] : new Emitter[1];
    //}

    //void CreateEmittersBuffer() {

    //    if (_emittersBuffer != null)
    //        _emittersBuffer.Release();

    //    _emittersBuffer = _emittersList.Count > 0 ? new ComputeBuffer(_emittersList.Count, _physarumEmitterStride) : new ComputeBuffer(1, _physarumEmitterStride);

    //    UpdateEmittersBuffer();
    //}

    //void UpdateEmittersArray() {

    //    if (_emittersArray.Length != _emittersList.Count)
    //        CreateEmittersArray();

    //    for (int i = 0; i < _emittersList.Count; i++) {
    //        _emittersArray[i].position = _emittersList[i].position;
    //        _emittersArray[i].radius = _emittersList[i].radius;
    //        _emittersArray[i].spawnRate = _emittersList[i].spawnRate;
    //    }
    //}

    //void UpdateEmittersBuffer() {

    //    UpdateEmittersArray();

    //    _emittersBuffer.SetData(_emittersArray);

    //    shader.SetBuffer(spawnParticlesKernel, "_EmittersBuffer", _emittersBuffer);
    //    shader.SetInt("_EmittersCount", _emittersList.Count);
    //}

    //void ReleaseEmittersBuffer() {

    //    _emittersBuffer.Release();
    //}

    public void AddEmitter(PhysarumEmitter emitter) {

        if (!_initialized)
            Initialize();

        _emittersList.Add(emitter);

        //CreateEmittersBuffer();
        InitializeParticlesBuffer();
    }

    public void RemoveEmitter(PhysarumEmitter emitter) {

        _emittersList.Remove(emitter);

        //CreateEmittersBuffer();
        InitializeParticlesBuffer();
    }

    #endregion
}
