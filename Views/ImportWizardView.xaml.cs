using System;
using System.Collections.Generic;
using System.Linq;
using AirPhotoGarage.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

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

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelCommand.Execute(null);
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
