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
        { "gallery",  typeof(MainPage) },
        { "settings", typeof(Views.SettingsPage) },
    };

    public MainWindow()
    {
        InitializeComponent();

        // Fluent Design 系统背景（详见 theming.md）
        SystemBackdrop = new MicaBackdrop();

        // 主题应用回调：SettingsViewModel 通过这个委托切换 RequestedTheme
        ViewModels.SettingsViewModel.ApplyTheme = ApplyTheme;
    }

    private void NavView_Loaded(object sender, RoutedEventArgs e)
    {
        ContentFrame.Navigated += ContentFrame_Navigated;
        // 默认进入照片库页
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
        var pageType = e.SourcePageType;
        var tag = _pages.FirstOrDefault(p => p.Value == pageType).Key;
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
        if (_pages.TryGetValue(tag, out var pageType) &&
            ContentFrame.CurrentSourcePageType != pageType)
        {
            ContentFrame.Navigate(pageType);
        }
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
