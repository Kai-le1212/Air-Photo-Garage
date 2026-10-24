using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace AirPhotoGarage.Views;

/// <summary>批量设置航司的确认结果。</summary>
internal sealed class AirlineAssignResult
{
    public required IReadOnlyList<string> Registrations { get; init; }
    public string? AirlineName { get; init; }
    public string? AirlineIata { get; init; }
}

/// <summary>
/// 「批量设置航空公司」对话框：勾选多架飞机（按注册号），一次写好航司。
///
/// <para>
/// 航司名称与二字码用与照片编辑同一套 <see cref="AirlineLinker"/> 互填，
/// 所以这里不用再重复一遍补齐逻辑。
/// </para>
///
/// <para>
/// 只负责收集选择，落库交给调用方（<c>DatabaseService.AssignAirlineAsync</c>）——
/// 那里会连带把缺失的机型按同注册号的已知机型补上。
/// </para>
/// </summary>
internal static class AirlineAssignDialog
{
    public static async Task<AirlineAssignResult?> ShowAsync(
        XamlRoot root, IReadOnlyList<string> registrations)
    {
        if (registrations.Count == 0) return null;

        var list = new StackPanel { Spacing = 0 };
        var boxes = new List<(string Registration, CheckBox Box)>();

        foreach (var registration in registrations)
        {
            var box = new CheckBox
            {
                Content = registration,
                MinWidth = 0,
                IsChecked = false,
            };
            boxes.Add((registration, box));
            list.Children.Add(box);
        }

        var selectAll = new Button
        {
            Content = "全选 / 全不选",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        selectAll.Click += (_, _) =>
        {
            var allSelected = boxes.All(b => b.Box.IsChecked == true);
            foreach (var b in boxes) b.Box.IsChecked = !allSelected;
        };

        var asbName = new AutoSuggestBox
        {
            Header = "航空公司",
            PlaceholderText = "输入名称或二字码，另一边自动补齐",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(asbName, "航空公司");
        asbName.TextChanged += (_, e) =>
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                asbName.ItemsSource = App.Airlines.SearchNames(asbName.Text);
            }
        };

        var tbCode = new TextBox
        {
            Header = "航空公司二字码（如 CX）",
            MaxLength = 2,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AirlineLinker.Attach(asbName, tbCode);

        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = $"勾选要设置航司的飞机（共 {registrations.Count} 架）：",
            Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
        });
        panel.Children.Add(selectAll);
        panel.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 240,
            Padding = new Thickness(4, 0, 12, 0),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        panel.Children.Add(asbName);
        panel.Children.Add(tbCode);
        panel.Children.Add(new TextBlock
        {
            Text = "应用后：勾选飞机的所有照片都会写入这个航司；缺失的机型也会按同注册号已知机型补齐。",
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        });

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "批量设置航空公司",
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 560,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            PrimaryButtonText = "应用",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var picked = boxes
            .Where(b => b.Box.IsChecked == true)
            .Select(b => b.Registration)
            .ToList();

        if (picked.Count == 0) return null;

        return new AirlineAssignResult
        {
            Registrations = picked,
            AirlineName = string.IsNullOrWhiteSpace(asbName.Text) ? null : asbName.Text.Trim(),
            AirlineIata = string.IsNullOrWhiteSpace(tbCode.Text) ? null : tbCode.Text.Trim().ToUpperInvariant(),
        };
    }
}
