using System;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace WallFlow.Services;

public class CompositionWebViewTab : IDisposable
{
    private CoreWebView2Controller? _controller;
    private CoreWebView2? _coreWebView2;

    public CoreWebView2Controller? Controller => _controller;
    public CoreWebView2? CoreWebView2 => _coreWebView2;

    public Uri? Source
    {
        get
        {
            Uri? result = null;
            DispatchHelper.RunOnUI(() =>
            {
                var cwv = _coreWebView2;
                if (cwv?.Source is string s && Uri.TryCreate(s, UriKind.Absolute, out var uri))
                    result = uri;
            });
            return result;
        }
        set
        {
            if (value != null)
            {
                DispatchHelper.RunOnUI(() =>
                {
                    _coreWebView2?.Navigate(value.ToString());
                });
            }
        }
    }

    public bool IsActive { get; private set; }

    /// <summary>
    /// Creates the composition controller. The creation is dispatched to the
    /// UI thread and blocks until the controller is fully created, ensuring
    /// the COM handler fires on the UI thread (correct thread affinity).
    /// </summary>
    public async Task InitializeAsync(CoreWebView2Environment env, IntPtr parentWindow)
    {
        var uiDispatcher = System.Windows.Application.Current.Dispatcher;

        if (uiDispatcher.CheckAccess())
        {
            var tcs = new TaskCompletionSource<bool>();
            _ = env.CreateCoreWebView2ControllerAsync(parentWindow).ContinueWith(t =>
            {
                uiDispatcher.Invoke(() =>
                {
                    if (t.IsFaulted)
                    {
                        tcs.SetException(t.Exception!.InnerException ?? t.Exception);
                    }
                    else if (t.IsCanceled)
                    {
                        tcs.SetCanceled();
                    }
                    else
                    {
                        try
                        {
                            var controller = t.Result;
                            _controller = controller;
                            _coreWebView2 = controller?.CoreWebView2;
                            tcs.SetResult(true);
                        }
                        catch (Exception ex)
                        {
                            tcs.SetException(ex);
                        }
                    }
                });
            });

            await tcs.Task;
        }
        else
        {
            var tcs = new TaskCompletionSource<bool>();
            _ = uiDispatcher.BeginInvoke(new Action(() =>
            {
                _ = env.CreateCoreWebView2ControllerAsync(parentWindow).ContinueWith(t =>
                {
                    uiDispatcher.Invoke(() =>
                    {
                        if (t.IsFaulted)
                        {
                            tcs.SetException(t.Exception!.InnerException ?? t.Exception);
                        }
                        else if (t.IsCanceled)
                        {
                            tcs.SetCanceled();
                        }
                        else
                        {
                            try
                            {
                                var controller = t.Result;
                                _controller = controller;
                                _coreWebView2 = controller?.CoreWebView2;
                                tcs.SetResult(true);
                            }
                            catch (Exception ex)
                            {
                                tcs.SetException(ex);
                            }
                        }
                    });
                });
            }));

            try
            {
                tcs.Task.Wait();
            }
            catch (AggregateException aggEx)
            {
                throw aggEx.InnerException ?? aggEx;
            }
        }
    }

    public void Attach(Visual hostVisual)
    {
        DispatchHelper.RunOnUI(() =>
        {
            if (_controller == null) return;
            _controller.IsVisible = true;
            IsActive = true;
        });
    }

    public void Detach()
    {
        DispatchHelper.RunOnUI(() =>
        {
            if (_controller == null) return;
            _controller.IsVisible = false;
            IsActive = false;
        });
    }

    public void Resize(double width, double height)
    {
        DispatchHelper.RunOnUI(() =>
        {
            if (_controller == null) return;
            _controller.Bounds = new System.Drawing.Rectangle(0, 0, (int)width, (int)height);
        });
    }

    public void Dispose()
    {
        DispatchHelper.RunOnUI(() =>
        {
            if (_controller == null) return;
            try
            {
                _controller.Close();
            }
            catch { }
            _controller = null;
            _coreWebView2 = null;
            IsActive = false;
        });
    }
}
