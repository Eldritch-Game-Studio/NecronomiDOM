shader_type canvas_item;
render_mode unshaded, blend_premul_alpha;

// Each draw owns its material and palette. Coordinates remain in CSS space through nested isolated targets.
uniform sampler2D palette : filter_nearest, repeat_disable;
uniform int stop_count;
uniform int gradient_kind;
uniform bool gradient_repeating;
uniform bool radial_circle;
uniform vec4 gradient_origin_axis;
uniform float gradient_length;
uniform vec4 tile_rect;
uniform vec2 tile_step;
uniform vec2 tile_repeat;
uniform vec4 average_color;
uniform bool gradient_edge_fringe = false;
varying vec2 css_position;
varying vec4 gradient_modulate;

void vertex() {
    css_position = VERTEX;
    gradient_modulate = vec4(COLOR.rgb * COLOR.a, COLOR.a);
    if (gradient_edge_fringe) {
        mat4 device = CANVAS_MATRIX * MODEL_MATRIX;
        float determinant = device[0][0] * device[1][1] - device[0][1] * device[1][0];
        vec2 extent = vec2(length(vec2(device[1][1], device[1][0])),
            length(vec2(device[0][1], device[0][0]))) / abs(determinant);
        vec2 expanded = VERTEX + sign(VERTEX - (tile_rect.xy + tile_rect.zw * 0.5)) * extent;
        css_position = expanded;
        VERTEX = expanded;
    }
}

vec4 segment_color(int row, float progress) {
    int intervals = textureSize(palette, 0).x - 2;
    float x = clamp(progress, 0.0, 1.0) * float(intervals);
    int left = int(floor(x));
    vec4 a = texelFetch(palette, ivec2(left + 1, row), 0);
    vec4 b = texelFetch(palette, ivec2(min(left + 1, intervals) + 1, row), 0);
    return mix(a, b, fract(x));
}

vec4 gradient_color(vec2 p) {
    vec2 delta = p - gradient_origin_axis.xy;
    float position;
    if (gradient_kind == 0) {
        position = dot(delta, gradient_origin_axis.zw) / gradient_length;
    } else if (gradient_kind == 1) {
        vec2 radii = gradient_origin_axis.zw;
        // CSS Images degenerate ellipse rules preserve length stops on a zero-width gradient line.
        if (radii.x == 0.0) {
            position = (radial_circle ? length(delta) : abs(delta.x)) / 1e-12;
        } else if (radii.y == 0.0) {
            if (gradient_repeating) { return average_color; }
            return segment_color(stop_count - 1, 0.0);
        } else {
            position = length(delta / radii);
        }
    } else {
        position = mod(atan(delta.x, -delta.y) / TAU - gradient_origin_axis.z, 1.0);
    }
    float first = texelFetch(palette, ivec2(0, 0), 0).r;
    float last = texelFetch(palette, ivec2(0, stop_count - 1), 0).r;
    if (gradient_repeating) {
        float period = last - first;
        if (period <= 0.0) { return average_color; }
        // Sub-device-pixel repeats are unresolvable, and CSS requires their average color.
        float footprint = fwidth(position);
        if (gradient_kind == 2) {
            footprint = dot(abs(delta.yx), fwidth(delta)) / max(TAU * dot(delta, delta), 1e-12);
        }
        if (period < footprint * 0.5) { return average_color; }
        position = first + mod(position - first, period);
    }
    if (position < first) { return segment_color(0, 0.0); }
    int row = 0;
    // Upper-bound search preserves authored ordering at coincident hard stops, without a fixed stop cap.
    int low = 0;
    int high = stop_count;
    while (low < high) {
        int middle = (low + high) / 2;
        if (texelFetch(palette, ivec2(0, middle), 0).r <= position) { low = middle + 1; }
        else { high = middle; }
    }
    row = max(0, low - 1);
    if (row == stop_count - 1) { return segment_color(row, 0.0); }
    vec2 current = texelFetch(palette, ivec2(0, row), 0).rg;
    float next = texelFetch(palette, ivec2(0, row + 1), 0).r;
    float progress = clamp((position - current.r) / (next - current.r), 0.0, 1.0);
    if (current.g <= 0.0) { progress = progress > 0.0 ? 1.0 : 0.0; }
    else if (current.g >= 1.0) { progress = progress < 1.0 ? 0.0 : 1.0; }
    else { progress = pow(progress, log(0.5) / log(current.g)); }
    return segment_color(row, progress);
}

void fragment() {
    vec2 p = css_position - tile_rect.xy;
    if (gradient_edge_fringe) {
        p = clamp(p, vec2(0.0), tile_rect.zw);
    } else {
        if (tile_repeat.x > 0.5) { p.x = mod(p.x, tile_step.x); }
        if (tile_repeat.y > 0.5) { p.y = mod(p.y, tile_step.y); }
        if (p.x < 0.0 || p.y < 0.0 || p.x >= tile_rect.z || p.y >= tile_rect.w) { discard; }
    }
    COLOR = gradient_modulate * gradient_color(p);
}
