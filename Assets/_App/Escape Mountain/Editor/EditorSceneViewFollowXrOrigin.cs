using UnityEditor;
using UnityEngine;

/// <summary>
/// On entering Play mode, locks the Scene view to "XR Origin Hands (XR Rig)" (same as Shift+F).
/// </summary>
[InitializeOnLoad]
static class EditorSceneViewFollowXrOrigin
{
    const string RigName = "XR Origin Hands (XR Rig)";

    static EditorSceneViewFollowXrOrigin()
    {
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode)
            return;

        EditorApplication.delayCall += FollowRig;
    }

    static void FollowRig()
    {
        if (!EditorApplication.isPlaying)
            return;

        GameObject rig = GameObject.Find(RigName);
        if (rig == null)
            return;

        Selection.activeGameObject = rig;

        SceneView sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null)
            return;

        sceneView.FrameSelected(true);
    }
}
