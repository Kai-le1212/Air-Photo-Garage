using System;
using System.Collections.Generic;
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
/// 规则：<b>留空的字段不修改</b>（不是清空）。想清空某个字段请用单张编辑。
/// 机型框带候选，航空公司名称 ↔ 二字码 与其它入口共用 <see cref="AirlineLinker"/>。
/// </para>
/// </summary>
internal static class PhotoBatchEditDialog
{
    public static async Task<PhotoBatchFields?> ShowAsync(XamlRoot root, int photoCount)
    {
        var asbModel = new AutoSuggestBox
        {
            Header = "机型（留空则不改）",
            PlaceholderText = "如 Boeing 737-800",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(asbModel, "机型");
        asbModel.TextChanged += (_, e) =>
        {
            if (e.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
            {
                asbModel.ItemsSource = App.AircraftCatalog.Search(asbModel.Text);
            }
        };

        var tbReg = new TextBox
        {
            Header = "注册号（留空则不改）",
            PlaceholderText = "如 B-1234",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        tbReg.TextChanged += (_, _) => TextInput.ForceUpperCase(tbReg);

        var asbAirline = new AutoSuggestBox
        {
            Header = "航空公司（留空则不改）",
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
            Header = "航空公司二字码（如 CX）",
            MaxLength = 2,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AirlineLinker.Attach(asbAirline, tbAirlineCode);

        var tbIata = new TextBox
        {
            Header = "机场 IATA（如 PEK）",
            MaxLength = 3,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        tbIata.TextChanged += (_, _) => TextInput.ForceUpperCase(tbIata);

        var tbIcao = new TextBox
        {
            Header = "机场 ICAO（如 ZBAA）",
            MaxLength = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        tbIcao.TextChanged += (_, _) => TextInput.ForceUpperCase(tbIcao);

        var tbAirportName = new TextBox
        {
            Header = "机场名称（如 北京首都国际机场）",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var tbFlight = new TextBox
        {
            Header = "航班号（如 CX390）",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        tbFlight.TextChanged += (_, _) => TextInput.ForceUpperCase(tbFlight);

        var tbNotes = new TextBox
        {
            Header = "备注",
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };

        var panel = new StackPanel { Spacing = 10, MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = $"将给选中的 {photoCount} 张照片写入下面的信息；留空的项保持原样。",
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(asbModel);
        panel.Children.Add(tbReg);
        panel.Children.Add(asbAirline);
        panel.Children.Add(tbAirlineCode);
        panel.Children.Add(tbIata);
        panel.Children.Add(tbIcao);
        panel.Children.Add(tbAirportName);
        panel.Children.Add(tbFlight);
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

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        return new PhotoBatchFields(
            AircraftModel: asbModel.Text,
            RegistrationNumber: tbReg.Text,
            AirlineName: asbAirline.Text,
            AirlineIata: tbAirlineCode.Text,
            AirportIata: tbIata.Text,
            AirportIcao: tbIcao.Text,
            AirportName: tbAirportName.Text,
            FlightNumber: tbFlight.Text,
            Notes: tbNotes.Text);
    }
}
