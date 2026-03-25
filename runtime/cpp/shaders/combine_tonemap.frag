#version 450

layout(location = 0) out vec4 out_color;

layout(set = 1, binding = 0) uniform sampler2D scene_sampler;
layout(set = 1, binding = 1) uniform sampler2D bloom_sampler;

layout(push_constant, std430) uniform PushConstants {
    layout(offset = 0) mat4 view_proj;
    layout(offset = 64) vec4 light_dir;
    layout(offset = 80) vec4 light_color;
    layout(offset = 96) mat4 model;
    layout(offset = 160) vec4 color;
    layout(offset = 176) vec4 bloom_threshold;
    layout(offset = 192) vec4 combine_params;
} push_constants;

vec3 ACESFilm(vec3 x) {
    const float a = 2.51;
    const float b = 0.03;
    const float c = 2.43;
    const float d = 0.59;
    const float e = 0.14;
    return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
}

void main() {
    vec2 uv = gl_FragCoord.xy / vec2(textureSize(scene_sampler, 0));
    vec2 texel = 1.0 / vec2(textureSize(scene_sampler, 0));
    vec3 scene_color = texture(scene_sampler, uv).rgb;
    vec3 bloom_color = texture(bloom_sampler, uv).rgb;

    float bloom_strength = push_constants.combine_params.x;
    float exposure = push_constants.combine_params.y;
    float vignette_strength = clamp(push_constants.combine_params.z, 0.0, 1.0);
    float saturation = max(0.0, push_constants.combine_params.w);

    vec3 combined = scene_color + bloom_color * bloom_strength;

    float luminance = dot(combined, vec3(0.2126, 0.7152, 0.0722));
    combined = mix(vec3(luminance), combined, saturation);

    vec3 tonemapped = ACESFilm(combined * max(exposure, 0.0001));

    vec2 centered = uv * 2.0 - 1.0;
    float vignette = 1.0 - dot(centered, centered) * vignette_strength;
    tonemapped *= clamp(vignette, 0.25, 1.0);

    float outline_strength = clamp(push_constants.bloom_threshold.y, 0.0, 1.0);
    if (outline_strength > 0.0001) {
        float c = dot(scene_color, vec3(0.299, 0.587, 0.114));
        float sx = abs(c - dot(texture(scene_sampler, uv + vec2(texel.x, 0.0)).rgb, vec3(0.299, 0.587, 0.114)));
        float sy = abs(c - dot(texture(scene_sampler, uv + vec2(0.0, texel.y)).rgb, vec3(0.299, 0.587, 0.114)));
        float edge = clamp((sx + sy) * 3.0, 0.0, 1.0);
        tonemapped = mix(tonemapped, vec3(0.02, 0.02, 0.02), edge * outline_strength);
    }

    out_color = vec4(tonemapped, 1.0);
}
