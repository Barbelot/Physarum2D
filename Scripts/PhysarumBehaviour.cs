using UnityEngine;
using System.Collections.Generic;

public class PhysarumBehaviour : MonoBehaviour
{
    [Header("Behaviour ID")]
    public string ID;

    [Header("Initial Values")]
    [SerializeField] private int numberOfParticles = 10000;
    [SerializeField] private int dimension = 256;
    [SerializeField] private ComputeShader shader;
    [SerializeField] private bool stimuliActive = true;
    [SerializeField] private Texture Stimuli;

    [Header("Trail Parameters")]
    [Range(0f, 1f)] public float decay = 0.002f;
    [Range(0f, 1f)] public float wProj = 0.1f;

    [Header("Particles Parameters")]
    public float sensorAngleDegrees = 45f; 	//in degrees
    public float rotationAngleDegrees = 45f;//in degrees
    [Range(0f, 1f)] public float sensorOffsetDistance = 0.01f;
    [Range(0f, 1f)] public float stepSize = 0.001f;
    public float lifetime = 0;

    public struct Emitter
    {
        public Vector2 position;
        public float radius;
    }

    private const int _physarumEmitterSize = 3 * sizeof(float);

    private ComputeBuffer _emittersBuffer;
    private List<PhysarumEmitter> _emittersList;
    private Emitter[] _emittersArray;

    //private int numberOfParticles;
    private float sensorAngle; 				//in radians
    private float rotationAngle;   			//in radians
    private RenderTexture trail;
    private RenderTexture RWStimuli;
    private int initParticlesKernel, spawnParticlesKernel, moveParticlesKernel, updateTrailKernel;
    private ComputeBuffer particleBuffer;

    private static int _groupCount = 16;       // Group size has to be same with the compute shader group size

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

    private int _particleSize = 9;

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

        UpdateEmittersBuffer();
        UpdateParticles();
        UpdateTrail();
    }

    void OnDisable() {
        if (particleBuffer != null) particleBuffer.Release();

        ReleaseEmittersBuffer();
    }

    #endregion

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

        InitializeParticles();
        InitializeTrail();
        InitializeStimuli();
        InitializeEmitters();

        _initialized = true;
    }

    void InitializeParticles()
    {
        // allocate memory for the particles
        //numberOfParticles = (int)(dimension * dimension * percentageParticles);
        if (numberOfParticles < _groupCount) numberOfParticles = _groupCount;

        Particle[] data = new Particle[numberOfParticles];
        particleBuffer = new ComputeBuffer(data.Length, _particleSize * sizeof(float));
        particleBuffer.SetData(data);

        //initialize particles with random positions
        shader.SetInt("numberOfParticles", numberOfParticles);
        shader.SetVector("trailDimension", Vector2.one * dimension);
        shader.SetBuffer(initParticlesKernel, "particleBuffer", particleBuffer);
        shader.SetFloat("lifetime", lifetime);
        shader.Dispatch(initParticlesKernel, numberOfParticles / _groupCount, 1, 1);

        shader.SetBuffer(moveParticlesKernel, "particleBuffer", particleBuffer);
        shader.SetBuffer(spawnParticlesKernel, "particleBuffer", particleBuffer);
    }

    void InitializeTrail()
    {
        trail = new RenderTexture(dimension, dimension, 24);
        trail.enableRandomWrite = true;
        trail.Create();

        var rend = GetComponent<Renderer>();
        rend.material.mainTexture = trail;

        shader.SetTexture(moveParticlesKernel, "TrailBuffer", trail);
        shader.SetTexture(updateTrailKernel, "TrailBuffer", trail);
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
        shader.SetBool("stimuliActive", stimuliActive);
        shader.SetTexture(updateTrailKernel, "Stimuli", RWStimuli);
    }

    void UpdateParticles()
    {
        SpawnParticles();
        MoveParticles();
    }

    void SpawnParticles() {

        shader.SetFloat("_DeltaTime", Time.deltaTime);

        shader.Dispatch(spawnParticlesKernel, numberOfParticles / _groupCount, 1, 1);
    }

    void MoveParticles() {

        sensorAngle = sensorAngleDegrees * 0.0174533f;
        rotationAngle = rotationAngleDegrees * 0.0174533f;

        shader.SetFloat("sensorAngle", sensorAngle);
        shader.SetFloat("rotationAngle", rotationAngle);
        shader.SetFloat("sensorOffsetDistance", sensorOffsetDistance);
        shader.SetFloat("stepSize", stepSize);

        shader.Dispatch(moveParticlesKernel, numberOfParticles / _groupCount, 1, 1);
    }

    void UpdateTrail()
    {
        shader.SetFloat("decay", decay);
        shader.SetFloat("wProj", wProj);
        shader.Dispatch(updateTrailKernel, dimension / _groupCount, dimension / _groupCount, 1);
    }

    #region Emitters

    void InitializeEmitters() {

        CreateEmittersList();
        CreateEmittersArray();
        CreateEmittersBuffer();
    }

    void CreateEmittersList() {

        _emittersList = new List<PhysarumEmitter>();
    }

    void CreateEmittersArray() {

        _emittersArray = _emittersList.Count > 0 ? new Emitter[_emittersList.Count] : new Emitter[1];
    }

    void CreateEmittersBuffer() {

        if (_emittersBuffer != null)
            _emittersBuffer.Release();

        _emittersBuffer = _emittersList.Count > 0 ? new ComputeBuffer(_emittersList.Count, _physarumEmitterSize) : new ComputeBuffer(1, _physarumEmitterSize);

        UpdateEmittersBuffer();
    }

    void UpdateEmittersArray() {

        if (_emittersArray.Length != _emittersList.Count)
            CreateEmittersArray();

        for (int i = 0; i < _emittersList.Count; i++) {
            _emittersArray[i].position = _emittersList[i].position;
            _emittersArray[i].radius = _emittersList[i].radius;
        }
    }

    void UpdateEmittersBuffer() {

        UpdateEmittersArray();

        _emittersBuffer.SetData(_emittersArray);

        shader.SetBuffer(spawnParticlesKernel, "_EmittersBuffer", _emittersBuffer);
        shader.SetInt("_EmittersCount", _emittersList.Count);
    }

    void ReleaseEmittersBuffer() {

        _emittersBuffer.Release();
    }

    public void AddEmitter(PhysarumEmitter emitter) {

        if (!_initialized)
            Initialize();

        _emittersList.Add(emitter);

        CreateEmittersBuffer();
    }

    public void RemoveEmitter(PhysarumEmitter emitter) {

        _emittersList.Remove(emitter);

        CreateEmittersBuffer();
    }

    #endregion
}
