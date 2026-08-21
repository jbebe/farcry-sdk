# 4x4 transform helpers, in plain Python so the codec stays free of bpy.
#
# Matrices are row-major nested tuples and multiply as parent x child, matching
# CGeomResource::GenerateMatrices.

IDENTITY = ((1.0, 0.0, 0.0, 0.0),
            (0.0, 1.0, 0.0, 0.0),
            (0.0, 0.0, 1.0, 0.0),
            (0.0, 0.0, 0.0, 1.0))


def trs_matrix(rotation, translation, scale=(1.0, 1.0, 1.0)):
    """Compose a node's local transform from its quaternion, offset and scale."""
    x, y, z, w = rotation
    sx, sy, sz = scale
    return (
        ((1.0 - 2.0 * (y * y + z * z)) * sx, 2.0 * (x * y - w * z) * sy,
         2.0 * (x * z + w * y) * sz, translation[0]),
        (2.0 * (x * y + w * z) * sx, (1.0 - 2.0 * (x * x + z * z)) * sy,
         2.0 * (y * z - w * x) * sz, translation[1]),
        (2.0 * (x * z - w * y) * sx, 2.0 * (y * z + w * x) * sy,
         (1.0 - 2.0 * (x * x + y * y)) * sz, translation[2]),
        (0.0, 0.0, 0.0, 1.0))


def multiply(a, b):
    return tuple(tuple(sum(a[r][k] * b[k][c] for k in range(4)) for c in range(4))
                 for r in range(4))


def apply(matrix, point):
    return tuple(sum(matrix[r][c] * point[c] for c in range(3)) + matrix[r][3]
                 for r in range(3))


def apply_direction(matrix, vector):
    """Rotate without translating, for normals and tangents."""
    return tuple(sum(matrix[r][c] * vector[c] for c in range(3)) for r in range(3))


def is_identity(matrix, tolerance=1e-6):
    return all(abs(matrix[r][c] - IDENTITY[r][c]) <= tolerance
               for r in range(4) for c in range(4))
