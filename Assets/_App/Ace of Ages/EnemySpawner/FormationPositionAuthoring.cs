using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Component that defines an entity's position within a formation.
/// Used to maintain consistent formation spacing while following a spline.
/// </summary>
public struct FormationPosition : IComponentData
{
    /// <summary>The position in the formation (0-9 for a 10-pin bowling formation)</summary>
    public int positionIndex;
    
    /// <summary>The lateral offset from the center spline path (perpendicular to movement direction)</summary>
    public float3 lateralOffset;
    
    /// <summary>The forward/backward offset along the spline (affects distanceRatio)</summary>
    public float forwardOffset;
}


