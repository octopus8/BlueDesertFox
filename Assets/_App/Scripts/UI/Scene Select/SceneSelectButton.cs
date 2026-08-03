using LiquidForce;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.UI;

    
[RequireComponent(typeof(Button))]
public class SceneSelectButton : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI buttonText;
        
        
    private AssetReference assetReference;
    private bool isAddressable = false;

    private SceneSelectUIState sceneSelectUIState;
        
    private SceneListSO.SceneListScene  scene;
        
    private Button button;
        
        
    public void Init(SceneListSO.SceneListScene buttonScene, SceneSelectUIState sceneSelectUIState)
    {
        button = GetComponent<Button>();
        scene = buttonScene;
        this.sceneSelectUIState = sceneSelectUIState;
        buttonText.text = buttonScene.sceneDisplayName;
        button.onClick.AddListener(LoadScene);
        gameObject.SetActive(true);
    }

    private void LoadScene()
    {
        sceneSelectUIState.LoadScene(scene);
    }

    public AssetReference GetAssetReference()
    {
        return assetReference;
    }
        
}
