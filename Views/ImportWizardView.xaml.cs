using System;
using System.Collections.Generic;
using System.Linq;
using AirPhotoGarage.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace AirPhotoGarage.Views;

public sealed partial class ImportWizardView : UserControl
{
    public ImportWizardViewModel ViewModel { get; }

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

    /// <summary>
    /// 注册号输入一律大写。
    /// XAML 上的 <c>CharacterCasing</c> 在本环境实测不生效，真正兜住的是这里
    /// （与照片编辑对话框、照片墙筛选面板同一套做法）。
    /// </summary>
    /// <summary>机型框：挡掉中文与全角，保留原大小写，同时刷新候选。</summary>
    private void OnModelTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

        var clean = TextInput.SanitizeModel(sender.Text);
        if (!string.Equals(clean, sender.Text, StringComparison.Ordinal)) sender.Text = clean;
    }

    private void OnRegistrationTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box) return;
        TextInput.SanitizeInPlace(box);
        // 首字母 B 自动补「-」：B1792 → B-1792
        var text = box.Text ?? string.Empty;
        if (text.Length >= 2 && text[0] == 'B' && text[1] != '-' && char.IsLetterOrDigit(text[1]))
        {
            var caret = box.SelectionStart;
            box.Text = "B-" + text[1..];
            box.SelectionStart = Math.Clamp(caret + 1, 0, box.Text.Length);
        }
    }

    /// <summary>航空公司名称：给候选 + 选中后自动带出二字码（与编辑对话框同一套目录）。</summary>
    private void OnAirlineNameChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        sender.ItemsSource = App.Airlines.SearchNames(sender.Text);

        // 名称能对上目录就顺手把二字码与航班号代码补齐
        if (App.Airlines.CodeOf(sender.Text) is { } code)
        {
            ViewModel.AirlineIata = code;
            EnsureFlightPrefix(code);
        }
    }

    private void OnAirlineNameChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is not string name) return;
        if (App.Airlines.CodeOf(name) is not { } code) return;

        ViewModel.AirlineIata = code;
        EnsureFlightPrefix(code);
    }

    /// <summary>
    /// 航司确定后把它的二字码填进航班号，用户只补数字即可。
    /// 已有航班号时只替换前两位代码，后面的数字保留。
    /// </summary>
    private void EnsureFlightPrefix(string code)
    {
        if (string.IsNullOrEmpty(code)) return;

        var flight = ViewModel.FlightNumber ?? string.Empty;
        if (flight.Length == 0)
        {
            ViewModel.FlightNumber = code;
            return;
        }

        if (flight.Length >= 2
            && char.IsAsciiLetter(flight[0])
            && char.IsAsciiLetter(flight[1])
            && !flight[..2].Equals(code, StringComparison.OrdinalIgnoreCase))
        {
            ViewModel.FlightNumber = code + flight[2..];
        }
    }

    /// <summary>二字码：强制大写；能在目录里查到就回填名称。</summary>
    private void OnAirlineCodeChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box) return;

        TextInput.SanitizeInPlace(box);
        var code = (box.Text ?? string.Empty).Trim();

        if (App.Airlines.NameOf(code) is { } name)
        {
            ViewModel.AirlineName = name;
            EnsureFlightPrefix(code);
        }
    }

    /// <summary>
    /// 航班号：大写 + <b>前两位当航司代码带出航司</b>
    /// （填 <c>CX390</c> → 航空公司=国泰航空、二字码=CX）。
    /// </summary>
    private void OnFlightNumberChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box) return;
        TextInput.SanitizeInPlace(box);

        var flight = (box.Text ?? string.Empty).Trim();
        if (flight.Length < 2) return;

        var code = flight[..2];
        if (code[0] is < 'A' or > 'Z' || code[1] is < 'A' or > 'Z') return;
        if (App.Airlines.NameOf(code) is not { } name) return;

        ViewModel.AirlineName = name;
        ViewModel.AirlineIata = code;
    }

    /// <summary>机场三字码 → 四字码 + 全称。</summary>
    private void OnAirportIataChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box) return;
        TextInput.SanitizeInPlace(box);

        var iata = (box.Text ?? string.Empty).Trim();
        if (iata.Length != 3) return;
        if (App.AirportCatalog.FindByIata(iata) is not { } airport) return;

        if (!string.Equals(ViewModel.AirportIcao, airport.Icao, StringComparison.OrdinalIgnoreCase))
            ViewModel.AirportIcao = airport.Icao ?? "";
        if (!string.Equals(ViewModel.AirportName, airport.Name, StringComparison.Ordinal))
            ViewModel.AirportName = airport.Name;
    }

    /// <summary>机场四字码 → 三字码 + 全称。</summary>
    private void OnAirportIcaoChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box) return;
        TextInput.SanitizeInPlace(box);

        var icao = (box.Text ?? string.Empty).Trim();
        if (icao.Length != 4) return;
        if (App.AirportCatalog.FindByIcao(icao) is not { } airport) return;

        if (!string.Equals(ViewModel.AirportIata, airport.Iata, StringComparison.OrdinalIgnoreCase))
            ViewModel.AirportIata = airport.Iata ?? "";
        if (!string.Equals(ViewModel.AirportName, airport.Name, StringComparison.Ordinal))
            ViewModel.AirportName = airport.Name;
    }

    /// <summary>点预览图 → 用系统默认程序打开这张原图（放大看细节）。</summary>
    private async void OnPreviewTapped(object sender, TappedRoutedEventArgs e)
    {
        e.Handled = true;
        if (ViewModel.CurrentPhotoPath is { } path)
        {
            await PhotoLauncher.OpenAsync(path);
        }
    }

    private async void OnCancelClick(object sender, RoutedEventArgs e)
    {
        // 已填的一堆字段点一下就没了太容易误触，先确认一次
        var discard = await Dialogs.ConfirmAsync(
            "取消这次导入？",
            "这一批照片已经填写的信息不会保存，照片副本也会留在库里。",
            primaryText: "取消导入",
            closeText: "继续填写");

        if (discard)
        {
            ViewModel.CancelCommand.Execute(null);
        }
    }

    // ---------- 机型候选（原生 AutoSuggestBox） ----------

    /// <summary>
    /// 挂上候选逻辑。依 WinUI 3 规范使用 <see cref="AutoSuggestBox"/>：
    /// 候选下拉是 Fluent 原生浮出层，自带「点击外部收起」「单击即选中」
    /// 「上下键导航」等行为，无需用 ListView 自行模拟。
    /// </summary>
    private void HookModelSuggestions()
    {
        // 仅用户输入时更新候选，避免程序化回填反复触发。
        AsbModel.TextChanged += (s, e) =>
        {
            if (e.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
            var results = ViewModel.AircraftSuggestions(AsbModel.Text ?? "").ToList();
            AsbModel.ItemsSource = results;

            // 保留原有「输入即同步到 ViewModel」的行为，确保保存时不丢。
            ViewModel.AircraftModel = AsbModel.Text ?? "";
        };

        // 选中候选项：写入文本并同步 ViewModel（原生会同时收起下拉）。
        AsbModel.SuggestionChosen += (s, e) =>
        {
            if (e.SelectedItem is string picked)
            {
                AsbModel.Text = picked;
                ViewModel.AircraftModel = picked;
            }
        };

        // 回车 / 查询按钮提交：优先用候选，否则用当前输入文本。
        AsbModel.QuerySubmitted += (s, e) =>
        {
            var chosen = e.ChosenSuggestion as string;
            if (!string.IsNullOrWhiteSpace(chosen))
            {
                AsbModel.Text = chosen;
                ViewModel.AircraftModel = chosen;
            }
            else
            {
                ViewModel.AircraftModel = AsbModel.Text ?? "";
            }
        };
    }
}
