#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SineWaveGenerator))]
public class SineWaveGeneratorEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();

        var generator = (SineWaveGenerator)target;

        EditorGUILayout.LabelField("Resolved Frequency", $"{generator.CurrentFrequency:F2} Hz");

        serializedObject.ApplyModifiedProperties();

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

