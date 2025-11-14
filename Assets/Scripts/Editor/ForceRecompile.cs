using UnityEditor;
using UnityEditor.Compilation;

public static class ForceRecompile
{
    [MenuItem("Tools/Force Recompile %&r")]  // Ctrl+Alt+R
    public static void RecompileScripts()
    {
        UnityEngine.Debug.Log("Forcing script recompilation...");
        CompilationPipeline.RequestScriptCompilation();
    }
}
