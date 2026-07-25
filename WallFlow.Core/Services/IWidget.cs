using System;

namespace WallFlow.Services;

public interface IWidget
{
    string Name { get; }
    void Open();
    void Close();
    bool IsOpen { get; }
    event EventHandler? Closed;
}
