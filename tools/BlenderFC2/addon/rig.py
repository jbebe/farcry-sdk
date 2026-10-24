# Reconcile an armature built from an .xbg with the .skeleton clips animate.
#
# The two trees disagree, and the disagreement is what tears a character's knees
# apart. On `pelvis_ref` the four mid-joint helpers — L/R Knee and L/R Elbow —
# hang off the Pelvis in the .xbg but off the thigh and upper arm they sit on in
# the .skeleton. Posed on the .xbg tree a knee helper stays by the hip while the
# leg swings, and the mesh weighted to it stretches into spikes.
#
# The parent ids come from CSkeletonResource::SerializeBone.


def translates(bone):
    """Whether a clip may move this bone; any other bone only rotates."""
    return bool(bone.get("animated_translation"))


def parents(skeleton):
    """Each bone's parent by name, as the .skeleton has it; a root names none."""
    names = {bone["id"]: bone["name"] for bone in skeleton["bones"]}
    return {bone["name"]: names[bone["parent"]] for bone in skeleton["bones"]
            if bone["parent"] in names}
