
using Unity.Burst;
using Unity.Entities;

partial struct DottyStartupSystem : ISystem
{ 
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PrefabEntitiesReferences>();
        testval = false;
    }

    private bool testval;
    
    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
/*        
        if (!testval)
        {
            testval = true;
            PrefabEntitiesReferences prefabEntitiesReferences = SystemAPI.GetSingleton<PrefabEntitiesReferences>();

            state.EntityManager.Instantiate(prefabEntitiesReferences.prefabEntity);
        }
*/        
    }
}

