using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PhysarumMaterialBinder : MonoBehaviour
{
    public PhysarumManager physarumManager;
    public Material material;
    public string trailPropertyName = "_MainTex";
	public string stimuliPropertyName = "";

	public void Start() {

		if(trailPropertyName != "")
			material.SetTexture(trailPropertyName, physarumManager.trail);

		if (stimuliPropertyName != "")
			material.SetTexture(stimuliPropertyName, physarumManager.stimuli);
	}
}
