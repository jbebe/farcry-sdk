#include "weather/table.h"

#include <cstring>
#include <iterator>

namespace {
    using SkyOverhaul::Weather::Family;
    using SkyOverhaul::Weather::kBreeze;
    using SkyOverhaul::Weather::kCalm;
    using SkyOverhaul::Weather::kWindy;
    using SkyOverhaul::Weather::Row;

    constexpr float kGale = 190.0f;

    #define GAME_FOG 0.0f, {1.0f, 1.0f, 1.0f}
    #define WHITE_GREY {0.95f, 1.0f, 1.05f}
    #define COOL_WHITE {0.9f, 0.98f, 1.1f}
    #define GREY {0.8f, 0.82f, 0.85f}

    // id, name, family,
    //   coverage, density, tallness, shade, cirrus, cirrus opacity, contrails,
    //   storminess, haze, heat, fog and tint, rain, wind, dust, milk
    Row g_rows[] = {
        {"C1", "Clear sky", Family::Clear,
         0.0f, 0.021f, 0.5f, 0.0f, 0.0f, 0.04f, 0.04f,
         0.0f, 1.0f, 1.0f, GAME_FOG, 0.0f, kCalm},
        {"C2", "Light cirrus", Family::Clear,
         0.0f, 0.021f, 0.5f, 0.0f, 0.33f, 0.15f, 0.06f,
         0.0f, 1.0f, 1.0f, GAME_FOG, 0.0f, kCalm},
        {"C3", "Strong cirrus", Family::Clear,
         0.0f, 0.021f, 0.5f, 0.0f, 0.9f, 0.22f, 0.08f,
         0.05f, 1.0f, 1.0f, GAME_FOG, 0.0f, kCalm},
        {"C4", "A few cumulus", Family::Clear,
         0.38f, 0.015f, 0.25f, 0.0f, 0.1f, 0.04f, 0.04f,
         0.0f, 1.0f, 1.0f, GAME_FOG, 0.0f, kBreeze},
        {"C5", "Lots of cumulus and cirrus", Family::Clear,
         0.44f, 0.021f, 0.5f, 0.0f, 0.65f, 0.45f, 0.04f,
         0.0f, 1.0f, 1.0f, GAME_FOG, 0.0f, kBreeze},
        {"C6", "Thick but light cumulus", Family::Clear,
         0.55f, 0.012f, 0.75f, 0.0f, 0.0f, 0.04f, 0.04f,
         0.0f, 1.0f, 0.8f, GAME_FOG, 0.0f, kBreeze},
        {"C7", "Hazy and dry", Family::Clear,
         0.05f, 0.015f, 0.25f, 0.0f, 0.2f, 0.04f, 0.04f,
         0.05f, 1.6f, 1.0f, GAME_FOG, 0.0f, kBreeze, 0.5f, 0.6f},

        {"F1", "Fog under full cirrus", Family::Foggy,
         0.0f, 0.021f, 0.5f, 0.0f, 1.0f, 0.52f, 0.02f,
         0.4f, 1.0f, 0.0f, 150.0f, COOL_WHITE, 0.0f, kCalm, 0.0f, 0.35f},
        {"F2", "Fog, cumulus and full cirrus", Family::Foggy,
         0.4f, 0.021f, 0.5f, 0.2f, 1.0f, 0.3f, 0.02f,
         0.4f, 1.0f, 0.0f, 150.0f, WHITE_GREY, 0.0f, kCalm, 0.0f, 0.35f},
        {"F3", "Storm warning", Family::Storm,
         0.2f, 0.05f, 0.75f, 0.5f, 1.0f, 0.6f, 0.0f,
         0.7f, 1.0f, 0.0f, 200.0f, GREY, 0.0f, kCalm},

        {"O1", "Overcast", Family::Overcast,
         0.85f, 0.05f, 0.25f, 0.5f, 0.5f, 0.3f, 0.0f,
         0.85f, 1.0f, 0.0f, GAME_FOG, 0.0f, kBreeze},
        {"O2", "Drizzle", Family::Overcast,
         0.9f, 0.06f, 0.25f, 0.6f, 0.5f, 0.3f, 0.0f,
         0.9f, 1.0f, 0.0f, GAME_FOG, 0.3f, kBreeze},
        {"O3", "Steady rain", Family::Overcast,
         0.95f, 0.08f, 0.25f, 0.7f, 0.5f, 0.3f, 0.0f,
         1.0f, 1.0f, 0.0f, GAME_FOG, 0.65f, kWindy},

        {"S1", "Gathering", Family::Storm,
         0.56f, 0.02f, 1.0f, 0.6f, 1.0f, 0.4f, 0.0f,
         0.55f, 1.0f, 0.0f, GAME_FOG, 0.0f, 150.0f},
        {"S2", "Thunderstorm", Family::Storm,
         0.65f, 0.09f, 1.0f, 0.8f, 1.0f, 0.8f, 0.0f,
         1.0f, 1.0f, 0.0f, GAME_FOG, 0.6f, 130.0f},
        {"S3", "Heavy storm", Family::Storm,
         0.9f, 0.12f, 1.0f, 0.9f, 1.0f, 0.8f, 0.0f,
         1.0f, 1.0f, 0.0f, GAME_FOG, 1.0f, kGale},
        {"S4", "Clearing", Family::Storm,
         0.3f, 0.03f, 0.5f, 0.2f, 0.5f, 0.1f, 0.0f,
         0.1f, 0.67f, 0.0f, GAME_FOG, 0.0f, 60.0f},

        {"W1", "Breezy", Family::Wind,
         0.3f, 0.018f, 0.4f, 0.0f, 0.2f, 0.04f, 0.04f,
         0.0f, 1.0f, 0.7f, GAME_FOG, 0.0f, kWindy},
        {"W2", "Windy and hazy", Family::Wind,
         0.05f, 0.015f, 0.25f, 0.0f, 0.2f, 0.04f, 0.02f,
         0.1f, 1.6f, 0.3f, GAME_FOG, 0.0f, 160.0f, 0.9f, 0.5f},
    };
}

Row* SkyOverhaul::Weather::Rows() {
    return g_rows;
}

size_t SkyOverhaul::Weather::RowCount() {
    return std::size(g_rows);
}

int SkyOverhaul::Weather::Find(const char* id) {
    for (size_t i = 0; i < std::size(g_rows); i++) {
        if (std::strcmp(g_rows[i].id, id) == 0) {
            return static_cast<int>(i);
        }
    }
    return -1;
}
