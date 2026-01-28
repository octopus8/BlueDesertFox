using System;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace App.StartScene
{
    public class SceneListButton : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI buttonText;
        
        private AssetReference assetReference;
        
        public void Init(string title, AssetReference assetReference)
        {
            buttonText.text = title;
            this.assetReference = assetReference;
        }

        public AssetReference GetAssetReference()
        {
            return assetReference;
        }
    }
    
}
