using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;


[CreateAssetMenu(fileName = "SceneListSO", menuName = "Scriptable Objects/SceneListSO")]
public class SceneListSO : ScriptableObject
{
    [Serializable]
    public class SceneListScene
    {
        public string sceneName;
        public AssetReference scene;
        public bool isAddressable;
    }
    
    public  List<SceneListScene> scenes;
    
}
