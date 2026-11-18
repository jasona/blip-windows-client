using Avalonia.Threading;

namespace Blip.Windows.Services;

/// <summary>The UI thread, for view models that are fed from SSH threads.</summary>
public static class Ui
{
    public static Task InvokeAsync(Action a) =>
        Dispatcher.UIThread.CheckAccess() ? Run(a) : Dispatcher.UIThread.InvokeAsync(a).GetTask();

    public static void Post(Action a) => Dispatcher.UIThread.Post(a);

    private static Task Run(Action a)
    {
        a();
        return Task.CompletedTask;
    }
}
