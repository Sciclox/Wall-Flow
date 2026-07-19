using System.Collections.Generic;

namespace WallFlow.Windows;

public class WorkspaceSessionData
{
    public string Name { get; set; } = "Principal";
    // For backwards compatibility:
    public List<string>? TabUrls { get; set; }
    public List<string>? TabTitles { get; set; }
    public List<bool>? TabHasCustomTitles { get; set; }
    public int SelectedTabIndex { get; set; } = 0;

    // For groups:
    public List<TabGroupSessionData> Groups { get; set; } = new();
    public int SelectedGroupIndex { get; set; } = 0;
}
