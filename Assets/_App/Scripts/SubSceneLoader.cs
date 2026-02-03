using Unity.Entities;


/// <summary>
/// This Singleton system is used to load ECS subscenes.
/// </summary>
/// <remarks>
/// - MonoBehaviour components call `LoadScene` on the Singleton instance to load subscenes.
/// - `SceneStartup` uses this to load subscenes.
/// </remarks>
public partial class SubSceneLoader : SystemBase
{
    /// <summary>
    /// The Singleton instance.
    /// </summary>
    public static SubSceneLoader Instance;

    
    /// <summary>
    /// Stores the Singleton reference.
    /// </summary>
    protected override void OnCreate()
    {
        base.OnCreate();
        Instance = this;
    }


    /// <summary>
    /// Loads the specified scene.
    /// </summary>
    public void LoadScene(Hash128 sceneReferenceId)
    {
        Unity.Scenes.SceneSystem.LoadSceneAsync(World.Unmanaged, sceneReferenceId);
    }


    /// <summary>
    /// Does nothing, but is required to be defined.
    /// </summary>
    protected override void OnUpdate()
    {
    }
}
