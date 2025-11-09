#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SineWaveGenerator))]
public class SineWaveGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        if (target == null)
            return;

        serializedObject.Update();
        Editor.DrawPropertiesExcluding(serializedObject, "m_Script");
        serializedObject.ApplyModifiedProperties();

        var generator = target as SineWaveGenerator;
        if (generator == null)
            return;

        EditorGUILayout.LabelField("Resolved Frequency", $"{generator.CurrentFrequency:F2} Hz");

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Live Controls", EditorStyles.boldLabel);

        if (!EditorApplication.isPlaying)
        {
            EditorGUILayout.HelpBox("Enter Play Mode to trigger audio.", MessageType.Info);
            return;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Note On"))
            generator.NoteOn();
        if (GUILayout.Button("Note Off"))
            generator.NoteOff();
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Trigger One Shot"))
            generator.TriggerOneShot();
    }
}
#endif

