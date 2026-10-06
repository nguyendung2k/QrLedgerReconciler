using System;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using QrLedgerReconciler.Services;
using QrLedgerReconciler.Models;

namespace QrLedgerReconciler.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly ReconciliationService _service = new();
    private readonly Action<string> _showMessage;
    private readonly Func<string, string, MessageBoxResult> _showConfirm;
    private readonly IDialogService _dialogService;

    public event PropertyChangedEventHandler? PropertyChanged;

    // Input Properties
    private static readonly string DefaultFromDate = DateTime.Today.ToString("dd/MM/yyyy 00:00");
    private static readonly string DefaultToDate = DateTime.Today.AddDays(1).AddMinutes(-1).ToString("dd/MM/yyyy HH:mm");

    public string FromDate { get; set; } = DefaultFromDate;
    public string ToDate { get; set; } = DefaultToDate;
    private string _userName = string.Empty;
    public string UserName { get => _userName; set => SetProperty(ref _userName, value); }

    private string _password = string.Empty;
    public string Password { get => _password; set => SetProperty(ref _password, value); }

    private bool _hasResults;
    public bool HasResults
    {
        get => _hasResults;
        set => SetProperty(ref _hasResults, value);
    }

    private string _customerCode = string.Empty;
    public string CustomerCode
    {
        get => _customerCode;
        set => SetProperty(ref _customerCode, value);
    }

    // Output Properties
    private string _ledgerText = string.Empty;
    public string LedgerText { get => _ledgerText; set => SetProperty(ref _ledgerText, value); }

    private string _qrText = string.Empty;
    public string QrText { get => _qrText; set => SetProperty(ref _qrText, value); }

    private string _differenceText = string.Empty;
    public string DifferenceText { get => _differenceText; set => SetProperty(ref _differenceText, value); }

    private Brush _differenceColor = Brushes.Black;
    public Brush DifferenceColor { get => _differenceColor; set => SetProperty(ref _differenceColor, value); }

    private string _logText = string.Empty;
    public string LogText { get => _logText; set => SetProperty(ref _logText, value); }

    private string _statusText = string.Empty;
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }



    // Trạng thái riêng cho phiên in hoá đơn công nợ (điều khiển bởi Tạm dừng/Tiếp tục/Kết thúc)
    private bool _isPrinting;
    public bool IsPrinting { get => _isPrinting; set => SetProperty(ref _isPrinting, value); }

    private bool _isPrintPaused;
    public bool IsPrintPaused { get => _isPrintPaused; set => SetProperty(ref _isPrintPaused, value); }

    private PrintControl? _printControl;

    // Commands
    public ICommand GetLedgerCommand { get; }
    public ICommand GetQrCommand { get; }
    public ICommand RunReconcileCommand { get; }
    public ICommand ClearShiftCommand { get; }
    public ICommand PickFromDateCommand { get; }
    public ICommand PickToDateCommand { get; }
    public ICommand PrintDebtInvoiceCommand { get; }
    public ICommand PrintShiftProductReportCommand { get; }
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand StopCommand { get; }

    private Cache? _cache;
    private record Cache(decimal Ledger, decimal Qr, DateTime From, DateTime To);

    private const string DateFormat = "dd/MM/yyyy HH:mm";

    public MainViewModel(Action<string> showMessage,
                         Func<string, string, MessageBoxResult> showConfirm,
                         IDialogService dialogService)
    {
        _showMessage = showMessage;
        _showConfirm = showConfirm;
        _dialogService = dialogService;

        GetLedgerCommand = new RelayCommand(ExecuteGetLedger);
        GetQrCommand = new RelayCommand(ExecuteGetQr);
        RunReconcileCommand = new RelayCommand(ExecuteReconcile);
        ClearShiftCommand = new RelayCommand(ExecuteClearShift);
        PickFromDateCommand = new RelayCommand(ExecutePickFromDate);
        PickToDateCommand = new RelayCommand(ExecutePickToDate);

        // Không cho bấm "In hoá đơn công nợ" lần nữa khi đang in dở
        PrintDebtInvoiceCommand = new RelayCommand<string>(ExecutePrintDebtInvoices, _ => !IsPrinting);

        // In "Bảng phân chia sản phẩm" theo ca — dùng chung nút Busy như Clear ca
        PrintShiftProductReportCommand = new RelayCommand(ExecutePrintShiftProductReport);

        // Tạm dừng: chỉ bật khi đang in và chưa pause
        PauseCommand = new RelayCommand(ExecutePause, () => IsPrinting && !IsPrintPaused);
        // Tiếp tục: chỉ bật khi đang in và đang pause
        ResumeCommand = new RelayCommand(ExecuteResume, () => IsPrinting && IsPrintPaused);
        // Kết thúc: bật bất cứ khi nào đang in (kể cả đang pause)
        StopCommand = new RelayCommand(ExecuteStop, () => IsPrinting);

        HasResults = false;
    }

    private async void ExecuteGetLedger() => await ExecuteWithBusyAsync("Đang lấy dữ liệu sổ cái...", async req =>
    {
        var ledger = await _service.GetLedgerOnlyAsync(req, s => StatusText = s);
        _cache = new Cache(ledger, _cache?.Qr ?? 0, req.From, req.To);
        UpdateLedgerUI(ledger, req.From, req.To);
        Log("Đã lấy dữ liệu sổ cái.");
        HasResults = true;
    });

    private async void ExecuteGetQr() => await ExecuteWithBusyAsync("Đang lấy dữ liệu QR Code...", async req =>
    {
        var qr = await _service.GetQrOnlyAsync(req, s => StatusText = s);
        _cache = new Cache(_cache?.Ledger ?? 0, qr, req.From, req.To);
        UpdateQrUI(qr, req.From, req.To);
        Log("Đã lấy dữ liệu QR Code.");
        HasResults = true;
    });

    private async void ExecuteReconcile() => await ExecuteWithBusyAsync("Đang kiểm tra dữ liệu...", async req =>
    {
        var result = await _service.RunAsync(req, s => StatusText = s);
        UpdateResultUI(result.LedgerTotal, result.QrTotal, req.From, req.To);
    });

    private async void ExecuteClearShift()
    {
        if (!TryGetRequest(out var req)) return;

        if (_showConfirm("Xác nhận", $"Thao tác sẽ xử lý từ {req.From:dd/MM/yyyy} đến {req.To:dd/MM/yyyy}.\n\nTiếp tục?") != MessageBoxResult.Yes)
            return;

        await ExecuteWithBusyAsync("Đang Clear sổ giao ca...", async req =>
        {
            await _service.ClearShiftAsync(req, s => StatusText = s);
            Log("Đã hoàn thành Clear sổ giao ca.");
            _showMessage($"Đã hoàn thành. từ {req.From:dd/MM/yyyy} đến {req.To:dd/MM/yyyy}");
        });
    }

    private async void ExecutePrintShiftProductReport()
    {
        if (!TryGetRequest(out var req)) return;

        if (_showConfirm("Xác nhận", $"Sẽ in Bảng phân chia sản phẩm cho tất cả ca từ {req.From:dd/MM/yyyy} đến {req.To:dd/MM/yyyy}.\n\nTiếp tục?") != MessageBoxResult.Yes)
            return;

        await ExecuteWithBusyAsync("Đang in Bảng phân chia sản phẩm...", async req =>
        {
            var service = new ShiftProductReportService();
            await service.RunAsync(req, s => StatusText = s);
            Log("Đã hoàn thành in Bảng phân chia sản phẩm.");
            _showMessage($"Đã hoàn thành in Bảng phân chia sản phẩm từ {req.From:dd/MM/yyyy} đến {req.To:dd/MM/yyyy}");
        });
    }

    private async void ExecutePrintDebtInvoices(string? customerCode)
    {
        if (string.IsNullOrWhiteSpace(customerCode))
        {
            _showMessage("Vui lòng nhập mã khách hàng để in hoá đơn.");
            return;
        }

        if (IsPrinting) return; // phòng trường hợp CanExecute chưa kịp requery

        _printControl = new PrintControl();
        IsPrinting = true;
        IsPrintPaused = false;

        try
        {
            await ExecuteWithBusyAsync("Đang in hoá đơn công nợ...", async req =>
            {
                var service = new InvoicePrintingService();
                try
                {
                    await service.PrintDebtInvoicesAsync(req, customerCode.Trim(), s => StatusText = s, _printControl);
                    Log("Đã in xong tất cả hoá đơn công nợ.");
                }
                catch (OperationCanceledException)
                {
                    Log("Đã dừng in hoá đơn theo yêu cầu.");
                }
            });
        }
        finally
        {
            // Nếu người dùng bấm Kết thúc, giữ nguyên thông báo "Đã dừng" thay vì
            // để ExecuteWithBusyAsync ghi đè thành "Hoàn thành."
            if (_printControl?.IsStopped == true)
                StatusText = "Đã dừng in hoá đơn.";

            IsPrinting = false;
            IsPrintPaused = false;
            _printControl = null;
        }
    }

    private void ExecutePause()
    {
        _printControl?.Pause();
        IsPrintPaused = true;
    }

    private void ExecuteResume()
    {
        _printControl?.Resume();
        IsPrintPaused = false;
    }

    private void ExecuteStop()
    {
        _printControl?.Stop();
        StatusText = "Đang dừng...";
    }

    private async Task ExecuteWithBusyAsync(string initialStatus, Func<ReconciliationRequest, Task> operation)
    {
        if (!TryGetRequest(out var request)) return;

        IsBusy = true;
        StatusText = initialStatus;

        try
        {
            await operation(request);
        }
        catch (Exception ex)
        {
            HandleError(ex);
        }
        finally
        {
            IsBusy = false;
            StatusText = "Hoàn thành.";
        }
    }

    private bool TryGetRequest(out ReconciliationRequest request)
    {
        request = null!;

        if (!TryParseDate(FromDate, out var from)) return Fail("Ngày bắt đầu không đúng định dạng.");
        if (!TryParseDate(ToDate, out var to)) return Fail("Ngày kết thúc không đúng định dạng.");
        if (from >= to) return Fail("Ngày bắt đầu phải nhỏ hơn ngày kết thúc.");
        if (string.IsNullOrWhiteSpace(UserName)) return Fail("Nhập tài khoản EGAS.");
        if (string.IsNullOrWhiteSpace(Password)) return Fail("Nhập mật khẩu EGAS.");

        request = new ReconciliationRequest(UserName.Trim(), Password, from, to);
        return true;
    }

    private bool TryParseDate(string text, out DateTime date)
        => DateTime.TryParseExact(text, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private bool Fail(string message)
    {
        _showMessage(message);
        return false;
    }

    private void HandleError(Exception ex)
    {
        Log(ex.Message);
        _showMessage(ex.Message);
    }

    private void UpdateLedgerUI(decimal total, DateTime from, DateTime to)
        => LedgerText = $"""
            Thời gian : {from:dd/MM/yyyy HH:mm} → {to:dd/MM/yyyy HH:mm}
            Tổng tiền : {total:N0} VND
            """;

    private void UpdateQrUI(decimal total, DateTime from, DateTime to)
        => QrText = $"""
            Thời gian : {from:dd/MM/yyyy HH:mm} → {to:dd/MM/yyyy HH:mm}
            Tổng tiền : {total:N0} VND
            """;

    private void UpdateResultUI(decimal ledger, decimal qr, DateTime from, DateTime to)
    {
        UpdateLedgerUI(ledger, from, to);
        UpdateQrUI(qr, from, to);
        UpdateDifference(ledger - qr);
        HasResults = true;
    }

    private void UpdateDifference(decimal diff)
    {
        DifferenceText = $"Chênh lệch : {diff:N0} VND";
        DifferenceColor = diff == 0 ? Brushes.ForestGreen : Brushes.Firebrick;
        Log(diff == 0 ? "Đối chiếu khớp." : "Có chênh lệch, cần kiểm tra thêm giao dịch.");
    }

    private void Log(string message) => LogText = message;

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        // Các RelayCommand ở đây dựa vào CommandManager.RequerySuggested để
        // biết khi nào cần gọi lại CanExecute. Khi IsPrinting/IsPrintPaused
        // đổi từ code-behind (không phải do thao tác chuột trên UI), WPF có
        // thể không tự requery kịp thời — ép nó cập nhật ngay để nút bấm
        // enable/disable đúng lúc.
        CommandManager.InvalidateRequerySuggested();
    }

    // Date Picker
    private void ExecutePickFromDate()
    {
        var current = TryParseDate(FromDate, out var dt) ? dt : DateTime.Now;
        var selected = _dialogService.ShowDatePicker(current);
        if (selected.HasValue)
            FromDate = selected.Value.ToString(DateFormat);
    }

    private void ExecutePickToDate()
    {
        var current = TryParseDate(ToDate, out var dt) ? dt : DateTime.Now;
        var selected = _dialogService.ShowDatePicker(current);
        if (selected.HasValue)
            ToDate = selected.Value.ToString(DateFormat);
    }
}
