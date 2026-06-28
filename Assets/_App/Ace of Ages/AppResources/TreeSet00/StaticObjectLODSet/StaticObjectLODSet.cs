using UnityEngine;

[System.Serializable]
public struct StaticObjectLODSetEntry
{
    public StaticObjectLODSet lodSet;
    [Min(0f)] public float spawnWeight;
    public float maxScaleDelta;
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
