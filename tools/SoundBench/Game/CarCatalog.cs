using System.Globalization;
using JackAll.Core.Format.Fcb;
using JackAll.Tools.Spk;
using JackAll.Tools.World;

namespace SoundBench.Game;

/// <summary>Every wheeled vehicle a world can spawn with an engine sound, one per distinct setup.</summary>
public static class CarCatalog
{
    public static IReadOnlyList<Car> Load(GameFiles files, IProgress<string> progress)
    {
        ArchetypeIndex index = ArchetypeIndex.Load(
            "world1", files.Read, progress, ArchetypeIndex.DiscoverDlcLibraries(files.KnownPaths));

        var cars = new Dictionary<string, Car>();
        foreach (string name in index.Names.Order(StringComparer.OrdinalIgnoreCase))
        {
            if (Read(name, index.Winner(name)!.Node) is not { } car)
            {
                continue;
            }
            string key = $"{car.Sounds with { WheelRolls = [] }}|{string.Join(",", car.Sounds.WheelRolls)}|{car.Physics}";
            cars[key] = cars.TryGetValue(key, out Car? first) ? first with { Archetypes = [.. first.Archetypes, name] } : car;
        }

        return [.. cars.Values
            .Select(c => c with { Label = c.Archetypes.Count > 1 ? $"{c.Label} ({c.Archetypes.Count} archetypes)" : c.Label })
            .OrderBy(c => c.Real is null)
            .ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase)];
    }

    private static Car? Read(string name, FcbObject entity)
    {
        if (Find(entity, "Sound", "sndEngineLoop") is not { } sound
            || Id(sound, "sndEngineLoop") == SpkLayout.NoId
            || Find(entity, "WheeledParams", "fMass") is not { } wheeled
            || Find(entity, "SoundSettings", "sndmlRPMSoundMultilayer") is not { } settings)
        {
            return null;
        }

        FcbObject pedal = Find(sound, "Settings", "fThrustPedalStopThreshold") ?? sound;
        FcbObject start = Find(entity, null, "MintEngineStartTime") ?? entity;
        FcbObject reliability = Find(entity, null, "sndswtpReliabilitySoundSwitchType") ?? entity;
        var physics = new CarPhysics(
            Float(wheeled, "fMass"),
            Float(wheeled, "fEnginePower"),
            Float(wheeled, "fGearBoxTopSpeed"),
            (int)(FcbEntityFields.ReadU32(wheeled, Hash("nGears")) ?? 4),
            All(wheeled, "Wheel").Sum(w => Float(w, "fBrakingTorque")),
            Float(start, "MintEngineStartTime"));
        var sounds = new CarSounds(
            Id(sound, "sndEngineIgnition"),
            Id(sound, "sndPlayEngineIdleLoop"),
            Id(sound, "sndStopEngineIdleLoop"),
            Id(sound, "sndEngineLoop"),
            Id(sound, "sndExtraTorqueEngineLoop"),
            Id(sound, "sndTurnOffEngine"),
            Id(sound, "sndThrustPedal"),
            Id(sound, "sndBrake"),
            Id(sound, "sndGearShift_New"),
            [.. Enumerable.Range(1, 4).Select(i => Id(sound, $"sndWheelRoll_{i}")).Where(id => id != SpkLayout.NoId)],
            Float(pedal, "fThrustPedalStopThreshold"),
            Float(pedal, "fThrustPedalStopFadeOut"),
            new CarParameters(
                Word(settings, "sndmlSpeedSoundMultilayer"),
                Word(settings, "sndmlRPMSoundMultilayer"),
                Word(settings, "sndmlThrustPedalSoundMultilayer"),
                Word(settings, "sndmlExtraTorqueSoundMultilayer"),
                Word(sound, "sndmlWheelSlipSoundMultilayer"),
                Word(settings, "sndmlDamageSoundMultilayer")),
            Word(reliability, "sndswtpReliabilitySoundSwitchType"),
            Word(reliability, "sndswvlNoDamageSoundSwitchValue"),
            Word(settings, "sndswtpMaterialSoundSwitchType"));

        RealVehicle? real = RealVehicle.Match(physics.Mass, physics.EnginePower);
        string label = real?.Name ?? (name.StartsWith("vehicle.", StringComparison.OrdinalIgnoreCase) ? name[8..] : name);
        return new Car(label, [name], real, sounds, physics);
    }

    /// <summary>The first node under <paramref name="root"/> of a class (any, for null) that holds a field.</summary>
    private static FcbObject? Find(FcbObject root, string? type, string field)
    {
        if ((type is null || root.TypeHash == Hash(type)) && root.Values.ContainsKey(Hash(field)))
        {
            return root;
        }
        return root.Children.Select(child => Find(child, type, field)).FirstOrDefault(found => found is not null);
    }

    private static IEnumerable<FcbObject> All(FcbObject root, string type)
        => root.Children.SelectMany(child => All(child, type)).Prepend(root).Where(n => n.TypeHash == Hash(type));

    private static uint Hash(string name) => FcbClassDefinitions.Crc32Ascii(name);

    private static float Float(FcbObject node, string field) => FcbEntityFields.ReadFloat(node, Hash(field)) ?? 0;

    private static uint Word(FcbObject node, string field) => FcbEntityFields.ReadU32(node, Hash(field)) ?? SpkLayout.NoId;

    /// <summary>A sound id field, which the library keeps as text: <c>0x0045CBD8</c>.</summary>
    private static uint Id(FcbObject node, string field)
    {
        string text = FcbEntityFields.ReadString(node, Hash(field)).Trim();
        return text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
               && uint.TryParse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint id)
            ? id
            : SpkLayout.NoId;
    }
}
