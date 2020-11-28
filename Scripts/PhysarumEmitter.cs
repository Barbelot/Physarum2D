using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PhysarumEmitter : MonoBehaviour
{
	public enum PositionMapping { XY, XZ, YZ }
	[Header("Position Mapping")]
	public PositionMapping positionMapping = PositionMapping.XY;
	[Tooltip("Size of your physarum area in meters.")] public Vector2 size = Vector2.one;
	[Tooltip("Is your emitter centered on its origin ?")] public bool isCentered = false;

	[Header("Physarum Emitter")]
	public string behaviourID;
	public float radius = 0.1f;
	public float spawnRate = 1;
	public int capacity = 1000000;

	[Header("Gizmos")]
	public bool showGizmos = true;

	[HideInInspector] public Vector2 position = Vector2.zero;

	private PhysarumBehaviour _physarumBehaviour;

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

		foreach (var behaviour in FindObjectsOfType<PhysarumBehaviour>()) {
			if (behaviour.ID == behaviourID) {
				_physarumBehaviour = behaviour; break;
			}
		}

		if (_physarumBehaviour)
			_physarumBehaviour.AddEmitter(this);
	}

	void UpdateEmitter() {

		switch (positionMapping) {
			case PositionMapping.XY:
				position.x = isCentered ? transform.position.x / size.x + 0.5f : transform.position.x / size.x;
				position.y = isCentered ? transform.position.y / size.y + 0.5f : transform.position.y / size.y;
				break;
			case PositionMapping.XZ:
				position.x = isCentered ? transform.position.x / size.x + 0.5f : transform.position.x / size.x;
				position.y = isCentered ? transform.position.z / size.y + 0.5f : transform.position.z / size.y;
				break;
			case PositionMapping.YZ:
				position.x = isCentered ? transform.position.y / size.x + 0.5f : transform.position.y / size.x;
				position.y = isCentered ? transform.position.z / size.y + 0.5f : transform.position.z / size.y;
				break;
		}
	}

	void RemoveEmitter() {

		if (_physarumBehaviour)
			_physarumBehaviour.RemoveEmitter(this);
	}
}
