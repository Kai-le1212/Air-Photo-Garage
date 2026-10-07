using System;
using Microsoft.UI.Dispatching;

namespace AirPhotoGarage.Services;

/// <summary>
/// 把 WinUI 的 <see cref="DispatcherQueue"/> 适配成 <see cref="IUiDispatcher"/>。
/// 这是组合根（App.xaml.cs）中唯一允许引用 <c>Microsoft.UI.Dispatching</c> 类型的地方。
/// </summary>
public sealed class DispatcherQueueAdapter : IUiDispatcher
{
    private readonly DispatcherQueue _queue;

    public DispatcherQueueAdapter(DispatcherQueue queue)
    {
        _queue = queue;
    }

    public bool TryEnqueue(Action action) => _queue.TryEnqueue(() => action());
}
