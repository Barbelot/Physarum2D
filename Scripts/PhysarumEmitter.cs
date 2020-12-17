using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PhysarumEmitter : MonoBehaviour
{
	public enum PositionMapping { XY, XZ, YZ }
	[Header("Position Mapping")]
	public PositionMapping positionMapping = PositionMapping.XY;
	//[Tooltip("Size of your physarum area in meters.")] public Vector2 size = Vector2.one;
	//[Tooltip("Is your emitter centered on its origin ?")] public bool isCentered = false;

	[Header("Physarum Emitter")]
	public string behaviourID;
	[Space]
	public int capacity = 1000000;
	public float spawnRate = 1;
	public Vector2 lifetimeMinMax;
	[Space]
	public float radius = 0.1f;
	[Space]
	public Color mainColor = Color.white;
	public Color secondaryColor = Color.red;
	[Range(0, 1)] public float secondaryColorProbability = 0.5f;
	[Space]
	[Range(-180f, 180f)] public float sensorAngleDegrees = 10f;     //in degrees
	//[Range(-180f, 180f)] public float rotationAngleDegrees = 45f;//in degrees
	[Range(0f, 1f)] public float sensorOffsetDistance = 0.05f;
	[Range(0f, 1f)] public float stepSize = 0.1f;

	[Header("Gizmos")]
	public bool showGizmos = true;

	[HideInInspector] public Vector2 position = Vector2.zero;
	[HideInInspector] public Vector2 previousPosition = Vector2.zero;

	private PhysarumManager _physarumManager;

	private void OnEnable() {

		AddEmitter();
		UpdateEmitter();
	}

	private void Update() {

		UpdateEmitter();
	}

	private void OnDisable() {

		RemoveEmitter();
	}

	private void OnDrawGizmos() {

		if (!showGizmos)
			return;

		Gizmos.color = Color.yellow;

		Gizmos.DrawWireSphere(transform.position, radius);
	}

	void AddEmitter() {

		foreach (var behaviour in FindObjectsOfType<PhysarumManager>()) {
			if (behaviour.ID == behaviourID) {
				_physarumManager = behaviour; break;
			}
		}

		if (_physarumManager)
			_physarumManager.AddEmitter(this);
	}

	void UpdateEmitter() {

		previousPosition = position;

		switch (positionMapping) {
			case PositionMapping.XY:
				position.x = transform.position.x;
				position.y = transform.position.y;
				break;
			case PositionMapping.XZ:
				position.x = transform.position.x;
				position.y = transform.position.z;
				break;
			case PositionMapping.YZ:
				position.x = transform.position.y;
				position.y = transform.position.z;
				break;
		}
	}

	void RemoveEmitter() {

		if (_physarumManager)
			_physarumManager.RemoveEmitter(this);
	}
}
