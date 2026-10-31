shader_type canvas_item;
render_mode unshaded;

// Transparent child viewport pixels are premultiplied. Unpremultiply once for
// the normal canvas source-over blend; group alpha/coverage then apply once.
uniform sampler2D edges : repeat_disable, filter_nearest;
uniform int edge_count = 0;
uniform bool empty_polygon = false;
uniform bool rounded_rect = false;
uniform bool empty_rect = false;
uniform vec4 clip_rect = vec4(0.0);
uniform float clip_radius = 0.0;
varying vec2 document_position;
varying vec4 item_modulate;

void vertex() {
    document_position = VERTEX;
    item_modulate = COLOR;
}

void fragment() {
    float coverage = empty_polygon || (rounded_rect && empty_rect) ? 0.0 : 1.0;
    if (rounded_rect && !empty_rect) {
        vec2 half_size = clip_rect.zw * 0.5;
        vec2 center = clip_rect.xy + half_size;
        vec2 q = abs(document_position - center) - (half_size - vec2(clip_radius));
        float distance_inside = clip_radius - (length(max(q, vec2(0.0))) + min(max(q.x, q.y), 0.0));
        float pixel_width = max(fwidth(distance_inside), 0.00001);
        coverage = clamp(distance_inside / pixel_width + 0.5, 0.0, 1.0);
    }
    for (int i = 0; i < edge_count; i++) {
        vec3 edge = texelFetch(edges, ivec2(i, 0), 0).xyz;
        float distance_inside = dot(edge.xy, document_position) + edge.z;
        float pixel_width = max(fwidth(distance_inside), 0.00001);
        coverage = min(coverage, clamp(distance_inside / pixel_width + 0.5, 0.0, 1.0));
    }
    vec4 group_color = textureLod(TEXTURE, UV, 0.0);
    if (group_color.a > 0.0001) {
        group_color.rgb /= group_color.a;
    }
    COLOR = item_modulate * vec4(group_color.rgb, group_color.a * coverage);
}
