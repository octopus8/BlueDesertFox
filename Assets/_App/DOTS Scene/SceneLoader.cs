using Unity.Entities;

public partial class SceneLoader : SystemBase
{
    public static SceneLoader Instance;
    
    private Entity loadedSubSceneEntity;
    
    
    protected override void OnCreate()
    {
        base.OnCreate();
        Instance = this;
    }


    public void LoadScene(Hash128 sceneReferenceId)
    {
//        var loadParameters = new SceneSystem.LoadParameters { Flags = SceneLoadFlags.NewInstance };
//        SceneSystem.LoadSceneAsync(World.Unmanaged, sceneReferenceId);

        Unity.Scenes.SceneSystem.LoadSceneAsync(World.Unmanaged, sceneReferenceId);

    }
    
    
    

    
    protected override void OnUpdate()
    {
    }
}
