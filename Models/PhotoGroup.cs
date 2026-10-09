using System;

namespace AirPhotoGarage.Models;

/// <summary>
/// 用户自定义的照片分组，归属于某个注册号（= 同一架飞机）。
///
/// <para>
/// 与「按天自动分组」相对：按天分组是按 <see cref="Photo.ShotAt"/> 自动切分、
/// 无需用户干预；本类型代表用户<b>手动命名</b>、<b>手动指定成员</b>的分组，
/// 典型场景如「2024 珠海航展」「首次拍到」。
/// </para>
/// <para>
/// 归属规则：分组挂在注册号下面，因此只有同一架飞机的照片能被归入同一个分组。
/// 一张照片<b>最多属于一个</b>自定义分组（成员表以 photo_id 为主键）。
/// </para>
/// </summary>
public sealed class PhotoGroup
{
    public long Id { get; set; }

    /// <summary>所属注册号。分组只在同一架飞机范围内可见与可选。</summary>
    public string RegistrationNumber { get; set; } = string.Empty;

    /// <summary>分组名（用户输入，如「2024 珠海航展」）。同一注册号下不允许重名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>创建时间。分组内若没有任何带拍摄时间的照片，时间轴用它对节点排序。</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
}
