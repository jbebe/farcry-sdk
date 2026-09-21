using System.Numerics;

namespace JackAll.Tools.World;

/// <summary>Which entity a click ray lands on, tested against each entity's local box.</summary>
public static class EntityPicking
{
    /// <summary>
    /// The entity whose box the ray enters first, or null. Testing the real bounds rather than the
    /// projected origin makes a model clickable anywhere on its body, and taking the nearest hit
    /// means the thing in front wins.
    /// </summary>
    /// <param name="bounds">An entity's box in its own space, before rotation and placement.</param>
    public static WorldEntity? Pick(
        Vector3 origin, Vector3 direction, IEnumerable<WorldEntity> entities,
        Func<WorldEntity, (Vector3 Min, Vector3 Max)> bounds)
    {
        WorldEntity? best = null;
        float bestDistance = float.MaxValue;
        foreach (WorldEntity entity in entities)
        {
            if (entity.Position is not { } position)
            {
                continue;
            }

            // Into the entity's own space rather than growing its box to fit the world axes, so a
            // rotated building is no easier to miss than an unrotated one.
            Matrix4x4 inverse = Matrix4x4.Transpose(entity.Rotation);
            Vector3 localOrigin = Vector3.Transform(origin - position, inverse);
            Vector3 localDirection = Vector3.Transform(direction, inverse);

            (Vector3 min, Vector3 max) = bounds(entity);
            if (RayHitsBox(localOrigin, localDirection, min, max) is { } distance && distance < bestDistance)
            {
                bestDistance = distance;
                best = entity;
            }
        }
        return best;
    }

    /// <summary>Slab test; the near hit distance along the ray, or null when it misses. A ray starting
    /// inside the box hits at zero.</summary>
    public static float? RayHitsBox(Vector3 origin, Vector3 direction, Vector3 min, Vector3 max)
    {
        float near = 0f, far = float.MaxValue;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = origin[axis], d = direction[axis];
            if (MathF.Abs(d) < 1e-6f)
            {
                if (o < min[axis] || o > max[axis])
                {
                    return null;
                }
                continue;
            }

            float t1 = (min[axis] - o) / d, t2 = (max[axis] - o) / d;
            if (t1 > t2)
            {
                (t1, t2) = (t2, t1);
            }

            near = MathF.Max(near, t1);
            far = MathF.Min(far, t2);
            if (near > far)
            {
                return null;
            }
        }
        return near;
    }
}
