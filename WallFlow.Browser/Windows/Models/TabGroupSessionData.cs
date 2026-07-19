using System.Collections.Generic;

namespace WallFlow.Windows;

public class TabGroupSessionData
{
    public string Name { get; set; } = "Grupo 1";
    public List<string> TabUrls { get; set; } = new();
    public List<string> TabTitles { get; set; } = new();
    public List<bool> TabHasCustomTitles { get; set; } = new();
    public int SelectedTabIndex { get; set; } = 0;
    public bool IsActive { get; set; } = false;
}
