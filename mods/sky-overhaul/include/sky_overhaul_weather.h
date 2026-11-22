// Sky Overhaul's weather as another FCSE plugin sees it: what is overhead now, one weather held in
// place of the timeline, and a storm put down by hand. SkyOverhaul.dll exports one function,
// SkyOverhaul_GetWeatherAPI.
#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// Bumped by any change to the structs or signatures below.
#define SKYOVERHAUL_WEATHER_API_VERSION 3

typedef struct SkyOverhaul_WeatherState {
    // Nonzero once a world frame has run with the weather on.
    int32_t live;
    // Rows by index: the one showing most, the one it is turning into (itself when none), how far,
    // and game seconds until the next row is wholly in.
    int32_t row;
    int32_t next;
    float blend;
    float secondsLeft;
    // The held row, or -1 while the timeline runs.
    int32_t forced;
    float mist;
    float wet;
    float dust;
    float storminess;
    float coverage;
    float cirrus;
    float rain;
    float windForce;
    float windDegrees;
    // Metres to the fog's end and how much of the fog is the weather's.
    float fogEnd;
    float fogWeight;
    // How much of the weather at the player is the storm cells', and metres to the nearest cell's
    // centre, below nought for none near.
    float cell;
    float cellDistance;
    // The way the camera looks, given as windDegrees is.
    float facingDegrees;
} SkyOverhaul_WeatherState;

typedef struct SkyOverhaul_WeatherAPI {
    // Always SKYOVERHAUL_WEATHER_API_VERSION for this layout - compare before using anything below.
    uint32_t apiVersion;
    int32_t (*RowCount)(void);
    // The row's id ("C1") and name, or null past the last.
    const char* (*RowId)(int32_t row);
    const char* (*RowName)(int32_t row);
    // From any thread.
    void (*Read)(SkyOverhaul_WeatherState* out);
    // Holds `row` from the next frame, or lets go with -1.
    void (*Force)(int32_t row);
    // Puts a storm down `distance` metres from the camera toward `degrees`, given as windDegrees
    // is, at its full from the next frame; it drifts with the wind like any other. A distance below
    // nought takes it away.
    void (*Plant)(float degrees, float distance);
} SkyOverhaul_WeatherAPI;

typedef const SkyOverhaul_WeatherAPI* (*SkyOverhaul_GetWeatherAPIFn)(void);

#ifdef __cplusplus
}
#endif
