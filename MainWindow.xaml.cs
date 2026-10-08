using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;

namespace AirPhotoGarage;

/// <summary>
/// 应用主窗口。按 winui3-full-skill/snippets/patterns/app-shell.md 的 NavigationView 壳模式：
/// 左栏菜单（照片库 + 设置 FooterMenuItems）+ 右栏 Frame 内容。
/// </summary>
public sealed partial class MainWindow : Window
{
    private static readonly Dictionary<string, Type> _pages = new()
    {
        { "gallery",      typeof(MainPage) },
        { "aircraft",     typeof(Views.GroupedPhotoPage) },
        { "airport",      typeof(Views.GroupedPhotoPage) },
        { "registration", typeof(Views.GroupedPhotoPage) },
        { "settings",     typeof(Views.SettingsPage) },
    };

    /// <summary>
    /// 各菜单项对应的导航参数。分组页共用同一 Page 类型，靠参数区分维度；
    /// 照片墙 / 设置传 null。
    /// </summary>
    private static object? GetNavParameter(string tag) => tag switch
    {
        "aircraft" => Views.GroupedPhotoPageParameter.Aircraft,
        "airport" => Views.GroupedPhotoPageParameter.Airport,
        "registration" => Views.GroupedPhotoPageParameter.Registration,
        _ => null,
    };

    public MainWindow()
    {
        InitializeComponent();

        // Fluent Design 系统背景（详见 theming.md）
        SystemBackdrop = new MicaBackdrop();

        // 主题应用回调：SettingsViewModel 通过这个委托切换 RequestedTheme
        ViewModels.SettingsViewModel.ApplyTheme = ApplyTheme;
    }

    private bool _started;

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigated += ContentFrame_Navigated;

        // 首次启动引导期间不导航：此刻 App 的 ViewModel 尚未创建。
        // 引导结束后由 App 调用 StartAfterSetup() 再进入首页。
        if (Views.FirstRunSetup.IsCompleted)
        {
            StartAfterSetup();
        }
    }

    /// <summary>
    /// 服务与 ViewModel 就绪后进入首页。
    /// 首次启动引导结束（或无需引导）时由 <see cref="App"/> 调用。
    /// 重复调用是安全的。
    /// </summary>
    public void StartAfterSetup()
    {
        if (_started) return;
        _started = true;

        // 默认进入照片墙页
        NavigateTo("gallery");

        // 应用持久化的主题
        var saved = LoadPersistedThemeIndex();
        ApplyTheme(saved);
        if (App.SettingsViewModel is not null)
        {
            App.SettingsViewModel.ThemeIndex = saved;
        }
    }

    /// <summary>从磁盘读上次保存的主题索引（0/1/2），默认 0。</summary>
    private static int LoadPersistedThemeIndex()
    {
        try
        {
            var path = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AirPhotoGarage", "settings.txt");
            if (System.IO.File.Exists(path))
            {
                var s = System.IO.File.ReadAllText(path).Trim();
                if (int.TryParse(s, out var i) && i >= 0 && i <= 2) return i;
            }
        }
        catch { }
        return 0;
    }

    private void NavView_ItemInvoked(NavigationView sender, NavigationViewItemInvokedEventArgs args)
    {
        if (args.InvokedItemContainer?.Tag is string tag)
        {
            NavigateTo(tag);
        }
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        var tag = GetTagByPageType(e.SourcePageType, e.Parameter);
        if (tag is null) return;

        var item = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .Concat(NavView.FooterMenuItems.OfType<NavigationViewItem>())
            .FirstOrDefault(i => i.Tag?.ToString() == tag);
        if (item is not null)
        {
            NavView.SelectedItem = item;
        }

        // 让导航后的页面也应用当前主题
        ApplyTheme(App.SettingsViewModel?.ThemeIndex ?? 0);
    }

    private void NavigateTo(string tag)
    {
        if (!_pages.TryGetValue(tag, out var pageType)) return;

        var param = GetNavParameter(tag);

        // 分组页共用同一 Page：即使 CurrentSourcePageType 相同，只要参数不同也必须重新导航。
        var sameType = ContentFrame.CurrentSourcePageType == pageType;
        if (sameType && param is null) return;   // 照片墙/设置：同页不重复导航

        ContentFrame.Navigate(pageType, param);
    }

    private string? GetTagByPageType(Type pageType, object? parameter)
    {
        // 分组页按参数区分 tag
        if (pageType == typeof(Views.GroupedPhotoPage))
        {
            return parameter switch
            {
                Views.GroupedPhotoPageParameter p when p.Column == "aircraft_model" => "aircraft",
                Views.GroupedPhotoPageParameter p when p.Column == "airport_icao" => "airport",
                Views.GroupedPhotoPageParameter p when p.Column == "registration_number" => "registration",
                _ => "aircraft",
            };
        }
        return _pages.FirstOrDefault(p => p.Value == pageType).Key;
    }

    /// <summary>
    /// 应用主题。0=系统默认 / 1=浅色 / 2=深色。
    /// </summary>
    private void ApplyTheme(int themeIndex)
    {
        var theme = themeIndex switch
        {
            1 => ElementTheme.Light,
            2 => ElementTheme.Dark,
            _ => ElementTheme.Default,
        };
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = theme;
        }
    }
}
