#version 450

layout(set = 0, binding = 0) uniform sampler2D sceneColor;
layout(set = 0, binding = 1) uniform sampler2D bloomColor;
layout(location = 0) in vec4 inColor;
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
    vec2 uvScene = gl_FragCoord.xy / vec2(textureSize(sceneColor, 0));
    vec2 uvBloom = gl_FragCoord.xy / vec2(textureSize(bloomColor, 0));
    vec3 scene = texture(sceneColor, uvScene).rgb;
    vec3 bloom = texture(bloomColor, uvBloom).rgb;
    vec3 color = scene + bloom * 0.75;
    vec3 tonemapped = ACESFilm(color);
    outColor = vec4(tonemapped, 1.0);
}
