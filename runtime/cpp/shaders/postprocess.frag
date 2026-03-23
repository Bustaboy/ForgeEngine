#version 450

layout(push_constant, std430) uniform PostProcessPushConstants {
    float bloomStrength;
    float exposure;
    float vignetteStrength;
    float timeSeconds;
} pc;

layout(location = 0) in vec2 inUv;
layout(location = 0) out vec4 outColor;

vec3 ACESFilm(vec3 x) {
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main() {
    vec2 uv = inUv;
    float radial = 1.0 - clamp(distance(uv, vec2(0.5)) * 1.75, 0.0, 1.0);
    float pulse = 0.5 + 0.5 * sin(pc.timeSeconds * 0.7 + uv.x * 5.0);
    vec3 bloom = vec3(1.0, 0.85, 0.75) * radial * pulse * pc.bloomStrength;

    vec2 centered = uv * 2.0 - 1.0;
    float vignette = 1.0 - dot(centered, centered) * pc.vignetteStrength;
    vignette = clamp(vignette, 0.0, 1.0);

    vec3 graded = ACESFilm(bloom * pc.exposure) * vignette;
    outColor = vec4(graded, 1.0);
}
