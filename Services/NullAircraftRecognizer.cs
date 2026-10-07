using System.Threading;
using System.Threading.Tasks;

namespace AirPhotoGarage.Services;

/// <summary>
/// 默认空实现：不做任何识别，<see cref="RecognizeAsync"/> 始终返回 null。
/// 用于第一版上线 / 未配置模型时的占位，避免 UI 调用方做空判断。
/// </summary>
public sealed class NullAircraftRecognizer : IAircraftRecognizer
{
    public string Name => "未启用模型识别";

    public Task<AircraftRecognitionResult?> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<AircraftRecognitionResult?>(null);
    }
}
