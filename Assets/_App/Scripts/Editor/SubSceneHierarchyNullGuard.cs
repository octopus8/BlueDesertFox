using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.Scenes;

/// <summary>
/// Replaces Entities' Hierarchy SubScene provider, which throws MissingReferenceException when
/// destroyed <see cref="SubScene"/> instances linger in the internal AllSubScenes list.
/// </summary>
[InitializeOnLoad]
static class SubSceneHierarchyNullGuard
{
    static readonly FieldInfo AllSubScenesField = typeof(SubScene).GetField(
        "s_AllSubScenes",
        BindingFlags.NonPublic | BindingFlags.Static);

    static readonly Func<SceneHierarchyHooks.SubSceneInfo[]> SafeProvider = ProvideSubScenesSafe;

    static SubSceneHierarchyNullGuard()
    {
        Install();
        EditorApplication.update += EnsureInstalled;
        EditorApplication.hierarchyChanged += PurgeDestroyedSubScenes;
        EditorApplication.playModeStateChanged += _ =>
        {
            PurgeDestroyedSubScenes();
            Install();
        };
    }

    static void EnsureInstalled()
    {
        if (SceneHierarchyHooks.provideSubScenes != SafeProvider)
            Install();
    }

    static void Install()
    {
        PurgeDestroyedSubScenes();
        SceneHierarchyHooks.provideSubScenes = SafeProvider;
    }

    static void PurgeDestroyedSubScenes()
    {
        if (AllSubScenesField?.GetValue(null) is not List<SubScene> list || list.Count == 0)
            return;

        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i] == null)
                list.RemoveAt(i);
        }
    }

    static bool IsInMainStageSafe(SubScene subScene)
    {
        // Mirrors SubScene.IsInMainStage() without calling the internal API.
        // Must only be called after a Unity null check (destroyed objects throw on .gameObject).
        return !EditorUtility.IsPersistent(subScene.gameObject)
               && StageUtility.GetStageHandle(subScene.gameObject) == StageUtility.GetMainStageHandle();
    }

    static SceneHierarchyHooks.SubSceneInfo[] ProvideSubScenesSafe()
    {
        PurgeDestroyedSubScenes();

        var alive = new List<SubScene>();
        foreach (var subScene in SubScene.AllSubScenes)
        {
            if (subScene == null)
                continue;
            alive.Add(subScene);
        }

        var scenes = new SceneHierarchyHooks.SubSceneInfo[alive.Count];
        var sceneAssets = new HashSet<SceneAsset>();
        int index = 0;

        foreach (var subScene in alive)
        {
            if (subScene == null)
            {
                index++;
                continue;
            }

            bool isSubSceneInMainStage;
            try
            {
                isSubSceneInMainStage = IsInMainStageSafe(subScene);
            }
            catch (MissingReferenceException)
            {
                index++;
                continue;
            }

            var sceneAsset = subScene.SceneAsset;
            bool duplicateSceneAsset = sceneAsset != null && isSubSceneInMainStage && !sceneAssets.Add(sceneAsset);

            if (duplicateSceneAsset)
                scenes[index].sceneName = $"{sceneAsset.name}  (Duplicate Scene)";

            var loadedScene = default(Scene);
            if (isSubSceneInMainStage && !duplicateSceneAsset)
            {
                var candidateScene = subScene.EditingScene;
                if (candidateScene.IsValid() && candidateScene.isSubScene)
                    loadedScene = candidateScene;
            }

            try
            {
                scenes[index].transform = subScene.transform;
                scenes[index].scene = loadedScene;
                scenes[index].sceneAsset = sceneAsset;
                scenes[index].color = subScene.HierarchyColor;
            }
            catch (MissingReferenceException)
            {
                scenes[index] = default;
            }

            index++;
        }

        return scenes;
    }
}
