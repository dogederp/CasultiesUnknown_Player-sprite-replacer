// The settings menu rebuilds its rows in SelectTab. Rebuild the bug report
// row after it, and remove the old row before the rebuild.
public static class BugReportSettingsPatch
{
    static void Prefix()
    {
        BugReportSettings.Remove();
    }

    static void Postfix(int index)
    {
        BugReportSettings.AddIfOurCategory(index);
    }
}
