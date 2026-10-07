using System.Threading;
using System.Threading.Tasks;

namespace AirPhotoGarage.Services;

/// <summary>
/// 飞机识别器抽象接口。
/// 预留扩展点：未来可接入 ONNX / WinML / 本地大模型 / 在线 API 等方案，
/// 当前默认实现 <see cref="NullAircraftRecognizer"/> 不做识别，仅返回 null。
/// </summary>
public interface IAircraftRecognizer
{
    /// <summary>识别器名称，用于在 UI 中展示当前生效的识别后端。</summary>
    string Name { get; }

    /// <summary>
    /// 对单张图片执行识别，返回机型字符串与置信度（0~1）。
    /// 无法识别时应返回 null，且不抛出异常。
    /// </summary>
    Task<AircraftRecognitionResult?> RecognizeAsync(
        string imagePath,
        CancellationToken cancellationToken = default);
}

public sealed record AircraftRecognitionResult(
    string AircraftModel,
    double Confidence);
