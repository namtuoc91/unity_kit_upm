// When a GameAudio is added to a scene, ensures the host project has an AudioLibrary asset:
//   Assets/GameKit/Audio/AudioLibraryData.asset
// and assigns it to the component if its library slot is empty.

using UnityEditor;
using UnityEngine;

namespace Raccoon.Audio
{
    [InitializeOnLoad]
    internal static class AudioLibrarySetup
    {
        private const string Folder = "Assets/GameKit/Audio";
        private const string AssetPath = Folder + "/AudioLibraryData.asset";

        static AudioLibrarySetup()
        {
            ObjectFactory.componentWasAdded -= OnComponentAdded;
            ObjectFactory.componentWasAdded += OnComponentAdded;
        }

        private static void OnComponentAdded(Component component)
        {
            if (component is not GameAudio gameAudio)
                return;

            AudioLibrary library = GetOrCreateLibrary();

            // library is private — assign it the same way the Inspector does
            var so = new SerializedObject(gameAudio);
            SerializedProperty prop = so.FindProperty("library");
            if (prop != null && prop.objectReferenceValue == null)
            {
                prop.objectReferenceValue = library;
                so.ApplyModifiedProperties();
            }
        }

        private static AudioLibrary GetOrCreateLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<AudioLibrary>(AssetPath);
            if (library != null)
                return library;

            EnsureFolder(Folder);
            library = ScriptableObject.CreateInstance<AudioLibrary>();
            AssetDatabase.CreateAsset(library, AssetPath);
            AssetDatabase.SaveAssets();

            Debug.Log($"[GameAudio] Created '{AssetPath}'. Add your sounds and musics to it.", library);
            return library;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = "Assets";
            foreach (string part in path.Substring("Assets/".Length).Split('/'))
            {
                string current = $"{parent}/{part}";
                if (!AssetDatabase.IsValidFolder(current))
                    AssetDatabase.CreateFolder(parent, part);
                parent = current;
            }
        }
    }
}
