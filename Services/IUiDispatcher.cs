using System;

namespace AirPhotoGarage.Services;

/// <summary>
/// 抽象 UI 线程调度器。让 ViewModel 不必直接引用 <c>Microsoft.UI.Dispatching.DispatcherQueue</c>
/// （违反 best-practices #2「ViewModels must not reference Microsoft.UI.Xaml.*」）。
///
/// 实现位于 <see cref="DispatcherQueueAdapter"/>。
/// </summary>
public interface IUiDispatcher
{
    /// <summary>将同步操作调度到 UI 线程。返回 true 表示已加入队列。</summary>
    bool TryEnqueue(Action action);
}
