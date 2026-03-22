shader_type canvas_item;

// =========================================================================
//  TENDRIL ORGANIC SHADER — HSV Pulse Wave
//
//  The headline effect: a sine wave of HUE ROTATION + BRIGHTNESS travels
//  along the tendril body in the movement direction. At rest it breathes
//  slowly; when moving it intensifies and accelerates. The wave covers the
//  entire body — core, fresh, trail, root — not just edges.
//
//  All tuning params are exposed as uniforms so you can tweak them live
//  in the Godot inspector via TendrilRenderer exports.
// =========================================================================

// === Texture size (set from C#) ===
uniform vec2 tex_size = vec2(500.0, 280.0);

// === Movement Data (set from C# each frame) ===
uniform float move_speed : hint_range(0.0, 1.0) = 0.0;
uniform vec2 move_direction = vec2(0.0, 1.0);
uniform float activity_floor : hint_range(0.0, 0.5) = 0.15;
uniform float move_reactivity : hint_range(0.0, 1.0) = 0.75;

// === HSV PULSE WAVE — the main event ===
// How fast the color wave travels along the tendril (higher = faster)
uniform float hue_wave_speed : hint_range(0.0, 8.0) = 3.0;
// How many pixels per full wave cycle (lower = tighter ripples)
uniform float hue_wave_length : hint_range(5.0, 200.0) = 30.0;
// How far the hue rotates at peak (0.15 = dramatic purple→pink→blue cycle)
uniform float hue_shift_range : hint_range(0.0, 0.5) = 0.15;
// How much brightness (V) oscillates at peak (0.4 = very visible)
uniform float val_shift_range : hint_range(0.0, 0.8) = 0.40;
// How much saturation oscillates at peak
uniform float sat_shift_range : hint_range(0.0, 0.5) = 0.15;
// Idle breathing: minimum pulse even when stationary
uniform float idle_pulse_strength : hint_range(0.0, 1.0) = 0.35;
// Movement amplification: how much speed boosts the pulse (on top of idle)
uniform float move_pulse_boost : hint_range(0.0, 1.0) = 0.65;

// === Writhing / UV Distortion ===
uniform float writhe_speed : hint_range(0.0, 5.0) = 1.2;
uniform float writhe_strength : hint_range(0.0, 20.0) = 5.0;
uniform float writhe_scale : hint_range(0.5, 40.0) = 10.0;

// === Color Mottling ===
uniform float mottle_strength : hint_range(0.0, 1.0) = 0.35;
uniform float mottle_scale : hint_range(0.5, 30.0) = 5.0;

// === Edge Wobble ===
uniform float edge_wobble_speed : hint_range(0.0, 5.0) = 1.5;
uniform float edge_wobble_strength : hint_range(0.0, 10.0) = 3.0;
uniform float edge_wobble_scale : hint_range(1.0, 30.0) = 7.0;

// === Vein Pattern ===
uniform float vein_strength : hint_range(0.0, 0.6) = 0.2;
uniform float vein_scale : hint_range(1.0, 30.0) = 4.0;
uniform float vein_speed : hint_range(0.0, 3.0) = 0.3;

// =========================================================================
//  NOISE
// =========================================================================

float hash21(vec2 p) {
	p = fract(p * vec2(123.34, 456.21));
	p += dot(p, p + 45.32);
	return fract(p.x * p.y);
}

float vnoise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float a = hash21(i);
	float b = hash21(i + vec2(1.0, 0.0));
	float c = hash21(i + vec2(0.0, 1.0));
	float d = hash21(i + vec2(1.0, 1.0));
	return mix(mix(a, b, f.x), mix(c, d, f.x), f.y);
}

float fbm(vec2 p) {
	float v = 0.0;
	v += vnoise(p) * 0.55;
	v += vnoise(p * 2.13 + 17.3) * 0.30;
	v += vnoise(p * 4.57 + 31.7) * 0.15;
	return v;
}

float ridged(vec2 p) {
	float n = vnoise(p);
	n = abs(n - 0.5) * 2.0;
	return 1.0 - n * n;
}

// =========================================================================
//  HSV ↔ RGB CONVERSION
// =========================================================================

vec3 rgb2hsv(vec3 c) {
	vec4 K = vec4(0.0, -1.0/3.0, 2.0/3.0, -1.0);
	vec4 p = mix(vec4(c.bg, K.wz), vec4(c.gb, K.xy), step(c.b, c.g));
	vec4 q = mix(vec4(p.xyw, c.r), vec4(c.r, p.yzx), step(p.x, c.r));
	float d = q.x - min(q.w, q.y);
	float e = 1.0e-10;
	return vec3(abs(q.z + (q.w - q.y) / (6.0 * d + e)), d / (q.x + e), q.x);
}

vec3 hsv2rgb(vec3 c) {
	vec4 K = vec4(1.0, 2.0/3.0, 1.0/3.0, 3.0);
	vec3 p = abs(fract(c.xxx + K.xyz) * 6.0 - K.www);
	return c.z * mix(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y);
}

// =========================================================================
//  FRAGMENT
// =========================================================================

void fragment() {
	vec2 pixel_uv = UV * tex_size;

	// -----------------------------------------------------------------
	//  ACTIVITY LEVEL
	// -----------------------------------------------------------------
	float activity = mix(activity_floor, 1.0, clamp(move_speed * move_reactivity, 0.0, 1.0));

	// Slow breathing oscillation — strongest when idle
	float idle_blend = 1.0 - clamp(move_speed * 2.0, 0.0, 1.0);
	float breath = sin(TIME * 1.8) * 0.5 + 0.5;
	float breath_boost = breath * 0.25 * idle_blend;
	activity = clamp(activity + breath_boost, 0.0, 1.0);

	// =====================================================================
	//  EDGE WOBBLE
	// =====================================================================
	float eff_edge_strength = edge_wobble_strength * activity;
	float t_edge = TIME * edge_wobble_speed * (0.5 + activity * 0.5);
	vec2 edge_coord = pixel_uv / edge_wobble_scale;
	float edge_dx = (fbm(edge_coord + vec2(t_edge * 0.6, t_edge * 0.4)) - 0.5);
	float edge_dy = (fbm(edge_coord + vec2(t_edge * 0.4 + 80.0, t_edge * 0.6 + 40.0)) - 0.5);
	vec2 edge_offset = vec2(edge_dx, edge_dy) * eff_edge_strength / tex_size;

	vec4 tex = texture(TEXTURE, UV + edge_offset);
	if (tex.a < 0.01) {
		COLOR = vec4(0.0);
		return;
	}

	// =====================================================================
	//  WRITHING
	// =====================================================================
	float eff_writhe = writhe_strength * activity;
	float t_writhe = TIME * writhe_speed * (0.4 + activity * 0.6);
	vec2 writhe_coord = pixel_uv / writhe_scale;
	float dx = fbm(writhe_coord + vec2(t_writhe * 0.7, t_writhe * 0.3)) - 0.5;
	float dy = fbm(writhe_coord + vec2(t_writhe * 0.3 + 100.0, t_writhe * 0.7 + 50.0)) - 0.5;
	vec2 writhe_raw = vec2(dx, dy);

	// Directional bias when moving
	float dir_bias = clamp(move_speed * 1.5, 0.0, 0.7);
	vec2 move_dir_n = length(move_direction) > 0.01 ? normalize(move_direction) : vec2(0.0, 1.0);
	float along = dot(writhe_raw, move_dir_n);
	vec2 directional_writhe = mix(writhe_raw, move_dir_n * along * 1.4, dir_bias);
	vec2 writhe_offset = directional_writhe * eff_writhe / tex_size;

	vec4 distorted = texture(TEXTURE, UV + edge_offset + writhe_offset);
	if (distorted.a < 0.01) {
		distorted = tex;
	}

	vec3 color = distorted.rgb;

	// =====================================================================
	//  MOTTLING
	// =====================================================================
	float eff_mottle = mottle_strength * (0.5 + activity * 0.5);
	float mottle1 = fbm(pixel_uv / mottle_scale + vec2(37.0, 91.0));
	float mottle2 = vnoise(pixel_uv / (mottle_scale * 0.5) + vec2(200.0, 150.0));
	float mottle = (mottle1 * 0.7 + mottle2 * 0.3) - 0.45;
	color += vec3(mottle * eff_mottle * 0.15);
	color *= 1.0 + mottle * eff_mottle * 0.4;

	// =====================================================================
	//  VEINS
	// =====================================================================
	if (vein_strength > 0.0) {
		float eff_vein = vein_strength * (0.4 + activity * 0.6);
		float t_vein = TIME * vein_speed * (0.3 + activity * 0.7);
		float vein = ridged(pixel_uv / vein_scale + vec2(t_vein * 0.2, t_vein * 0.15));
		float vein_dark = 1.0 - (1.0 - vein) * eff_vein;
		float vein_light = vein * eff_vein * 0.1;
		color = color * vein_dark + vec3(vein_light);
	}

	// =====================================================================
	//  HSV PULSE WAVE — the core visual effect
	//
	//  A sine wave travels along the tendril in the movement direction
	//  (or diagonally when idle). It modulates HUE, SATURATION, and VALUE
	//  simultaneously so you see a color shift + brightness wave rippling
	//  through the entire body.
	//
	//  Two overlapping waves at different frequencies prevent the pattern
	//  from looking too uniform/mechanical.
	// =====================================================================

	// Pulse intensity: blend of idle baseline + movement boost
	float pulse_intensity = idle_pulse_strength + move_speed * move_pulse_boost;
	pulse_intensity = clamp(pulse_intensity, 0.0, 1.0);

	// Wave direction: follows movement when moving, slow diagonal when idle
	vec2 idle_dir = normalize(vec2(0.5, 0.7));
	vec2 wave_dir = mix(idle_dir, move_dir_n, clamp(move_speed * 2.0, 0.0, 1.0));

	// Primary wave — the big visible pulse
	float eff_wave_speed = hue_wave_speed * (0.4 + pulse_intensity * 0.6);
	float wave_phase = dot(pixel_uv, wave_dir) / hue_wave_length - TIME * eff_wave_speed;
	float wave1 = sin(wave_phase * 6.283);

	// Secondary wave — slightly different frequency for organic feel
	float wave2_phase = dot(pixel_uv, wave_dir) / (hue_wave_length * 1.7) - TIME * eff_wave_speed * 0.7;
	float wave2 = sin(wave2_phase * 6.283);

	// Combine: primary dominates, secondary adds organic irregularity
	float combined_wave = wave1 * 0.7 + wave2 * 0.3;

	// Add noise to break up the perfectly straight wave front
	float wave_noise = vnoise(pixel_uv / 12.0 + TIME * 0.5) - 0.5;
	combined_wave += wave_noise * 0.3;
	combined_wave = clamp(combined_wave, -1.0, 1.0);

	// Convert to HSV, apply the wave, convert back
	vec3 hsv = rgb2hsv(clamp(color, 0.001, 1.0));

	// Hue rotation: the wave shifts hue ± hue_shift_range
	hsv.x += combined_wave * hue_shift_range * pulse_intensity;
	hsv.x = fract(hsv.x); // wrap around 0..1

	// Saturation boost: wave peaks are more saturated
	float sat_wave = combined_wave * 0.5 + 0.5; // remap to 0..1
	hsv.y = clamp(hsv.y + sat_wave * sat_shift_range * pulse_intensity, 0.0, 1.0);

	// Value (brightness): the most visible component of the pulse
	hsv.z += combined_wave * val_shift_range * pulse_intensity;
	hsv.z = clamp(hsv.z, 0.0, 1.0);

	color = hsv2rgb(hsv);

	// =====================================================================
	//  MOVEMENT COLOR SHIFT — overall hue drift when active
	// =====================================================================
	float shift = clamp(move_speed * move_reactivity, 0.0, 1.0);
	color.r += shift * 0.03;
	color.g -= shift * 0.02;
	color.b += shift * 0.06;
	color += vec3(shift * 0.04);

	// =====================================================================
	//  FINAL
	// =====================================================================
	COLOR = vec4(clamp(color, 0.0, 1.0), distorted.a);
}
