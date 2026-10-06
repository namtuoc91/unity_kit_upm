// When a LocalizationManager is added to a scene, ensures the Resources folder
// it loads locale files from exists in the host project:
//   Assets/GameKit/Resources/<resourcesPath>

using UnityEditor;
using UnityEngine;

namespace Raccoon.Localization
{
    [InitializeOnLoad]
    internal static class LocalizationResourcesSetup
    {
        private const string RootFolder = "Assets/GameKit/Resources";
        private const string DefaultResourcesPath = "Localization";

        static LocalizationResourcesSetup()
        {
            ObjectFactory.componentWasAdded -= OnComponentAdded;
            ObjectFactory.componentWasAdded += OnComponentAdded;
        }

        private static void OnComponentAdded(Component component)
        {
            if (component is not LocalizationManager manager)
                return;

            // resourcesPath is private — read it the same way the Inspector does
            var so = new SerializedObject(manager);
            string resourcesPath = so.FindProperty("resourcesPath")?.stringValue;
            if (string.IsNullOrWhiteSpace(resourcesPath))
                resourcesPath = DefaultResourcesPath;

            EnsureFolder($"{RootFolder}/{resourcesPath.Trim('/')}");
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

            Debug.Log($"[Localization] Created folder '{path}'. Put your locale files (en.csv, vi.csv, ...) here.");
        }
    }
}
