// SkyOverhaul_GetWeatherAPI, for DevTools' Environment tab.
#include "sky_overhaul_weather.h"

#include "weather/weather.h"

namespace {
    using namespace SkyOverhaul::Weather;

    const Row* At(int32_t row) {
        return row >= 0 && static_cast<size_t>(row) < RowCount() ? &Rows()[row] : nullptr;
    }

    int32_t ApiRowCount() {
        return static_cast<int32_t>(RowCount());
    }

    const char* ApiRowId(int32_t row) {
        const Row* found = At(row);
        return found != nullptr ? found->id : nullptr;
    }

    const char* ApiRowName(int32_t row) {
        const Row* found = At(row);
        return found != nullptr ? found->name : nullptr;
    }

    void ApiRead(SkyOverhaul_WeatherState* out) {
        *out = {};
        out->forced = Forced();
        Reading reading;
        if (!Latest(reading)) {
            return;
        }
        const Levers& levers = reading.levers;
        out->live = 1;
        out->row = reading.row;
        out->next = reading.next;
        out->blend = reading.blend;
        out->secondsLeft = static_cast<float>(reading.secondsLeft);
        out->mist = reading.mist;
        out->wet = reading.wet;
        out->dust = reading.dust;
        out->storminess = levers.storminess;
        out->coverage = levers.coverage;
        out->cirrus = levers.cirrus;
        out->rain = levers.rain;
        out->windForce = levers.wind;
        out->windDegrees = reading.windDegrees;
        out->fogEnd = FogOf(levers).end;
        out->fogWeight = levers.fogWeight;
        out->cell = reading.cell;
        out->cellDistance = reading.cellDistance;
        Facing(out->facingDegrees);
    }

    void ApiForce(int32_t row) {
        Force(row);
    }

    void ApiPlant(float degrees, float distance) {
        Plant(degrees, distance);
    }

    const SkyOverhaul_WeatherAPI kApi = {SKYOVERHAUL_WEATHER_API_VERSION, &ApiRowCount, &ApiRowId,
                                         &ApiRowName, &ApiRead, &ApiForce, &ApiPlant};
}

extern "C" __declspec(dllexport) const SkyOverhaul_WeatherAPI* SkyOverhaul_GetWeatherAPI() {
    return &kApi;
}
