using UnityEngine;

// Creates the chess app when Play starts, in any scene, without editing the scene.
public static class Bootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        if (Object.FindAnyObjectByType<ChessApp>() != null) return;
        var go = new GameObject("ChessApp");
        go.AddComponent<ChessApp>();
        Object.DontDestroyOnLoad(go);
    }
}
