using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time setup utility. Run from the menu after opening Game.unity.
/// Adds a BulletPool GameObject to the active scene and pre-populates it
/// with all bullet prefab entries so you only need to set pool sizes.
/// </summary>
public static class BulletPoolSetup
{
    private static readonly string[] k_prefabPaths =
    {
        "Assets/_Game/Entities/Bullets/Bullet.prefab",
        "Assets/_Game/Entities/Bullets/CannonBullet.prefab",
        "Assets/_Game/Entities/Bullets/GatlingBullet.prefab",
        "Assets/_Game/Entities/Bullets/RailgunBullet.prefab",
        "Assets/_Game/Entities/Bullets/ArtyBullet.prefab",
    };

    // Default warm-up sizes per type (tune to match expected on-screen counts).
    private static readonly int[] k_defaultSizes = { 32, 32, 128, 32, 16 };

    [MenuItem("Last Bastion/Setup/Add BulletPool to Scene")]
    public static void AddBulletPoolToScene()
    {
        if (Object.FindFirstObjectByType<BulletPool>() != null)
        {
            EditorUtility.DisplayDialog("BulletPool", "A BulletPool already exists in this scene.", "OK");
            return;
        }

        var go = new GameObject("BulletPool");
        var pool = go.AddComponent<BulletPool>();

        var entries = new BulletPool.Entry[k_prefabPaths.Length];
        for (int i = 0; i < k_prefabPaths.Length; i++)
        {
            var prefabGO = AssetDatabase.LoadAssetAtPath<GameObject>(k_prefabPaths[i]);
            entries[i] = new BulletPool.Entry
            {
                Prefab      = prefabGO != null ? prefabGO.GetComponent<Bullet>() : null,
                InitialSize = k_defaultSizes[i],
            };

            if (prefabGO == null)
                Debug.LogWarning($"[BulletPoolSetup] Could not find prefab at {k_prefabPaths[i]}");
        }

        // Use SerializedObject so the change is recorded by the undo system.
        var so = new SerializedObject(pool);
        var entriesProp = so.FindProperty("Entries");
        entriesProp.arraySize = entries.Length;
        for (int i = 0; i < entries.Length; i++)
        {
            var elem = entriesProp.GetArrayElementAtIndex(i);
            elem.FindPropertyRelative("Prefab").objectReferenceValue      = entries[i].Prefab;
            elem.FindPropertyRelative("InitialSize").intValue              = entries[i].InitialSize;
        }
        so.ApplyModifiedProperties();

        Undo.RegisterCreatedObjectUndo(go, "Add BulletPool");
        Selection.activeGameObject = go;

        EditorUtility.DisplayDialog("BulletPool",
            "BulletPool added and pre-populated.\n\nReview the pool sizes in the Inspector, then save the scene.",
            "OK");
    }
}
