using JackAll.Core.Format.Fcb;

namespace JackAll.Tools.Ai;

public enum SoldierFieldKind
{
    Number,
    Whole,
    Toggle,
    Choice,
}

/// <summary>
/// One tunable of an NPC archetype: the member at <see cref="Path"/> below one of the entity's components,
/// named and explained for someone who has never seen the FCB tree.
/// </summary>
public sealed record SoldierField(
    string Group, string Label, string Help, string[] Path, string Member,
    SoldierFieldKind Kind = SoldierFieldKind.Number, string[]? Choices = null)
{
    private readonly uint _member = FcbClassDefinitions.Crc32Ascii(Member);
    private readonly uint[] _path = [.. Path.Select(FcbClassDefinitions.Crc32Ascii)];

    public double? Read(FcbObject entity)
    {
        if (Owner(entity) is not { } owner || !owner.Values.ContainsKey(_member))
        {
            return null;
        }
        return Kind switch
        {
            SoldierFieldKind.Number => FcbEntityFields.ReadFloat(owner, _member),
            SoldierFieldKind.Toggle => FcbEntityFields.ReadBool(owner, _member) ? 1 : 0,
            _ => FcbEntityFields.ReadU32(owner, _member),
        };
    }

    /// <summary>Writes <paramref name="value"/>; false when this archetype doesn't carry the member.</summary>
    public bool Write(FcbObject entity, double value)
    {
        FcbObject? owner = Owner(entity);
        if (owner is null || !owner.Values.ContainsKey(_member))
        {
            return false;
        }
        owner.Values[_member] = Kind switch
        {
            SoldierFieldKind.Number => BitConverter.GetBytes((float)value),
            SoldierFieldKind.Toggle => [(byte)(value != 0 ? 1 : 0)],
            _ => BitConverter.GetBytes((uint)Math.Max(0, Math.Round(value))),
        };
        return true;
    }

    private FcbObject? Owner(FcbObject entity)
    {
        FcbObject? node = FcbEntityFields.FindComponent(entity, _path[0]);
        for (int i = 1; i < _path.Length && node is not null; i++)
        {
            node = node.Children.Find(c => c.TypeHash == _path[i]);
        }
        return node;
    }
}

/// <summary>The archetype tunables the AI tab offers, grouped the way a designer thinks about a soldier.</summary>
public static class SoldierFields
{
    private const string Perception = "Spotting";
    private const string Vision = "Vision cones";
    private const string Marksmanship = "Marksmanship";
    private const string Detection = "Detection thresholds";
    private const string Movement = "Movement";
    private const string Toughness = "Toughness";
    private const string Role = "Role";

    private static readonly string[] Agent = ["CFCXAIComponent", "AIObject", "CPawnAgent"];
    private static readonly string[] Shooting = [.. Agent, "ShootingSystem"];
    private static readonly string[] Senses = [.. Agent, "SensorySystem"];
    private static readonly string[] Multipliers = [.. Senses, "FOVParameters", "FOVMultipliers"];
    private static readonly string[] Weights = [.. Senses, "VisibilityEvaluatorParameters", "Weights"];
    private static readonly string[] Internals = [.. Senses, "VisibilityEvaluatorParameters", "InternalValues"];
    private static readonly string[] Social = [.. Senses, "SocialMechanic"];
    private static readonly string[] Gait = ["CFCXAIComponent", "AIObject", "CGameAgent"];
    private static readonly string[] Counters = ["CFCXCountersComponentAI"];

    public static IReadOnlyList<SoldierField> All { get; } =
    [
        new(Perception, "Unaware", "How far the soldier sees before anything has alarmed him, as a multiple of the vision cones. Lower makes stealth easier.", Multipliers, "fPreCombatMultiplier"),
        new(Perception, "In combat", "Vision cone multiplier while fighting.", Multipliers, "fCombatMultiplier"),
        new(Perception, "After combat", "Vision cone multiplier while searching once the fight has died down.", Multipliers, "fPostCombatMultiplier"),
        new(Perception, "Player in a vehicle", "Vision cone multiplier when the player drives - vehicles are loud and big.", Multipliers, "fPlayerInVehicleMultiplier"),
        new(Perception, "At night", "Vision cone multiplier in darkness.", Multipliers, "fNightTimeMultiplier"),
        new(Perception, "Scope range", "Length multiplier while looking through a sniper scope.", Multipliers, "fSniperLengthMultiplier"),
        new(Perception, "Scope width", "Angle multiplier while looking through a sniper scope; small means tunnel vision.", Multipliers, "fSniperAngleMultiplier"),
        new(Perception, "Notices a stare after (s)", "How long the player can stare at a calm soldier before he reacts.", Social, "fStareDetectionTime"),
        new(Perception, "Notices a raised gun after (s)", "How long the player can aim at a calm soldier before he reacts.", Social, "fAimAtDetectionTime"),
        new(Perception, "Personal space, inner (m)", "Distance of the innermost intrusion ring around a calm soldier.", Social, "fIntrusionDistanceInnerRing"),
        new(Perception, "Personal space, middle (m)", "Distance of the middle intrusion ring.", Social, "fIntrusionDistanceMidRing"),
        new(Perception, "Personal space, outer (m)", "Distance of the outer intrusion ring.", Social, "fIntrusionDistanceOuterRing"),
        new(Perception, "Charge distance (m)", "Farthest a player running at him still counts as charging.", Social, "fMaxChargingDistance"),
        new(Perception, "Charge angle (°)", "How straight at him the player must run to count as charging.", Social, "fMaxChargingAngle"),
        new(Perception, "Weight: distance", "How much distance hides the player. 0 ignores it.", Weights, "fDistanceEvaluatorWeight"),
        new(Perception, "Weight: vision cone", "How much sitting at the edge of the cone hides the player.", Weights, "fFOVEvaluatorWeight"),
        new(Perception, "Weight: body coverage", "How much of the player's body must be visible (sampled points).", Weights, "fPawnSamplingEvaluatorWeight"),
        new(Perception, "Weight: occlusion", "How much walls and objects hide the player.", Weights, "fOcclusionEvaluatorWeight"),
        new(Perception, "Weight: vegetation", "How much grass and bushes hide the player.", Weights, "fVegetationEvaluatorWeight"),
        new(Perception, "Weight: stance", "How much crouching hides the player.", Weights, "fStanceEvaluatorWeight"),
        new(Perception, "Weight: movement", "How much moving gives the player away.", Weights, "fSpeedEvaluatorWeight"),
        new(Perception, "Weight: light", "How much darkness hides the player.", Weights, "fAmbientLightEvaluatorWeight"),
        new(Perception, "Full visibility inside", "Fraction of the cone length inside which distance no longer hides the player.", Internals, "fDistanceEvaluator_FullVisibilityRatio"),
        new(Perception, "Visibility at cone end", "What is left of visibility at the far end of the cone.", Internals, "fDistanceEvaluator_MinVisibilityAtMaxFOVRange"),
        new(Perception, "Standing still factor", "Visibility of a motionless player, relative to a moving one.", Internals, "fSpeedEvaluator_StandingStillVisibilityFactor"),
        new(Perception, "Visibility at cone edge", "Visibility at the side edge of the cone.", Internals, "fFOVEvaluator_VisibilityFactorAtFOVLimit"),

        .. Cones("Desert", "DesertFOV"),
        .. Cones("Savannah", "SavannahFOV"),
        .. Cones("Jungle", "JungleFOV"),

        new(Marksmanship, "Reaction time (s)", "Every shot misses for this long after he picks the player as target. The single biggest lever against instant, aimbot-like hits.", Shooting, "fTimerToMissTarget"),
        new(Marksmanship, "Miss spread, width (m)", "Width of the area around the player where missed shots land.", Shooting, "fMissWidth"),
        new(Marksmanship, "Miss spread, height (m)", "Height of that area.", Shooting, "fMissHeight"),
        new(Marksmanship, "Point blank (m)", "Inside this distance, and after the point-blank delay, every shot is allowed to hit.", Shooting, "fPointBlankDistance"),
        new(Marksmanship, "Point-blank delay (s)", "How long the player must stay inside point blank before every shot may hit.", Shooting, "fTimerToPointBlank"),
        .. Status("Own", "ShooterStatus", "his own"),
        .. Status("Player's", "TargetStatus", "the player's"),

        .. Thresholds("Idle", "m_Idle"),
        .. Thresholds("Social", "m_Social"),
        .. Thresholds("Alert", "m_Alert"),
        .. Thresholds("Combat", "m_Combat"),
        .. Thresholds("Wounded", "m_Threshold"),
        .. Thresholds("In a vehicle", "m_Vehicle"),

        new(Movement, "Walk speed (m/s)", "Speed of a patrol walk.", Gait, "fSpeedsWalk"),
        new(Movement, "Jog speed (m/s)", "Speed when moving with purpose.", Gait, "fSpeedsJog"),
        new(Movement, "Run speed (m/s)", "Speed when running to cover.", Gait, "fSpeedsRun"),
        new(Movement, "Sprint speed (m/s)", "Top speed.", Gait, "fSpeedsSprint"),
        new(Movement, "Walk variation", "Random spread of walk speed between soldiers, so a patrol does not march in step.", Gait, "fVariationWalk"),
        new(Movement, "Acceleration", "Acceleration at normal urgency.", Gait, "fAccelerationsNormal"),
        new(Movement, "Fast acceleration", "Acceleration when in a hurry.", Gait, "fAccelerationsFast"),

        new(Toughness, "Health", "Hit points; -1 takes the default from the stim-effect table.", Counters, "fAgentHealth"),
        new(Toughness, "Hit locations", "Whether head, torso and limb hits are told apart.", Counters, "bEnableHitLocations", SoldierFieldKind.Toggle),
        new(Toughness, "Torso hit, wounded state", "How strongly a torso hit pushes a soldier towards the wounded (health failure) state.", Counters, "fHealthFailureTorsoHitModifier"),
        new(Toughness, "Limb hit, wounded state", "How strongly a limb hit pushes a soldier towards the wounded state.", Counters, "fHealthFailureLimbsHitModifier"),
        new(Toughness, "Wounded grace (s)", "How long a soldier who just entered the wounded state cannot be killed.", Counters, "fHealthFailureCantDieDuration"),
        new(Toughness, "Weapon jam scale", "Multiplies his weapons' jam chance.", Counters, "WeaponJamProbabilityScale"),

        new(Role, "Has a long-range weapon", "Treated as a marksman by the brain: engages from farther and seeks sniper points.", Agent, "bHasALongRangeWeapon", SoldierFieldKind.Toggle),
        new(Role, "Squad role", "The role the squad logic gives him.", Agent, "selODU", SoldierFieldKind.Choice, ["Grunt", "Medic", "Boss", "Engineer", "Sniper"]),
        new(Role, "Infamy reaction", "Whether he reacts to the player's infamy as low, high or randomly.", Agent, "selAIInfamyMode", SoldierFieldKind.Choice, ["Always low", "Always high", "Random"]),
    ];

    /// <summary>Whether <paramref name="entity"/> runs a soldier brain - the archetypes this catalogue fits.</summary>
    public static bool IsSoldier(FcbObject entity) => Marker.Read(entity) is not null;

    /// <summary>A member only a <c>CPawnAgent</c> with a sensory system carries.</summary>
    private static readonly SoldierField Marker = new("", "", "", [.. Multipliers], "fPreCombatMultiplier");

    /// <summary>The pair <c>CPawnAgent::SetVisibilityValues</c> installs when the brain enters a state.</summary>
    private static IEnumerable<SoldierField> Thresholds(string state, string prefix)
    {
        string where = state == "In a vehicle" ? "in a vehicle" : $"in the {state.ToLowerInvariant()} state";
        yield return new(Detection, $"{state}: senses something at", $"How visible (0-1) the player must be before a soldier {where} starts to notice. Lower detects sooner.", Agent, prefix + "FuzzyVal");
        yield return new(Detection, $"{state}: sees you at", $"How visible (0-1) the player must be before a soldier {where} positively spots him. Lower detects sooner.", Agent, prefix + "ClearVal");
    }

    private static IEnumerable<SoldierField> Cones(string biome, string node)
    {
        string[] fov = [.. Senses, "FOVParameters", node];
        yield return new(Vision, $"{biome}: focus range (m)", $"How far he sees straight ahead in {biome.ToLowerInvariant()} terrain.", [.. fov, "FocusFOV"], "fLength");
        yield return new(Vision, $"{biome}: focus angle (°)", "Width of the sharp central cone.", [.. fov, "FocusFOV"], "fAngle");
        yield return new(Vision, $"{biome}: side range (m)", $"How far his peripheral vision reaches in {biome.ToLowerInvariant()} terrain.", [.. fov, "PeripheralFOV"], "fLength");
        yield return new(Vision, $"{biome}: side angle (°)", "Width of the peripheral cone.", [.. fov, "PeripheralFOV"], "fAngle");
    }

    private static IEnumerable<SoldierField> Status(string who, string node, string whose)
    {
        string[] path = [.. Shooting, node];
        string help = $"Multiplies his hit chance by {whose} situation.";
        yield return new(Marksmanship, $"{who} stance: standing", help, path, "fStandingFactor");
        yield return new(Marksmanship, $"{who} stance: crouching", help, path, "fCrouchingFactor");
        if (node == "ShooterStatus")
        {
            yield return new(Marksmanship, $"{who}: aiming down sights", help, path, "fIronsightFactor");
        }
        yield return new(Marksmanship, $"{who} speed: walking", help, path, "fMoveSpeedWalkFactor");
        yield return new(Marksmanship, $"{who} speed: jogging", help, path, "fMoveSpeedJogFactor");
        yield return new(Marksmanship, $"{who} speed: running", help, path, "fMoveSpeedRunFactor");
        yield return new(Marksmanship, $"{who} speed: sprinting", help, path, "fMoveSpeedSprintFactor");
        yield return new(Marksmanship, $"{who}: driving", help, path, "fDrivingFactor");
        yield return new(Marksmanship, $"{who}: swimming", help, path, "fSwimmingFactor");
        yield return new(Marksmanship, $"{who}: hits per second cap", "Once the player takes this many hits in a second, further shots are not allowed to hit.", path, "uiMaxHitPerSecondFactor", SoldierFieldKind.Whole);
    }
}
