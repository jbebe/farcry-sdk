# Reconcile an armature built from an .xbg with the .skeleton clips animate.
#
# The two trees disagree, and the disagreement is what tears a character's knees
# apart. On `pelvis_ref` the four mid-joint helpers — L/R Knee and L/R Elbow —
# hang off the Pelvis in the .xbg but off the thigh and upper arm they sit on in
# the .skeleton. Posed on the .xbg tree a knee helper stays by the hip while the
# leg swings, and the mesh weighted to it stretches into spikes.
#
# The parent ids come from CSkeletonResource::SerializeBone. Reparenting keeps
# every bone's head, tail and roll, so the bind pose the mesh was skinned
# against is untouched and only the pose propagation changes.

import bpy

from fc2fmt.skeleton import ORI_NONE

MARKER = "FC2 "


def clear(armature):
    """Drop the constraints a previous run added, leaving any others alone."""
    for pose_bone in armature.pose.bones:
        for constraint in list(pose_bone.constraints):
            if constraint.name.startswith(MARKER):
                pose_bone.constraints.remove(constraint)


def reparent(armature, skeleton):
    """Move bones onto the parents the .skeleton names. Returns how many moved.

    Only bones the skeleton gives a parent are touched, so the .xbg's own root
    above the pelvis is left where it is.
    """
    names = {bone.id: bone.name for bone in skeleton.bones}
    previous = bpy.context.view_layer.objects.active
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="EDIT")
    moved = []
    try:
        for bone in skeleton.bones:
            edit_bone = armature.data.edit_bones.get(bone.name)
            wanted = armature.data.edit_bones.get(names.get(bone.parent, ""))
            if edit_bone is None or wanted is None:
                continue
            # Compared by name: Blender hands back a fresh wrapper each time, so
            # an identity test here reparents every bone to where it already is.
            if edit_bone.parent and edit_bone.parent.name == wanted.name:
                continue
            try:
                edit_bone.parent = wanted
            except (RuntimeError, ValueError) as error:
                print("fc2: cannot reparent %s to %s: %s"
                      % (bone.name, wanted.name, error))
                continue
            edit_bone.use_connect = False
            moved.append(bone.name)
    finally:
        bpy.ops.object.mode_set(mode="OBJECT")
        bpy.context.view_layer.objects.active = previous
    return moved


def derived_bones(skeleton):
    """Bones the engine solves rather than reads from a clip, so nothing keys them.

    Sixteen on `pelvis_ref`: the four mid-joint helpers and twelve arm twists.
    Their fields are read (see fc2fmt.skeleton) but where the engine evaluates
    them has not been traced, so nothing here poses them — they simply follow
    their parents.
    """
    return [bone.name for bone in skeleton.bones if bone.ori.kind != ORI_NONE]


def apply(armature, skeleton):
    """Put the armature on the hierarchy the clips are authored against."""
    return {"reparented": reparent(armature, skeleton),
            "derived": derived_bones(skeleton)}
