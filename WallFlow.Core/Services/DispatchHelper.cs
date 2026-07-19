using System;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace WallFlow.Services;

public static class DispatchHelper
{
    public static void RunOnUI(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null) return;

        if (dispatcher.CheckAccess())
        {
            RunWithSyncContext(dispatcher, action);
        }
        else
        {
            dispatcher.Invoke(new Action(() => RunWithSyncContext(dispatcher, action)));
        }
    }

    private static void RunWithSyncContext(Dispatcher dispatcher, Action action)
    {
        var oldCtx = SynchronizationContext.Current;
        var dsc = new DispatcherSynchronizationContext(dispatcher);
        SynchronizationContext.SetSynchronizationContext(dsc);
        try
        {
            action();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(oldCtx);
        }
    }
}
