using System;
using System.Threading;
using System.Threading.Tasks;
using QrLedgerReconciler.Models;

namespace QrLedgerReconciler.Services;

/// <summary>
/// Đối tượng điều khiển một lần chạy chức năng: Tạm dừng / Tiếp tục / Kết thúc
/// (dùng cho in hoá đơn công nợ) và Dừng toàn bộ (dùng cho mọi chức năng).
///
/// Tạo một RunControl MỚI cho mỗi lần chạy — không tái sử dụng giữa các lần
/// khác nhau, vì CancellationTokenSource chỉ Cancel() được một lần.
///
/// Hai mức dừng:
///   Stop()      — hợp tác: huỷ token, dừng ở checkpoint kế tiếp (nút "Kết thúc").
///   ForceStop() — tức thời: huỷ token + BrowserContextFactory đóng trình duyệt
///                 ngay, mọi lệnh Playwright đang chờ sẽ lỗi và được dịch thành
///                 OperationCanceledException (nút "Dừng toàn bộ").
/// </summary>
public sealed class RunControl : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private volatile bool _isPaused;
    private volatile bool _forceStopRequested;

    public AppFunction Function { get; init; }

    public bool IsPaused => _isPaused;
    public bool IsStopped => _cts.IsCancellationRequested;

    /// <summary>Dừng toàn bộ — khác Stop() ở chỗ trình duyệt sẽ bị đóng ngay.</summary>
    public bool ForceStopRequested => _forceStopRequested;

    public CancellationToken Token => _cts.Token;

    public void Pause() => _isPaused = true;

    public void Resume() => _isPaused = false;

    public void Stop() => _cts.Cancel();

    public void ForceStop()
    {
        // Đặt cờ TRƯỚC khi Cancel để callback trong BrowserContextFactory
        // (chạy ngay khi Cancel) thấy được yêu cầu đóng trình duyệt.
        _forceStopRequested = true;
        _cts.Cancel();
    }

    public void ThrowIfStopped() => Token.ThrowIfCancellationRequested();

    /// <summary>
    /// Checkpoint chung cho mọi vòng lặp dài: dừng nếu đã Stop, chờ nếu đang Pause.
    /// </summary>
    public async Task CheckpointAsync(Action<string>? status = null)
    {
        ThrowIfStopped();
        await WaitIfPausedAsync(status);
    }

    /// <summary>
    /// Nếu đang ở trạng thái tạm dừng thì chờ tới khi Resume() được gọi.
    /// Trả về ngay lập tức nếu không tạm dừng.
    /// Ném OperationCanceledException nếu Stop() được gọi trong lúc đang chờ
    /// (kể cả khi đang tạm dừng) — cho phép vòng lặp dừng ngay cả khi đang pause.
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

    /// <summary>
    /// Giải phóng CancellationTokenSource. Chỉ gọi sau khi lần chạy đã kết thúc
    /// (mọi callback của token đã chạy xong) — xem RunExclusiveAsync.
    /// </summary>
    public void Dispose() => _cts.Dispose();
}
