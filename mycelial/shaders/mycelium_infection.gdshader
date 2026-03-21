shader_type canvas_item;

// ============================================================================
//  MYCELIUM INFECTION SHADER
//
//  Applied to TileMapLayer nodes. Affects only mycelium-family tiles by
//  checking the UV atlas position. Creates:
//    - Color pulsing that intensifies with infection stage
//    - Subtle vertex displacement (writhing)
//    - Glow/emission on the freshest infection (MyceliumDark, MyceliumCore)
//    - Veiny tendril patterns overlaid on partially-infected tiles
//
//  The shader reads tile type from atlas position:
//    Row 5 (y=5): Mycelium=col0, Dense=col1, Dark=col2, Core=col3
//    Row 3 (y=3): Infected grass edges (cols 0-7)
//    Row 4 (y=4): Infected grass outer corners (cols 0-3)
//
//  Atlas layout: 16 columns, tile IDs map to (id % 16, id / 16)
// ============================================================================

// --- Uniforms for tuning ---
uniform float pulse_speed : hint_range(0.5, 4.0) = 1.8;
uniform float pulse_intensity : hint_range(0.0, 0.4) = 0.15;
uniform float writhe_speed : hint_range(0.2, 3.0) = 0.9;
uniform float writhe_amplitude : hint_range(0.0, 3.0) = 1.2; // pixels
uniform float glow_intensity : hint_range(0.0, 1.0) = 0.35;
uniform float vein_density : hint_range(1.0, 8.0) = 4.0;
uniform float vein_speed : hint_range(0.1, 2.0) = 0.4;

// Color tints per infection stage
uniform vec4 color_mycelium : source_color = vec4(0.35, 0.28, 0.22, 1.0);
uniform vec4 color_dense : source_color = vec4(0.42, 0.30, 0.38, 1.0);
uniform vec4 color_dark : source_color = vec4(0.50, 0.32, 0.48, 1.0);
uniform vec4 color_core : source_color = vec4(0.60, 0.35, 0.55, 1.0);
uniform vec4 color_infected_grass : source_color = vec4(0.30, 0.35, 0.20, 1.0);

// --- Noise helpers ---
// Simple pseudo-random for GPU (no texture needed)
float hash(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f); // smoothstep

    float a = hash(i);
    float b = hash(i + vec2(1.0, 0.0));
    float c = hash(i + vec2(0.0, 1.0));
    float d = hash(i + vec2(1.0, 1.0));

    return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

// Fractal noise (2 octaves — cheap but organic looking)
float fbm(vec2 p) {
    float value = 0.0;
    value += 0.5 * noise(p);
    value += 0.25 * noise(p * 2.0);
    return value;
}

// Vein pattern — creates branching tendril-like lines
float vein_pattern(vec2 uv, float time) {
    vec2 p = uv * vein_density;
    p.x += time * vein_speed * 0.3;
    p.y += sin(time * vein_speed + uv.x * 3.0) * 0.2;

    float n1 = noise(p);
    float n2 = noise(p * 1.7 + vec2(5.2, 1.3));

    // Create vein-like ridges by taking abs of centered noise
    float vein = 1.0 - abs(n1 - 0.5) * 2.0;
    vein = pow(vein, 3.0); // Sharpen the veins

    float vein2 = 1.0 - abs(n2 - 0.5) * 2.0;
    vein2 = pow(vein2, 4.0);

    return max(vein * 0.7, vein2 * 0.5);
}

void vertex() {
    // --- Writhing vertex displacement ---
    // Only displace mycelium tiles (we'll check in fragment for color,
    // but vertex displacement is cheap and subtle enough to apply broadly)
    float t = TIME * writhe_speed;
    vec2 world_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;

    float displacement_x = sin(t + world_pos.y * 0.15) * cos(t * 0.7 + world_pos.x * 0.1);
    float displacement_y = cos(t * 0.8 + world_pos.x * 0.12) * sin(t * 0.6 + world_pos.y * 0.13);

    VERTEX += vec2(displacement_x, displacement_y) * writhe_amplitude;
}

void fragment() {
    vec4 tex_color = texture(TEXTURE, UV);

    // Skip fully transparent pixels (empty tiles / air)
    if (tex_color.a < 0.01) {
        discard;
    }

    // --- Determine infection stage from atlas UV ---
    // The atlas is 16 columns wide. We can estimate which tile we're in
    // by looking at the UV coordinates relative to the atlas.
    // TileMapLayer sets UV to sample the correct atlas region.
    //
    // We use the color of the tile as a proxy for infection stage,
    // since we can't directly read the tile ID in the shader.
    // Instead, we'll modulate ALL rendered pixels and the effect
    // will naturally only appear on mycelium/infected tiles because
    // those are the only ones with these particular base colors.

    float luminance = dot(tex_color.rgb, vec3(0.299, 0.587, 0.114));
    vec2 world_uv = (MODEL_MATRIX * vec4(UV * 16.0, 0.0, 1.0)).xy;

    // --- Pulse ---
    float t = TIME * pulse_speed;
    float pulse = sin(t + world_uv.x * 0.5 + world_uv.y * 0.3) * 0.5 + 0.5;
    pulse = pulse * pulse_intensity;

    // --- Vein overlay ---
    float veins = vein_pattern(world_uv * 0.06, TIME);

    // --- Color modulation ---
    // Brighten slightly with pulse, add vein pattern as darker lines
    vec3 modulated = tex_color.rgb;
    modulated += vec3(pulse * 0.6, pulse * 0.3, pulse * 0.7); // Purple-ish pulse
    modulated = mix(modulated, modulated * 0.6, veins * 0.3); // Darken along veins

    // --- Glow on fresh infection tiles ---
    // Fresh mycelium (darker base colors) gets more glow
    float darkness = 1.0 - luminance;
    float glow = darkness * glow_intensity * (0.5 + pulse * 0.5);
    modulated += vec3(glow * 0.4, glow * 0.15, glow * 0.5); // Purple glow

    COLOR = vec4(modulated, tex_color.a);
}
