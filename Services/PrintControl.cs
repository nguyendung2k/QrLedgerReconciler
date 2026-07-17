using System;
using System.Threading;
using System.Threading.Tasks;

namespace QrLedgerReconciler.Services;

/// <summary>
/// Đối tượng điều khiển Tạm dừng / Tiếp tục / Kết thúc dùng cho các tác vụ
/// chạy dài (hiện tại: in hoá đơn công nợ). Tạo một PrintControl MỚI cho
/// mỗi lần bắt đầu in — không tái sử dụng giữa các lần chạy khác nhau, vì
/// CancellationTokenSource chỉ Cancel() được một lần.
/// </summary>
public class PrintControl
{
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _isPaused;

    public bool IsPaused => _isPaused;
    public bool IsStopped => _cts.IsCancellationRequested;
    public CancellationToken Token => _cts.Token;

    public void Pause() => _isPaused = true;

    public void Resume() => _isPaused = false;

    public void Stop() => _cts.Cancel();

    public void ThrowIfStopped() => Token.ThrowIfCancellationRequested();

    /// <summary>
    /// Nếu đang ở trạng thái tạm dừng thì chờ tới khi Resume() được gọi.
    /// Trả về ngay lập tức nếu không tạm dừng.
    /// Ném OperationCanceledException nếu Stop() được gọi trong lúc đang chờ
    /// (kể cả khi đang tạm dừng) — cho phép vòng lặp in dừng ngay cả khi
    /// đang bị pause.
    /// </summary>
    public async Task WaitIfPausedAsync(Action<string>? status = null)
    {
        if (!_isPaused) return;

        status?.Invoke("⏸ Đã tạm dừng in hoá đơn. Chờ bấm Tiếp tục...");

        while (_isPaused)
        {
            ThrowIfStopped();
            await Task.Delay(200, Token);
        }

        status?.Invoke("▶ Tiếp tục in hoá đơn...");
    }
}
