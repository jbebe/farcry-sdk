# The one place that knows how Dunia's conventions differ from Blender's.
#
# Measured rather than inherited: both are Z-up, so geometry needs no axis
# change. The file winds clockwise (D3D) in 113 of 113 sampled meshes, so each
# triangle is reversed; UVs arrive already flipped to bottom-up V in the pack.
# A Dunia node points down its X axis at its child and a Blender bone down Y, so
# every bone is its node turned a quarter about Z.

import math

import mathutils

BONE_AXES = mathutils.Matrix.Rotation(-math.pi / 2.0, 4, "Z")
_NODE_AXES = BONE_AXES.inverted()
_TURN = BONE_AXES.to_quaternion()
_TURN_BACK = _TURN.inverted()


def triangle(corners):
    """File order is clockwise; Blender wants the opposite."""
    return (corners[2], corners[1], corners[0])


def matrix(rows):
    """A pack's row-major 4x4 as a Blender Matrix."""
    return mathutils.Matrix(rows)


def node_frame(bone_matrix):
    """The Dunia node a bone was built from."""
    return bone_matrix @ _NODE_AXES


def node_local(bone_matrix, parent_matrix=None):
    """A bone's node relative to its parent's, from the two bones' matrices."""
    node = node_frame(bone_matrix)
    if parent_matrix is None:
        return node
    return node_frame(parent_matrix).inverted() @ node


def to_bone(rotation):
    """A pose rotation worked out between nodes, carried into the bone's frame."""
    return _TURN_BACK @ rotation @ _TURN


def offset_to_bone(offset):
    """A pose offset worked out between nodes, carried into the bone's frame."""
    return _TURN_BACK @ offset


def to_node(rotation):
    """The inverse of `to_bone`."""
    return _TURN @ rotation @ _TURN_BACK


def bone_length(head, children, fallback=0.05):
    """How far a bone reaches: to the mean of its children, or a nudge if it has none."""
    if not children:
        return fallback
    mean = sum((mathutils.Vector(c) for c in children), mathutils.Vector()) / len(children)
    return (mean - head).length if (mean - head).length >= 1e-5 else fallback
