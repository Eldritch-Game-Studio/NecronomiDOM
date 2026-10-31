// Chrome 155 pins Skia 29ed1e87a0a50f3d8347e988842d9d59e7573efa.
// SkAnalyticEdge::setLine/SnapY quantize endpoints to 1/256 x and 1/4 y;
// aaa_walk_convex_edges snaps scanline intersections to 1/16 x. Coverage is
// the sum of horizontal trapezoid strips, not an fwidth signed-distance ramp.
// https://skia.googlesource.com/skia/+/29ed1e87a0a50f3d8347e988842d9d59e7573efa/src/core/SkScan_AAAPath.cpp
// Chromium's Linux default raster tiles are 256x256 with one border texel;
// TilingData owns 254px interiors and clips paths to the overlapping tile BEFORE
// Skia's endpoint snapping. A whole-quad conversion drifts across tile seams.
// https://chromium.googlesource.com/chromium/src/+/155.0.8059.39/cc/base/tiling_data.cc
// https://chromium.googlesource.com/chromium/src/+/155.0.8059.39/cc/trees/layer_tree_settings.cc
uniform vec2 rect_raster_origin = vec2(0.0);
varying flat vec2 rect_raw_0;
varying flat vec2 rect_raw_1;
varying flat vec2 rect_raw_2;
varying flat vec2 rect_raw_3;
varying flat float rect_axis_aligned;

vec2 rect_endpoint(vec2 p) {
    vec2 fixed_point = trunc(p * 256.0) / 256.0;
    return vec2(fixed_point.x, floor(fixed_point.y * 4.0 + 0.5) / 4.0);
}

float rect_triangle_alpha(float width, float inverse_slope) {
    float a = floor(width * 32.0);
    float b = floor(inverse_slope * 32.0);
    return mod(floor(a * a * b / 256.0), 256.0);
}

// Coverage on the filled side of one edge over a horizontal strip. The two
// end triangles use Skia's fixed-point triangle approximation. Full-height,
// single-pixel trapezoids use its direct (8-bit truncated) area conversion.
float rect_side_alpha(vec2 ends, float inverse_slope, float height, float x, bool left) {
    float l = floor(min(ends.x, ends.y) * 16.0 + 0.5) / 16.0;
    float r = floor(max(ends.x, ends.y) * 16.0 + 0.5) / 16.0;
    float full = floor(255.0 * height + 0.5);
    float begin = floor(l);
    float end = ceil(r);
    if (x < begin) { return left ? 0.0 : full; }
    if (x >= end) { return left ? full : 0.0; }
    float count = end - begin;
    if (count == 1.0) {
        float area = left ? (2.0 * end - l - r) : (l + r - 2.0 * begin);
        float alpha = floor(area * 128.0);
        return full == 255.0 ? alpha : floor(alpha * full / 256.0);
    }
    float first = begin + 1.0 - l;
    float last = r - (end - 1.0);
    float offset = x - begin;
    float alpha;
    if (count == 2.0) {
        alpha = offset == 0.0 ? rect_triangle_alpha(first, inverse_slope)
            : full - rect_triangle_alpha(last, inverse_slope);
        return left ? alpha : full - alpha;
    }
    if (left) {
        if (offset == 0.0) { return rect_triangle_alpha(first, inverse_slope); }
        float last_height = floor(last * inverse_slope * 65536.0) / 65536.0;
        if (offset == count - 1.0) { return full - floor(last * last_height * 128.0); }
        return full - floor((last_height + inverse_slope * (count - offset - 1.5)) * 256.0);
    }
    if (offset == count - 1.0) { return rect_triangle_alpha(last, inverse_slope); }
    float first_height = floor(first * inverse_slope * 65536.0) / 65536.0;
    if (offset == 0.0) { return full - floor(first * first_height * 128.0); }
    return full - floor((first_height + inverse_slope * (offset - 0.5)) * 256.0);
}

float rect_device_coverage(vec2 center) {
    center += rect_raster_origin;
    if (rect_axis_aligned > 0.5) {
        // The axis-aligned rectangle fast path integrates the actual device box,
        // including fractional DPR/translation; it does not quantize path endpoints.
        vec2 extent = clamp(min(center + vec2(0.5), max(rect_raw_0, rect_raw_2))
            - max(center - vec2(0.5), min(rect_raw_0, rect_raw_2)), vec2(0.0), vec2(1.0));
        return min(255.0, floor(extent.x * extent.y * 256.0)) / 255.0;
    }
    vec2 points[8];
    points[0] = rect_raw_0; points[1] = rect_raw_1;
    points[2] = rect_raw_2; points[3] = rect_raw_3;
    int count = 4;
    vec2 tile_origin = max(floor((floor(center) - vec2(1.0)) / 254.0), vec2(0.0)) * 254.0;
    vec2 clipped[8];
    for (int plane = 0; plane < 4; plane++) {
        int axis = plane / 2;
        bool greater = plane % 2 == 0;
        float boundary = tile_origin[axis] + (greater ? 0.0 : 256.0);
        int next_count = 0;
        for (int i = 0; i < count; i++) {
            vec2 a = points[i];
            vec2 b = points[(i + 1) % count];
            bool inside_a = greater ? a[axis] >= boundary : a[axis] <= boundary;
            bool inside_b = greater ? b[axis] >= boundary : b[axis] <= boundary;
            if (inside_a) { clipped[next_count++] = a; }
            if (inside_a != inside_b && a[axis] != boundary && b[axis] != boundary) {
                float t = (boundary - a[axis]) / (b[axis] - a[axis]);
                clipped[next_count++] = mix(a, b, t);
            }
        }
        count = next_count;
        for (int i = 0; i < count; i++) { points[i] = clipped[i]; }
    }
    if (count < 3) { return 0.0; }
    for (int i = 0; i < count; i++) { points[i] = rect_endpoint(points[i]); }
    float row = floor(center.y);
    float stop = row + 1.0;
    float y = row;
    float alpha = 0.0;
    // The intersection of two rectangles has at most eight endpoint heights.
    for (int strip = 0; strip < 9; strip++) {
        float next_y = stop;
        for (int i = 0; i < count; i++) {
            if (points[i].y > y && points[i].y < next_y) { next_y = points[i].y; }
        }
        vec2 left_ends = vec2(0.0);
        vec2 right_ends = vec2(0.0);
        float left_inverse = 0.0;
        float right_inverse = 0.0;
        int active = 0;
        for (int i = 0; i < count; i++) {
            vec2 a = points[i];
            vec2 b = points[(i + 1) % count];
            if (a.y > b.y) { vec2 swap_point = a; a = b; b = swap_point; }
            if (a.y <= y && b.y >= next_y && b.y > a.y) {
                // setLine uses 26.6 deltas for its 16.16 slope.
                float dx = floor((b.x - a.x) * 64.0);
                float dy = (b.y - a.y) * 64.0;
                float slope = trunc(dx * 65536.0 / dy) / 65536.0;
                float inverse = dx == 0.0 ? 0.0 : abs(trunc(dy * 65536.0 / dx) / 65536.0);
                vec2 ends = a.x + slope * (vec2(y, next_y) - a.y);
                if (active == 0 || ends.x + ends.y < left_ends.x + left_ends.y) {
                    right_ends = left_ends; right_inverse = left_inverse;
                    left_ends = ends; left_inverse = inverse;
                } else { right_ends = ends; right_inverse = inverse; }
                active++;
            }
        }
        if (active == 2) {
            float height = next_y - y;
            float full = floor(255.0 * height + 0.5);
            float left_alpha = rect_side_alpha(left_ends, left_inverse, height, floor(center.x), true);
            float right_alpha = rect_side_alpha(right_ends, right_inverse, height, floor(center.x), false);
            alpha += max(0.0, left_alpha + right_alpha - full);
        }
        y = next_y;
        if (y == stop) { break; }
    }
    return min(alpha, 255.0) / 255.0;
}
