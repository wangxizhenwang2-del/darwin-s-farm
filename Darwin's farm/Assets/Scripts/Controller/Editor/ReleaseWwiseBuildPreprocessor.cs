#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

// Wwise defaults to Profile for the editor. Select its Release native plugins
// before Wwise's own build preprocessor runs for a shipping Windows player.
public sealed class ReleaseWwiseBuildPreprocessor : IPreprocessBuildWithReport
{
    public int callbackOrder => -100;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.StandaloneWindows64 ||
            (report.summary.options & BuildOptions.Development) != 0)
        {
            return;
        }

        AkWwiseProjectInfo.GetData().CurrentPluginConfig = AkPluginActivatorConstants.CONFIG_RELEASE;
        AkPluginActivatorMenus.CheckMenuItems(AkPluginActivatorConstants.CONFIG_RELEASE);
    }
}
#endif
