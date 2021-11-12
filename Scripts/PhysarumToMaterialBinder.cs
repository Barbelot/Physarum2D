using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PhysarumToMaterialBinder : MonoBehaviour
{
    public PhysarumManager physarumManager;
    public Material material;
    public string trailPropertyName = "_MainTex";
	public string stimuliPropertyName = "";

	private bool _binded = false;

	public void OnEnable() {

		Bind();
	}

	private void Update() {

		if (!_binded)
			Bind();
	}

	private void OnValidate() {

		if(Application.isPlaying)
			Bind();
	}

	public void Bind() {

		_binded = true;

		if (trailPropertyName != "") {
			if (physarumManager.trail)
				material.SetTexture(trailPropertyName, physarumManager.trail);
			else
				_binded = false;
		}

		if (stimuliPropertyName != "") {
			if (physarumManager.stimuli)
				material.SetTexture(stimuliPropertyName, physarumManager.stimuli);
			else
				_binded = false;
		}
			
	}
}
