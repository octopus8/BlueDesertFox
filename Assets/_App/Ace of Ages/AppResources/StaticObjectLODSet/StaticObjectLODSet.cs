using UnityEngine;

[CreateAssetMenu(fileName = "StaticObjectLODSet", menuName = "Scriptable Objects/Static Object LOD Set")]
public class StaticObjectLODSet : ScriptableObject
{
    public string name;
    public GameObject lod0;
    public GameObject lod1;
    public GameObject lod2;
    public float objectTypeSpawnWeight;
    public bool lod2IsBillboard;
}
