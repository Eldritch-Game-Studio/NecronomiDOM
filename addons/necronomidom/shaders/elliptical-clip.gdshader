shader_type canvas_item;
render_mode unshaded;
#include "res://addons/necronomidom/shaders/rect-coverage.gdshaderinc"

// Same transparent isolated-child sampling/blend contract as polygon-clip.gdshader.
// Only geometry coverage differs; no shared screen/backbuffer sampler.
uniform vec4 clip_rect = vec4(0.0);
uniform vec4 top_radii = vec4(0.0); // top-left xy, top-right zw
uniform vec4 bottom_radii = vec4(0.0); // bottom-right xy, bottom-left zw
uniform bool empty_clip = true;
varying vec2 document_position;
varying vec4 item_modulate;

void vertex() {
    document_position = VERTEX;
    item_modulate = COLOR;
    mat4 device = CANVAS_MATRIX * MODEL_MATRIX;
    rect_raw_0 = (device * vec4(clip_rect.xy, 0.0, 1.0)).xy + rect_raster_origin;
    rect_raw_1 = (device * vec4(clip_rect.xy + vec2(clip_rect.z, 0.0), 0.0, 1.0)).xy + rect_raster_origin;
    rect_raw_2 = (device * vec4(clip_rect.xy + clip_rect.zw, 0.0, 1.0)).xy + rect_raster_origin;
    rect_raw_3 = (device * vec4(clip_rect.xy + vec2(0.0, clip_rect.w), 0.0, 1.0)).xy + rect_raster_origin;
    rect_axis_aligned = ((device[0][1] == 0.0 && device[1][0] == 0.0)
        || (device[0][0] == 0.0 && device[1][1] == 0.0)) ? 1.0 : 0.0;
}

float corner_coverage(vec2 from_corner, vec2 radius) {
    // Derivatives precede the varying branch, including at tangent joins.
    vec2 unit = (from_corner - radius) / max(radius, vec2(0.00001));
    float inside = 1.0 - dot(unit, unit);
    float pixel_width = max(fwidth(inside), 0.00001);
    if (radius.x <= 0.0 || radius.y <= 0.0 || from_corner.x >= radius.x || from_corner.y >= radius.y) {
        return 1.0;
    }
    return clamp(inside / pixel_width + 0.5, 0.0, 1.0);
}

void fragment() {
    vec2 start = document_position - clip_rect.xy;
    vec2 end = clip_rect.zw - start;
    // Each straight edge is linear in one axis of document_position: take that axis' own
    // pixel footprint before the nonlinear min(). fwidth() of the minimum mixes different
    // edges across one 2x2 derivative quad at corners.
    vec2 pixel_width = max(fwidth(document_position), vec2(0.00001));
    vec2 edge_inside = min(start, end) / pixel_width;
    float coverage = empty_clip ? 0.0 : clamp(min(edge_inside.x, edge_inside.y) + 0.5, 0.0, 1.0);
    if (top_radii == vec4(0.0) && bottom_radii == vec4(0.0)) {
        // Interior pixels need no scan conversion; only the device-pixel fringe does.
        float nearest_edge = min(edge_inside.x, edge_inside.y);
        coverage = empty_clip || nearest_edge <= -1.0 ? 0.0 : (nearest_edge >= 1.0
            ? 1.0 : rect_device_coverage(FRAGCOORD.xy));
    }
    coverage = min(coverage, corner_coverage(start, top_radii.xy));
    coverage = min(coverage, corner_coverage(vec2(end.x, start.y), top_radii.zw));
    coverage = min(coverage, corner_coverage(end, bottom_radii.xy));
    coverage = min(coverage, corner_coverage(vec2(start.x, end.y), bottom_radii.zw));
    vec4 group_color = textureLod(TEXTURE, UV, 0.0);
    if (group_color.a > 0.0001) {
        group_color.rgb /= group_color.a;
    }
    COLOR = item_modulate * vec4(group_color.rgb, group_color.a * coverage);
}
