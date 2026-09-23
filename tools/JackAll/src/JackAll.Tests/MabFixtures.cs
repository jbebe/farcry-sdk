namespace JackAll.Tests;

/// <summary>The retail animation banks and the rigs they pose, under <c>Fixtures\</c>.</summary>
internal static class MabFixtures
{
    // The ak47 reload: the character's clip and three for the rifle, an event chunk, and three tag
    // records naming the rifle.
    public const string Reload = "Mab/3rdge_uppb_reload_nodir_prak4_i1.mab";

    // A pistol shot: one prop, no event chunk, and every rotation re-encodes exactly.
    public const string Pistol = "Mab/1stge_uppb_shootcycle_+000fw_se6p9_i1.mab";

    // A desert eagle shot holding one constant rotation authored on an exact tie, which re-encodes
    // to a different, equally valid triple.
    public const string Tie = "Mab/1stge_uppb_shootingcycle_+000fw_sedea_i1.mab";

    // A cutscene bank that carries the rifle, filed away from every weapon folder.
    public const string Cutscene = "Mab/sm10_se02_guard02_wait01.mab";

    // The ak47 upper-body run a rifle pack is given as its clip.
    public const string RifleRun = "Mab/3rdge_uppb_runregupperbody_+000fw_prak47_i1.mab";

    // The human rig: blend and dependent constraints, and animation handles.
    public const string CharacterRig = "Skeleton/pelvis_ref.skeleton";

    // A weapon's rig: unconstrained bones and no handles.
    public const string RifleRig = "Skeleton/ak47_ref.skeleton";

    // A second character rig, which the rig search must not prefer over the pelvis.
    public const string SingleBoneRig = "Mab/singlebone_ref.skeleton";
}
