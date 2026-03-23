#version 450

layout(set = 0, binding = 0) uniform sampler2D sceneColor;
layout(location = 0) in vec4 inColor;
layout(location = 0) out vec4 outColor;

void main() {
    vec2 uv = gl_FragCoord.xy / vec2(textureSize(sceneColor, 0));
    vec3 color = texture(sceneColor, uv).rgb;
    float luminance = dot(color, vec3(0.2126, 0.7152, 0.0722));
    float mask = smoothstep(0.8, 1.2, luminance);
    outColor = vec4(color * mask, 1.0);
}
