using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using AirPhotoGarage.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace AirPhotoGarage.Views;

/// <summary>
/// 「批量填写」对话框：给多选的照片一次性写同一组信息。
///
/// <para>
/// 规则：<b>留空的字段不修改</b>（不是清空）。
/// </para>
///
/// <para>
/// 打开时会先看这批照片在各字段上是否<b>已经全部相同</b>：相同就直接预填到输入框，
/// 用户不用再抄一遍；提交时值没变的字段也不会重复写库。
/// </para>
/// </summary>
internal static class PhotoBatchEditDialog
{
    public static async Task<PhotoBatchFields?> ShowAsync(XamlRoot root, IReadOnlyList<Photo> photos)
    {
        var count = photos.Count;

        var asbModel = new AutoSuggestBox
        {
            Header = "机型",
            PlaceholderText = "如 Boeing 737-800",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(asbModel, "机型");
        TextInput.ApplyModel(asbModel);
        asbModel.TextChanged += (_, e) =>
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                asbModel.ItemsSource = App.AircraftCatalog.Search(asbModel.Text);
            }
        };

        var tbReg = new TextBox
        {
            Header = "注册号",
            PlaceholderText = "如 B-1234",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        TextInput.ApplyRegistration(tbReg);

        var asbAirline = new AutoSuggestBox
        {
            Header = "航空公司",
            PlaceholderText = "输入名称或二字码，另一边自动补齐",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(asbAirline, "航空公司");
        asbAirline.TextChanged += (_, e) =>
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                asbAirline.ItemsSource = App.Airlines.SearchNames(asbAirline.Text);
            }
        };

        var tbAirlineCode = new TextBox
        {
            Header = "航司二字码",
            PlaceholderText = "如 CX",
            MaxLength = 2,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var tbAirlineIcao = new TextBox
        {
            Header = "航司三字码",
            PlaceholderText = "如 CPA",
            MaxLength = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        TextInput.ApplyCode(tbAirlineIcao);

        var tbIata = new TextBox
        {
            Header = "机场 IATA",
            PlaceholderText = "如 SZX",
            MaxLength = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        TextInput.ApplyCode(tbIata);

        var tbIcao = new TextBox
        {
            Header = "机场 ICAO",
            PlaceholderText = "如 ZGSZ",
            MaxLength = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        TextInput.ApplyCode(tbIcao);

        var tbAirportName = new TextBox
        {
            Header = "机场名称",
            PlaceholderText = "如 深圳宝安国际机场",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // 三字码 / 四字码 / 全称 互相补齐（与照片编辑同一份逻辑）
        AirportLinker.Attach(tbIata, tbIcao, tbAirportName);

        var tbFlight = new TextBox
        {
            Header = "航班号",
            PlaceholderText = "如 CX390",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        TextInput.ApplyCode(tbFlight);

        // 三向联动：名称 ↔ 二字码 ↔ 航班号前两位
        AirlineLinker.Attach(asbAirline, tbAirlineCode, tbAirlineIcao, tbFlight);

        var tbNotes = new TextBox
        {
            Header = "备注",
            PlaceholderText = "留空则不改",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        // 已经全部相同的字段直接预填 —— 用户不必再抄一遍
        int prefilled = 0;

        void Prefill(TextBox box, Func<Photo, string?> pick)
        {
            var common = CommonValue(photos, pick);
            if (common is null) return;
            box.Text = common;
            prefilled++;
        }

        Prefill(tbReg, p => p.RegistrationNumber);
        Prefill(tbIata, p => p.AirportIata);
        Prefill(tbIcao, p => p.AirportIcao);
        Prefill(tbAirportName, p => p.AirportName);
        Prefill(tbFlight, p => p.FlightNumber);
        Prefill(tbNotes, p => p.Notes);

        var commonModel = CommonValue(photos, p => p.AircraftModel);
        if (commonModel is not null) { asbModel.Text = commonModel; prefilled++; }

        var commonAirline = CommonValue(photos, p => p.AirlineName);
        if (commonAirline is not null) { asbAirline.Text = commonAirline; prefilled++; }

        var commonCode = CommonValue(photos, p => p.AirlineIata);
        if (commonCode is not null) { tbAirlineCode.Text = commonCode; prefilled++; }

        var commonIcao = CommonValue(photos, p => p.AirlineIcao);
        if (commonIcao is not null) { tbAirlineIcao.Text = commonIcao; prefilled++; }

        var panel = new StackPanel { Spacing = 10, MinWidth = 440 };

        var hint = new TextBlock
        {
            Text = prefilled > 0
                ? $"将给选中的 {count} 张照片写入下面的信息。"
                  + $"其中 {prefilled} 项这批照片已经全部相同，已自动带出；保持原样就不会重复写入。"
                : $"将给选中的 {count} 张照片写入下面的信息；留空的项保持原样。",
            TextWrapping = TextWrapping.Wrap,
        };
        panel.Children.Add(hint);

        // 顺序与导入向导一致：机型+注册号 / 航司 / 二字码+航班号 / 机场 / 备注
        panel.Children.Add(Pair(asbModel, tbReg));
        panel.Children.Add(asbAirline);
        panel.Children.Add(Pair(tbAirlineCode, tbAirlineIcao));
        panel.Children.Add(tbFlight);
        panel.Children.Add(tbIata);
        panel.Children.Add(tbIcao);
        panel.Children.Add(tbAirportName);
        panel.Children.Add(tbNotes);

        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = "批量填写信息",
            Content = new ScrollViewer
            {
                Content = panel,
                MaxHeight = 560,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
            PrimaryButtonText = "写入",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };
        Views.AppTheme.Apply(dialog);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        return new PhotoBatchFields(
            AircraftModel: asbModel.Text,
            RegistrationNumber: tbReg.Text,
            AirlineName: asbAirline.Text,
            AirlineIata: tbAirlineCode.Text,
            AirlineIcao: tbAirlineIcao.Text,
            AirportIata: tbIata.Text,
            AirportIcao: tbIcao.Text,
            AirportName: tbAirportName.Text,
            FlightNumber: tbFlight.Text,
            Notes: tbNotes.Text);
    }

    /// <summary>
    /// 这批照片在该字段上是否<b>全部相同且非空</b>；是则返回该值，否则 null。
    /// </summary>
    private static string? CommonValue(IReadOnlyList<Photo> photos, Func<Photo, string?> pick)
    {
        if (photos.Count == 0) return null;

        string? first = null;
        foreach (var photo in photos)
        {
            var value = pick(photo);
            if (string.IsNullOrWhiteSpace(value)) return null;

            var trimmed = value.Trim();
            if (first is null) first = trimmed;
            else if (!string.Equals(first, trimmed, StringComparison.OrdinalIgnoreCase)) return null;
        }

        return first;
    }

    /// <summary>把两个控件并排放一行（各占一半，中间留 8px）。</summary>
    private static Grid Pair(FrameworkElement left, FrameworkElement right)
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(left, 0);
        Grid.SetColumn(right, 1);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }
}
