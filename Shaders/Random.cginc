#ifndef RANDOM_INCLUDED
#define RANDOM_INCLUDED

#include "UnityCG.cginc"

// Hash function from H. Schechter & R. Bridson, goo.gl/RXiKaH
uint Hash(uint s) {
	s ^= 2747636419u;
	s *= 2654435769u;
	s ^= s >> 16;
	s *= 2654435769u;
	s ^= s >> 16;
	s *= 2654435769u;
	return s;
}

// https://www.pcg-random.org/
uint pcg(uint v)
{
	uint state = v * 747796405u + 2891336453u;
	uint word = ((state >> ((state >> 28u) + 4u)) ^ state) * 277803737u;
	return (word >> 22u) ^ word;
}

float Random(uint seed) {
	return float(pcg(seed)) / 4294967295.0; // 2^32-1
}

float nrand(float2 co){
	return frac(sin(dot(co.xy, float2(12.9898, 78.233))) * 43758.5453);
}

float nrand(float2 uv, float salt) {
	uv += float2(salt, 0.0);
	return nrand(uv);
}

float3 nrand3(float2 seed){
	float t = sin(seed.x + seed.y * 1e3);
	return float3(frac(t*1e4), frac(t*1e6), frac(t*1e5));
}

float3 random_orth(float2 seed) {
	// float u = (nrand(seed) + 1.0) * 0.5;
	float u = nrand(seed);

	float3 axis;

	if (u < 0.166) axis = float3(0, 0, 1);
	else if (u < 0.332) axis = float3(0, 0, -1);
	else if (u < 0.498) axis = float3(0, 1, 0);
	else if (u < 0.664) axis = float3(0, -1, 0);
	else if (u < 0.83) axis = float3(-1, 0, 0);
	else axis = float3(1, 0, 0);

	return axis;
}

float3 random_positive_orth(float2 seed) {
	float u = (nrand(seed) + 1) * 0.5;

	float3 axis;

	if (u < 0.333) axis = float3(0, 0, 1);
	else if (u < 0.666) axis = float3(0, 1, 0);
	else axis = float3(1, 0, 0);

	return axis;
}

// Uniformaly distributed points on a unit sphere
// http://mathworld.wolfram.com/SpherePointPicking.html
float3 random_point_on_sphere(float2 uv) {
	float u = nrand(uv) * 2 - 1;
	float theta = nrand(uv + 0.333) * UNITY_PI * 2;
	float u2 = sqrt(1 - u * u);
	return float3(u2 * cos(theta), u2 * sin(theta), u);
}

#endif // RANDOM_INCLUDED
