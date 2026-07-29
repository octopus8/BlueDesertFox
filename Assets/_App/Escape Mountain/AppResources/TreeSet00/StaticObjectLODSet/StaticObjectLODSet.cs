using UnityEngine;

[System.Serializable]
public struct StaticObjectLODSetEntry
{
    public StaticObjectLODSet lodSet;
    [Min(0f)] public float spawnWeight;
    public float maxScaleDelta;

    [Tooltip("Minimum slope angle in degrees (0 = flat, 90 = vertical cliff). Objects won't spawn on flatter slopes.")]
    [Range(0f, 90f)]
    public float minSlopeDegrees;

    [Tooltip("Maximum slope angle in degrees (0 = flat, 90 = vertical cliff). Objects won't spawn on steeper slopes.")]
    [Range(0f, 90f)]
    public float maxSlopeDegrees;
}

[CreateAssetMenu(fileName = "StaticObjectLODSet", menuName = "Scriptable Objects/Static Object LOD Set")]
public class StaticObjectLODSet : ScriptableObject
{
    public string name;
    public GameObject lod0;
    public GameObject lod1;
    public GameObject lod2;
    public bool lod2IsBillboard;
}
