#version 450

layout(set = 0, binding = 0) uniform sampler2D inputColor;
layout(location = 0) in vec4 inColor;
layout(location = 0) out vec4 outColor;

void main() {
    vec2 uv = gl_FragCoord.xy / vec2(textureSize(inputColor, 0));
    vec2 texel = 1.0 / vec2(textureSize(inputColor, 0));

    vec3 result = texture(inputColor, uv).rgb * 0.227027;
    result += texture(inputColor, uv + vec2(texel.x, 0.0)).rgb * 0.1945946;
    result += texture(inputColor, uv - vec2(texel.x, 0.0)).rgb * 0.1945946;
    result += texture(inputColor, uv + vec2(0.0, texel.y)).rgb * 0.1945946;
    result += texture(inputColor, uv - vec2(0.0, texel.y)).rgb * 0.1945946;
    result += texture(inputColor, uv + vec2(texel.x * 2.0, 0.0)).rgb * 0.1216216;
    result += texture(inputColor, uv - vec2(texel.x * 2.0, 0.0)).rgb * 0.1216216;
    result += texture(inputColor, uv + vec2(0.0, texel.y * 2.0)).rgb * 0.1216216;
    result += texture(inputColor, uv - vec2(0.0, texel.y * 2.0)).rgb * 0.1216216;

    outColor = vec4(result, 1.0);
}
