shader_type canvas_item;
render_mode unshaded;
uniform sampler2D styles : filter_nearest, repeat_disable;
varying flat vec4 box_data;
void vertex() { box_data = INSTANCE_CUSTOM; }
float rounded_box(vec2 p, vec2 size, float radius) {
    vec2 q = abs(p - size * 0.5) - size * 0.5 + vec2(radius);
    return length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0) - radius;
}
void fragment() {
    int row = int(box_data.x);
    vec4 bg = texelFetch(styles, ivec2(0, row), 0);
    vec4 border = texelFetch(styles, ivec2(1, row), 0);
    vec4 metrics = texelFetch(styles, ivec2(2, row), 0);
    vec2 size = box_data.zw;
    float radius = min(metrics.y, min(size.x, size.y) * 0.5);
    float distance = rounded_box(UV * size, size, radius);
    float coverage = 1.0 - smoothstep(-0.5, 0.5, distance);
    float interior = 1.0 - smoothstep(-0.5, 0.5, distance + metrics.x);
    vec4 fill = mix(border, bg, interior);
    COLOR = vec4(fill.rgb, fill.a * coverage);
}
