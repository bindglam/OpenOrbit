shader_type canvas_item;

uniform sampler2D border_texture : filter_linear, repeat_disable;
uniform sampler2D surface_texture : hint_default_white, filter_linear_mipmap, repeat_enable;
uniform sampler2D noise_texture : hint_default_black, filter_linear_mipmap, repeat_enable;
uniform sampler2D shape_data : filter_nearest, repeat_disable;
uniform float units_per_pixel = 0.015;
uniform float surface_scale = 10.0;
uniform float noise_scale = 0.36;
uniform float noise_strength = 1.12;
uniform float border_tune = 1.23;
uniform float shadow_size = 0.11;
uniform vec4 shadow_color = vec4(0.0, 0.0, 0.0, 0.45);
// Gems: 정사각형에 보석 그림을 그대로 붙인다
uniform bool sprite = false;
varying vec2 local_units;
varying flat vec4 shape_a; // 종류, 두 팔 사이 각도(도), 팔 길이, 폭의 절반
varying flat vec4 shape_b; // 모서리 중심 xy, 모서리 반지름, 두 팔이 겹치면 1
varying flat vec3 surface_offset;

void vertex() {
    local_units = VERTEX * units_per_pixel * vec2(1.0, -1.0);
    int column = int(UV.x + 0.5);
    int packed = int(UV.y + 0.5);
    ivec2 at = ivec2(column * 2, packed / 16);
    shape_a = texelFetch(shape_data, at, 0);
    shape_b = texelFetch(shape_data, at + ivec2(1, 0), 0);
    // 변형 번호마다 다른 무늬 위치(xy)와 회전(z, 0~1 → 0~π)
    float variant = float(packed % 16);
    surface_offset = fract(sin(vec3(12.9898, 78.233, 37.719) * (variant + 1.0)) * 43758.5453);
}

float side_of(vec2 dir, vec2 p) {
    return dir.x * p.y - dir.y * p.x;
}

// 팔 하나: 옆선, 앞쪽 팔 끝선, 뒤쪽은 다른 팔 끝선 안까지
float arm(float along, float across, float other_along) {
    return max(max(abs(across) - shape_a.w, along - shape_a.z), min(-along, other_along - shape_a.z));
}

// 두 팔 바깥 옆선 사이 쐐기를 모서리 반지름으로 둥글린 것
float wedge(vec2 p, vec2 dir1) {
    vec2 q = p - shape_b.xy;
    float out0 = -q.y;
    float out1 = side_of(dir1, q);
    float d;
    if (out0 <= 0.0 && out1 <= 0.0) {
        d = max(out0, out1);
    } else {
        d = min(length(q - vec2(max(q.x, 0.0), 0.0)), length(q - dir1 * max(dot(q, dir1), 0.0)));
    }
    return d - shape_b.z;
}

// x: 외곽선까지 부호 있는 거리(안쪽 음수), y: 그림자를 정하는 거리 (그림자가 없는 곳은 아주 큰 값)
vec2 floor_distance(vec2 p) {
    float len = shape_a.z;
    float half_width = shape_a.w;
    if (shape_a.x > 1.5) {
        // 막다른 타일: 짧은 팔과 뾰족한 끝의 오각형
        float x = p.x - shape_b.x;
        float ay = abs(p.y);
        float d = max(max(ay - half_width, x - len), (-x + ay - half_width) * 0.70710678);
        float s = max(ay - half_width, -x + ay - half_width);
        return vec2(d, x <= len ? max(s, 0.0) : 1e6);
    }
    float angle = radians(shape_a.y);
    vec2 dir1 = vec2(cos(angle), sin(angle));
    float along1 = dot(p, dir1);
    float across1 = side_of(dir1, p);
    float d = min(arm(p.x, p.y, along1), arm(along1, across1, p.x));
    if (shape_a.x < 0.5) {
        d = max(d, wedge(p, dir1));
    }
    float s = max(d, 0.0);
    // 팔 끝 너머와 겹친 두 팔 사이에는 그림자가 없다
    if (p.x > len || along1 > len || (shape_b.w > 0.5 && p.y > 0.0 && across1 < 0.0)) {
        s = 1e6;
    }
    return vec2(d, s);
}

void fragment() {
    vec2 p = local_units;
    if (sprite) {
        COLOR *= texture(surface_texture, vec2(0.5 + p.x, 0.5 - p.y));
    } else {
        vec2 dist = floor_distance(p);

        // 타일: 외곽선에서 폭의 절반만큼 들어가면 테두리 1 → 0
        float shade = clamp(1.0 + dist.x / shape_a.w, 0.0, 1.0);
        float turn = surface_offset.z * PI;
        vec2 rotated = mat2(vec2(cos(turn), sin(turn)), vec2(-sin(turn), cos(turn))) * p;
        vec4 surface = texture(surface_texture, surface_offset.xy + rotated / surface_scale) * COLOR;
        vec4 border = texture(border_texture, vec2(0.5, 1.0 - shade));
        float noise = texture(noise_texture, surface_offset.xy + p / noise_scale).r * noise_strength;
        vec3 edge = min(border.rgb * border_tune + vec3(noise * noise * noise), vec3(1.0));
        vec4 tile = vec4(edge * surface.rgb, border.a * surface.a);

        // 그림자: 타일에 붙은 쪽이 진하고 바깥으로 갈수록 옅다
        float t = dist.y / shadow_size;
        float shadow_alpha = t < 1.0 ? pow(1.0 - t, 1.3) * shadow_color.a * COLOR.a : 0.0;

        // 외곽선은 부드럽게 섞지 않는다. 섞으면 이어 붙은 타일 사이에 틈이 비친다
        COLOR = dist.x <= 0.0 ? tile : vec4(COLOR.rgb * shadow_color.rgb, shadow_alpha);
    }
}
