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
        public string sceneDisplayName;
        public bool isAddressable;
        public AssetReference scene;
        public string scenePath;
    }
    
    public  List<SceneListScene> scenes;
    
}
