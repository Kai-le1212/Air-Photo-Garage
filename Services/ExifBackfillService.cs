using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using AirPhotoGarage.Models;

namespace AirPhotoGarage.Services;

/// <summary>
/// 历史照片的元数据回填。
///
/// <para>
/// 在修好 XMP 读取之前导入的照片（PNG / Lightroom 导出这一类）元数据全是空的，
/// 而且 <c>exif_json</c> 从来没被写过，所以那些照片重开也不会自己变好。
/// 启动时按批补一次：只处理 <c>exif_json IS NULL</c> 的行，限量 <see cref="BatchLimit"/> 张，
/// 剩下的下次启动继续 —— 避免大库把启动拖住。
/// </para>
///
/// <para>
/// <b>只填空值，绝不覆盖</b>：机型 / 注册号 / 机场 / 备注 / 拍摄时间都可能被用户手动改过
/// （<c>recognition_status = 2</c> 就表示已修正），回填只补那些仍然为空的 EXIF 字段。
/// </para>
/// </summary>
public static class ExifBackfillService
{
    /// <summary>每次启动最多回填多少张。10 张以内基本无感；大库分多次启动自然收敛。</summary>
    public const int BatchLimit = 50;

    public static async Task<int> RunAsync(IDatabaseService db, IExifService exif, int limit = BatchLimit)
    {
        IReadOnlyList<Photo> pending;
        try
        {
            pending = await db.GetPhotosMissingExifAsync(limit);
        }
        catch
        {
            return 0;
        }

        var count = 0;
        foreach (var photo in pending)
        {
            try
            {
                ExifMetadata? meta = null;
                var path = photo.FilePath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    meta = await exif.ReadAsync(path);
                }

                if (meta is null)
                {
                    // 原图已经不在了：记一个空快照，免得每次启动都重试同一张
                    photo.ExifJson = "{}";
                }
                else
                {
                    FillMissing(photo, meta);
                    photo.ExifJson = meta.RawDump ?? "{}";
                }

                await db.UpdatePhotoAsync(photo);
                count++;
            }
            catch
            {
                // 单张失败不影响其余照片
            }
        }

        return count;
    }

    /// <summary>把元数据里读到的值补进照片的空白字段（<c>??=</c> 保证已有值不被覆盖）。</summary>
    private static void FillMissing(Photo photo, ExifMetadata meta)
    {
        photo.CameraMake ??= meta.CameraMake;
        photo.CameraModel ??= meta.CameraModel;
        photo.LensModel ??= meta.LensModel;
        photo.FocalLength ??= meta.FocalLength;
        photo.Iso ??= meta.Iso;
        photo.Latitude ??= meta.Latitude;
        photo.Longitude ??= meta.Longitude;

        // 拍摄时间同样只补空值：有的照片是用户手动填的（EXIF 里可能根本没有）
        photo.ShotAt ??= meta.ShotAt;

        // 光圈 / 快门在库里是「显示文案」，格式必须和导入流程一致，所以复用同一份格式化函数
        if (string.IsNullOrWhiteSpace(photo.Aperture))
        {
            photo.Aperture = PhotoImportService.FormatAperture(meta.ApertureFNumber);
        }
        if (string.IsNullOrWhiteSpace(photo.ShutterSpeed))
        {
            photo.ShutterSpeed = PhotoImportService.FormatShutter(meta.ExposureTimeSeconds);
        }
    }
}
