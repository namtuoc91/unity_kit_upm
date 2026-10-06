// Raccoon → Localization → Add LocalizedFont to Scene
//
// Scans the active scene for GameObjects that have LocalizedText
// + TMP_Text OR Text (legacy) but are missing LocalizedFont,
// and adds the component to all of them in one click.

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
#if USING_TMP
using TMPro;
#endif

namespace Raccoon.Localization
{
    public static class LocalizedFontSetupTool
    {
        [MenuItem("Raccoon/Localization/Add LocalizedFont to Scene")]
        private static void Run()
        {
            var tmpHits = new List<GameObject>(); // has TMP_Text
            var legacyHits = new List<GameObject>(); // has legacy Text

            var stack = new Stack<GameObject>();
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                stack.Push(root);

            while (stack.Count > 0)
            {
                var go = stack.Pop();
                foreach (Transform child in go.transform)
                    stack.Push(child.gameObject);

                bool hasLocalized = go.GetComponent<LocalizedText>() != null;
                bool hasFont = go.GetComponent<LocalizedFont>() != null;

                if (!hasLocalized || hasFont) continue; // skip ineligible

#if USING_TMP
                if (go.GetComponent<TMP_Text>() != null)
                    tmpHits.Add(go);
                else
#endif
                if (go.GetComponent<Text>() != null)
                    legacyHits.Add(go);
            }

            int total = tmpHits.Count + legacyHits.Count;

            if (total == 0)
            {
                EditorUtility.DisplayDialog("LocalizedFont Setup",
                    "No eligible GameObjects found.\n\n" +
                    "Objects need LocalizedText + TMP_Text or Text (Legacy), " +
                    "but no LocalizedFont yet.", "OK");
                return;
            }

            string msg =
                $"Found {total} eligible object(s):\n" +
                $"  • {tmpHits.Count} with TMP_Text\n" +
                $"  • {legacyHits.Count} with legacy Text\n\n" +
                "Add LocalizedFont to all of them?";

            if (!EditorUtility.DisplayDialog("LocalizedFont Setup", msg, "Add to all", "Cancel"))
                return;

            foreach (var go in tmpHits) Undo.AddComponent<LocalizedFont>(go);
            foreach (var go in legacyHits) Undo.AddComponent<LocalizedFont>(go);

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[LocalizedFontSetup] Added LocalizedFont to {total} object(s) " +
                      $"({tmpHits.Count} TMP, {legacyHits.Count} Legacy).");

            EditorUtility.DisplayDialog("Done",
                $"Added LocalizedFont to {total} object(s).\n\n" +
                "TMP objects  → assign TMP font slots in the Inspector.\n" +
                "Legacy objects → assign Legacy font slots in the Inspector.\n\n" +
                "Any slot left null falls back to the font already on the component.",
                "Got it");
        }

        [MenuItem("Raccoon/Localization/Add LocalizedFont to Scene", validate = true)]
        private static bool Validate()
            => !Application.isPlaying && SceneManager.GetActiveScene().IsValid();
    }
}
