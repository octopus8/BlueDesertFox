using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace LiquidForce
{
    [CreateAssetMenu(fileName = "SceneListSO", menuName = "Scriptable Objects/SceneListSO")]
    
    
    public class SceneListSO : ScriptableObject
    {
        [Serializable]
        public class SceneListScene
        {
            /// <summary>The display name for the scene.</summary>
            public string sceneDisplayName;
            
            /// <summary>Flag indicating scene is an addressable.</summary>
            public bool isAddressable;
            
            /// <summary>
            /// 
            /// </summary>
            public AssetReference scene;
            public string scenePath;
        }
    
        public  List<SceneListScene> scenes;
    
    }
    
}

