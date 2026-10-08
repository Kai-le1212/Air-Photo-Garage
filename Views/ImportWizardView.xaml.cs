using System;
using System.Collections.Generic;
using System.Linq;
using AirPhotoGarage.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace AirPhotoGarage.Views;

public sealed partial class ImportWizardView : UserControl
{
    public ImportWizardViewModel ViewModel { get; }

    private IReadOnlyList<string> _currentSuggestions = Array.Empty<string>();
    private int _selectedIndex = -1;

    public ImportWizardView(ImportWizardViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        HookModelSuggestions();
    }

    public ImportWizardView()
    {
        InitializeComponent();
        ViewModel = App.ImportWizardViewModel;
        HookModelSuggestions();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelCommand.Execute(null);
    }

    // ---------- 机型候选下拉 ----------

    /// <summary>
    /// 为机型输入框挂上候选逻辑：输入即搜索、上下键选择、Enter/Tab 采用、失焦收起。
    /// 与 MainPage 编辑对话框保持同一交互模式。
    /// </summary>
    private void HookModelSuggestions()
    {
        TbModel.TextChanged += (_, _) => RefreshSuggestions();

        TbModel.LostFocus += (_, _) =>
        {
            // 延迟到焦点转移完成后再判断，避免点候选时被误收起
            DispatcherQueue.TryEnqueue(() =>
            {
                var focused = FocusManager.GetFocusedElement(XamlRoot) as DependencyObject;
                if (IsDescendantOf(focused, ListModelSuggestion)) return;
                HideSuggestions();
            });
        };

        TbModel.AddHandler(UIElement.KeyDownEvent, new KeyEventHandler(OnModelKeyDown), handledEventsToo: true);
    }

    private void OnModelKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (_currentSuggestions.Count == 0) return;
        switch (e.Key)
        {
            case VirtualKey.Down:
                _selectedIndex = Math.Min(_selectedIndex + 1, _currentSuggestions.Count - 1);
                ListModelSuggestion.SelectedIndex = _selectedIndex;
                e.Handled = true;
                break;
            case VirtualKey.Up:
                _selectedIndex = Math.Max(_selectedIndex - 1, 0);
                ListModelSuggestion.SelectedIndex = _selectedIndex;
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                PickCurrent();
                e.Handled = true;
                break;
            case VirtualKey.Tab:
                PickCurrent();
                break;  // 不拦截 Tab，保留正常焦点转移
            case VirtualKey.Escape:
                HideSuggestions();
                e.Handled = true;
                break;
        }
    }

    private void RefreshSuggestions()
    {
        var results = ViewModel.AircraftSuggestions(TbModel.Text ?? "").ToList();
        _currentSuggestions = results;
        ListModelSuggestion.ItemsSource = results;

        if (results.Count > 0)
        {
            _selectedIndex = 0;
            ListModelSuggestion.SelectedIndex = 0;
            ListModelSuggestion.Visibility = Visibility.Visible;
        }
        else
        {
            HideSuggestions();
        }
    }

    private void HideSuggestions()
    {
        _selectedIndex = -1;
        ListModelSuggestion.SelectedIndex = -1;
        ListModelSuggestion.Visibility = Visibility.Collapsed;
    }

    private void PickCurrent()
    {
        string pick;
        if (_selectedIndex >= 0 && _selectedIndex < _currentSuggestions.Count)
            pick = _currentSuggestions[_selectedIndex];
        else if (_currentSuggestions.Count > 0)
            pick = _currentSuggestions[0];
        else
            return;

        TbModel.Text = pick;
        ViewModel.AircraftModel = pick;   // 同步回 ViewModel，确保保存时不丢
        HideSuggestions();
    }

    private void OnModelSuggestionClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not string pick) return;
        TbModel.Text = pick;
        ViewModel.AircraftModel = pick;
        HideSuggestions();
    }

    /// <summary>
    /// 判断节点是否是某祖先本身或其视觉树子孙（ListView 内实际聚焦的是 ListViewItem）。
    /// </summary>
    private static bool IsDescendantOf(DependencyObject? node, DependencyObject ancestor)
    {
        for (var cur = node; cur is not null; cur = VisualTreeHelper.GetParent(cur))
        {
            if (ReferenceEquals(cur, ancestor)) return true;
        }
        return false;
    }
}
