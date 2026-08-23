# The one place that knows how Dunia's conventions differ from Blender's.
#
# Measured rather than inherited: both are Z-up, so geometry needs no axis
# change. The file winds clockwise (D3D) in 113 of 113 sampled meshes, so each
# triangle is reversed; UVs arrive already flipped to bottom-up V in the pack.
# Nothing here rotates the armature.

import mathutils


def triangle(corners):
    """File order is clockwise; Blender wants the opposite."""
    return (corners[2], corners[1], corners[0])


def matrix(rows):
    """A pack's row-major 4x4 as a Blender Matrix."""
    return mathutils.Matrix(rows)


def bone_tail(head, children, fallback=0.05):
    """Aim a bone at the mean of its children, or nudge it along +Y if it has none."""
    if not children:
        return head + mathutils.Vector((0.0, fallback, 0.0))
    mean = sum((mathutils.Vector(c) for c in children), mathutils.Vector()) / len(children)
    if (mean - head).length < 1e-5:
        return head + mathutils.Vector((0.0, fallback, 0.0))
    return mean
