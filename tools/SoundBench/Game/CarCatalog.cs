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

        return [.. index.Names.Order(StringComparer.OrdinalIgnoreCase)
            .Select(name => Read(name, index.Winner(name)!.Node))
            .OfType<Car>()
            .GroupBy(c => (Sounds: c.Sounds with { WheelRolls = Array.Empty<uint>() }, Wheels: string.Join(",", c.Sounds.WheelRolls), c.Physics))
            .Select(g => g.First() with { Archetypes = [.. g.Select(c => c.Archetypes[0])] })
            .OrderBy(c => c.Real is null)
            .ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase)];
    }

    private static Car? Read(string name, FcbObject entity)
    {
        if (Find(entity, Hash("Sound"), Hash("sndEngineLoop")) is not { } sound
            || Id(sound, "sndEngineLoop") == SpkLayout.NoId
            || Find(entity, Hash("WheeledParams"), Hash("fMass")) is not { } wheeled
            || Find(entity, Hash("SoundSettings"), Hash("sndmlRPMSoundMultilayer")) is not { } settings)
        {
            return null;
        }

        FcbObject pedal = Find(sound, Hash("Settings"), Hash("fThrustPedalStopThreshold")) ?? sound;
        FcbObject start = Find(entity, null, Hash("MintEngineStartTime")) ?? entity;
        FcbObject reliability = Find(entity, null, Hash("sndswtpReliabilitySoundSwitchType")) ?? entity;
        var physics = new CarPhysics(
            Float(wheeled, "fMass"),
            Float(wheeled, "fEnginePower"),
            Float(wheeled, "fGearBoxTopSpeed"),
            (int)(FcbEntityFields.ReadU32(wheeled, Hash("nGears")) ?? 4),
            Wheels(wheeled).Sum(w => Float(w, "fBrakingTorque")),
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

        return new Car([name], RealVehicle.Match(physics.Mass, physics.EnginePower), sounds, physics);
    }

    /// <summary>The first node under <paramref name="root"/> of a class (any, for null) that holds a field.</summary>
    private static FcbObject? Find(FcbObject root, uint? type, uint field)
    {
        if ((type is null || root.TypeHash == type) && root.Values.ContainsKey(field))
        {
            return root;
        }
        foreach (FcbObject child in root.Children)
        {
            if (Find(child, type, field) is { } found)
            {
                return found;
            }
        }
        return null;
    }

    private static IEnumerable<FcbObject> Wheels(FcbObject wheeled)
    {
        uint wheel = Hash("Wheel");
        return wheeled.Children.SelectMany(c => c.Children.Prepend(c)).Where(n => n.TypeHash == wheel);
    }

    private static uint Hash(string name) => FcbClassDefinitions.Crc32Ascii(name);

    private static float Float(FcbObject node, string field) => FcbEntityFields.ReadFloat(node, Hash(field)) ?? 0;

    private static uint Word(FcbObject node, string field) => FcbEntityFields.ReadU32(node, Hash(field)) ?? SpkLayout.NoId;

    /// <summary>A sound id field, which the library keeps as text: <c>0x0045CBD8</c>.</summary>
    private static uint Id(FcbObject node, string field)
        => FcbEntityFields.ReadString(node, Hash(field)) is { Length: > 0 } text ? SpkBank.ParseId(text) : SpkLayout.NoId;
}
