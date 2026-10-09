using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AirPhotoGarage.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace AirPhotoGarage.Views;

/// <summary>「加入分组」对话框的返回结果。</summary>
/// <param name="Confirmed">用户是否确认（false = 取消）。</param>
/// <param name="Remove">是否选择「移出分组」。</param>
/// <param name="GroupId">选中的既有分组 Id。</param>
/// <param name="NewName">要新建的分组名（优先于 <paramref name="GroupId"/>）。</param>
public sealed record PhotoGroupPickResult(bool Confirmed, bool Remove, long? GroupId, string? NewName);

/// <summary>
/// 自定义分组的交互对话框集合。
///
/// <para>
/// 全部使用 <see cref="ContentDialog"/> 并且<b>必须</b>设置 <c>XamlRoot</c>——
/// 这是 WinUI 3 相对 UWP 的硬性变化，漏设会在 ShowAsync 时抛
/// <see cref="InvalidOperationException"/>。
/// </para>
/// <para>
/// 这里刻意不在代码里查 <c>Application.Current.Resources[...]</c> 取样式：
/// 资源键缺失时会抛异常，而对话框弹不出来又没有任何提示，排查成本极高。
/// 文本样式改用显式属性，颜色用 <see cref="Microsoft.UI.Xaml.Media.SolidColorBrush"/> 之外
/// 的默认继承值，保证任何主题下都能显示。
/// </para>
/// </summary>
public static class PhotoGroupDialog
{
    /// <summary>
    /// 选择或新建一个分组。
    /// 若用户既没选既有分组、也没输入新名称，则<b>不关闭对话框</b>而是就地提示，
    /// 避免"确认后什么都没发生"的困惑。
    /// </summary>
    public static async Task<PhotoGroupPickResult> PickAsync(
        XamlRoot xamlRoot,
        string registrationNumber,
        IReadOnlyList<PhotoGroup> existingGroups,
        long? currentGroupId)
    {
        var groups = existingGroups?.ToList() ?? new List<PhotoGroup>();

        var error = new TextBlock
        {
            Text = "请选择一个分组，或在下方输入新分组名。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Margin = new Thickness(0, 8, 0, 0),
            Visibility = Visibility.Collapsed,
        };

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 200,
        };
        foreach (var g in groups)
        {
            list.Items.Add(g.Name);
        }
        if (currentGroupId.HasValue)
        {
            var idx = groups.FindIndex(g => g.Id == currentGroupId.Value);
            if (idx >= 0) list.SelectedIndex = idx;
        }
        // 选中项变化即清掉校验提示
        list.SelectionChanged += (_, _) => error.Visibility = Visibility.Collapsed;

        var newBox = new TextBox
        {
            Header = "或新建分组",
            PlaceholderText = "例如：2024 珠海航展",
            Margin = new Thickness(0, 12, 0, 0),
        };
        newBox.TextChanged += (_, _) => error.Visibility = Visibility.Collapsed;

        var panel = new StackPanel { Width = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = $"分组只作用于注册号「{registrationNumber}」下的照片（即同一架飞机）。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 8),
        });

        if (groups.Count == 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "这架飞机还没有任何分组，直接在下面输入名称即可新建。",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Opacity = 0.75,
            });
        }
        else
        {
            panel.Children.Add(list);
        }
        panel.Children.Add(newBox);
        panel.Children.Add(error);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "加入分组",
            Content = panel,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        // 已在分组里才提供「移出分组」
        if (currentGroupId.HasValue)
        {
            dialog.SecondaryButtonText = "移出分组";
        }

        var picked = default(PhotoGroupPickResult);

        dialog.PrimaryButtonClick += (_, args) =>
        {
            var newName = newBox.Text?.Trim();
            if (!string.IsNullOrEmpty(newName))
            {
                picked = new PhotoGroupPickResult(true, false, null, newName);
                return;
            }
            if (list.SelectedIndex >= 0 && list.SelectedIndex < groups.Count)
            {
                picked = new PhotoGroupPickResult(true, false, groups[list.SelectedIndex].Id, null);
                return;
            }
            // 两项都没填 → 阻止关闭，就地提示
            args.Cancel = true;
            error.Visibility = Visibility.Visible;
        };

        dialog.SecondaryButtonClick += (_, _) =>
        {
            picked = new PhotoGroupPickResult(true, true, null, null);
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None || picked is null)
        {
            return new PhotoGroupPickResult(false, false, null, null);
        }
        return picked;
    }

    /// <summary>重命名分组。返回新名字；取消返回 null。</summary>
    public static async Task<string?> RenameAsync(XamlRoot xamlRoot, string currentName)
    {
        var box = new TextBox
        {
            Header = "分组名",
            Text = currentName,
            Width = 360,
        };

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = "重命名分组",
            Content = box,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        var confirmed = false;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var name = box.Text?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                args.Cancel = true;
                return;
            }
            confirmed = true;
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary || !confirmed) return null;

        var newName = box.Text?.Trim();
        return string.IsNullOrEmpty(newName) || newName == currentName ? null : newName;
    }

    /// <summary>解散分组前的二次确认。</summary>
    public static async Task<bool> ConfirmDeleteAsync(
        XamlRoot xamlRoot, string groupName, int photoCount)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = $"解散分组「{groupName}」？",
            Content = new TextBlock
            {
                Text = $"组内 {photoCount} 张照片不会被删除，只是回到「按天」节点。",
                TextWrapping = TextWrapping.Wrap,
            },
            PrimaryButtonText = "解散",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}
