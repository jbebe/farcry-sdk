// The engine's own sky dome and god-ray mask clouds, each replaced at the one draw call that is it.
//
// Our clouds are drawn when a pass ends, which is after everything in it - but a sky belongs under
// the sun, the moon and the stars, so it has to be drawn where the dome is drawn. There is no seam
// at the start of a pass, so the seam is the dome's own draw call: it is recognised as it is
// submitted, dropped, and a sky of ours drawn in its place. Everything the engine draws after it
// then lands on our sky exactly as it landed on the engine's.
//
// The same hook replaces the god-ray mask's cloud draw, the one draw that multiplies its target by
// what it lets through, so that the mask follows our clouds rather than the engine's, and draws the
// moon without the fog the engine hides a low moon in.
//
// It also watches the draws named by their shaders, non-indexed ones too, for the effects below,
// and drops the leaves' draws into the engine's rain cover map, so the rain comes through trees.
//
// See docs/docs/engine-internals/presentation-and-input.md for what named that call.
#pragma once

#include "engine/known_shaders.h"

#include <cstdint>
#include <d3d9.h>

namespace SkyOverhaul::DomeDraw {

// Whether the engine draws its own dome or ours is drawn instead.
enum class Mode {
    Engine,
    Overhaul,
};

// Draws in place of one of the engine's draws, with the device set exactly as that draw found it.
// True means it drew, and the engine's own draw is dropped; false leaves the engine to draw it, so
// a substitute that cannot draw yet costs nothing but what it did not replace.
using SubstituteFn = bool (*)(IDirect3DDevice9* device);

// Takes over the engine device's two draw methods. Call once from FCSE_Load. False means they could
// not be taken, which it logs, and nothing is left hooked. Starts in Engine mode, so nothing
// changes until something asks for it.
bool Install(SubstituteFn substitute);

// Takes effect on the next draw.
void SetMode(Mode mode);

// Whether the pass now ending drew the star sphere and no dome, as the engine does once the night is
// full, so the sky has still to go over the stars.
bool SkyOwed();

// Draws in place of the god-ray mask's cloud draw, with the device set as that draw found it. True
// means it drew, and the engine's draw is dropped.
void SetMaskSubstitute(SubstituteFn mask);

// Draws the final pass's colour grade with other values. `engine` holds Saturation, ColorRemapData
// and ContrastData as the engine set them, a register each; the draw takes `out` instead.
using GradeFn = void (*)(const float engine[12], float out[12]);
void SetGrade(GradeFn grade);

// The engine's registers a shine change put other values in, and the texture it put another in
// place of, to be put back after the draw.
constexpr int kShineSaved = 16;
struct ShineSaved {
    int count;
    UINT registers[kShineSaved];
    float values[kShineSaved][4];
    // Null unless a texture was replaced; holds a reference until it is put back.
    IDirect3DBaseTexture9* texture;
    DWORD sampler;
};

// Changes a material's shine for its draw, through the registers its pixel shader reads it from
// (KnownShaders::Known::shine), recording in `saved` each register and texture it changed with the
// engine's own; a count of nought and no texture leaves the draw as it is.
using ShineFn = void (*)(IDirect3DDevice9* device, const uint8_t* shine, ShineSaved& saved);
void SetShine(ShineFn shine);

// The effects that read the linear depth; the draws that read it are watched for its texture while
// any of them wants it.
enum class DepthUser : unsigned { Shadows = 1, Occlusion = 2, Haze = 4, Clouds = 8, Ground = 16 };

void SetWatchDepth(DepthUser user, bool watch);

// How many registers of their own the vertex shaders lighting foliage in the engine's place read.
constexpr UINT kFoliageParameterRegisters = 2;

// The vertex shader to draw a draw of lit foliage with, given which of the engine's it replaces,
// and the values for its own registers; null leaves the engine's.
using FoliageFn = IDirect3DVertexShader9* (*)(IDirect3DDevice9* device,
                                               KnownShaders::VertexKind kind,
                                               float parameters[kFoliageParameterRegisters * 4]);
void SetFoliage(FoliageFn foliage);

// The vertex shader to draw with in place of `bound`, asked at the engine's own draw call after
// every effect above has chosen its shaders; null leaves `bound`. Never asked in the sky pass.
using VertexFn = IDirect3DVertexShader9* (*)(IDirect3DDevice9* device,
                                              IDirect3DVertexShader9* bound);
void SetVertex(VertexFn vertex);

}
