using System;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;


namespace App.StartScene
{
    
    public class UI : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                OnLoadScene();
            }
        }


        public void OnLoadScene()
        {
            Addressables.LoadSceneAsync("Assets/_App/StartScene/AutoHand Demo.unity").Completed += handle =>
            {
                if (handle.Status == AsyncOperationStatus.Succeeded)
                {
                    SceneManager.SetActiveScene(handle.Result.Scene); 
                }
            };
            
            
//            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
        }
    }
    
}

