#if UNITY_EDITOR
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(HiHat))]
public class HiHatEditor : OdinEditor
{
    public override void OnInspectorGUI()
    {
        base.OnInspectorGUI();

        var hihat = target as HiHat;
        if (hihat == null)
            return;

        // Runtime info display
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Runtime Info", EditorStyles.boldLabel);

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to see runtime information and trigger sounds.", MessageType.Info);
            return;
        }

        // Show playing status with color
        var prevColor = GUI.color;
        GUI.color = hihat.IsPlaying ? Color.green : Color.gray;
        EditorGUILayout.LabelField("Status", hihat.IsPlaying ? "Playing" : "Idle");
        GUI.color = prevColor;

        // Repaint during play mode to update status
        if (Application.isPlaying)
        {
            Repaint();
        }
    }
}
#endif
