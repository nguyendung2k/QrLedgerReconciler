using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using QrLedgerReconciler.Models;
using QrLedgerReconciler.Services;

namespace QrLedgerReconciler.ViewModels;

/// <summary>Một dòng checkbox quyền trong ma trận chức năng.</summary>
public sealed class FunctionOption : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public FunctionOption(AppFunction function, bool isChecked)
    {
        Function = function;
        _isChecked = isChecked;
    }

    public AppFunction Function { get; }
    public string DisplayName => Function.DisplayName();

    /// <summary>Mô tả ngắn của chức năng (dùng cho hàng "chạy ẩn").</summary>
    public string HeadlessDescription => Function.HeadlessDescription();

    /// <summary>Icon Segoe MDL2 Assets của chức năng (hiển thị cạnh tên quyền).</summary>
    public string IconGlyph => Function switch
    {
        AppFunction.GetLedger => "",            // book / sổ
        AppFunction.GetQr => "",                // QR code
        AppFunction.Reconcile => "",            // balance / đối chiếu
        AppFunction.ClearShift => "",           // gear
        AppFunction.PrintDebtInvoice => "",     // printer
        AppFunction.PrintShiftProductReport => "", // table/grid
        AppFunction.ClearDebt => "",            // calendar với X
        _ => "",
    };

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
            OnChanged?.Invoke();
        }
    }

    /// <summary>Bắn khi người dùng đổi trạng thái checkbox (dùng để đánh dấu bẩn).</summary>
    public Action? OnChanged { get; set; }
}

/// <summary>
/// Màn hình quản lý tài khoản EGAS: thêm / sửa / xoá tài khoản, cấu hình
/// mật khẩu và gán các chức năng (phân quyền) cho từng tài khoản.
/// </summary>
public sealed class AccountManagerViewModel : INotifyPropertyChanged
{
    private readonly AccountStore _store;
    private readonly SettingsStore _settings;
    private readonly Action<string> _showMessage;
    private readonly Func<string, string, MessageBoxResult> _showConfirm;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Store dùng chung — các cửa sổ code-behind cần truy cập để đổi mật khẩu admin.</summary>
    public AccountStore Store => _store;

    public ObservableCollection<UserAccount> Accounts => _store.Accounts;
    public ObservableCollection<FunctionOption> FunctionOptions { get; } = new();

    /// <summary>Checkbox "chạy ẩn" cho tất cả chức năng đang có.</summary>
    public ObservableCollection<FunctionOption> HeadlessOptions { get; } = new();

    /// <summary>Số quyền đang bật của tài khoản đang sửa (ví dụ "4/7 quyền").</summary>
    public int CheckedFunctionCount => FunctionOptions.Count(o => o.IsChecked);

    /// <summary>True khi có thay đổi chưa lưu (để hiện cảnh báo và hỏi khi đóng).</summary>
    public bool IsDirty { get; private set; }

    private UserAccount? _selectedAccount;
    public UserAccount? SelectedAccount
    {
        get => _selectedAccount;
        set
        {
            if (ReferenceEquals(_selectedAccount, value)) return;

            // Chọn một dòng trong danh sách = thoát chế độ "thêm mới".
            var wasNew = _isNew;
            _isNew = false;
            _selectedAccount = value;

            OnPropertyChanged();
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(EditorTitle));

            if (!wasNew || value is not null) LoadEditor(value);

            CommandManager.InvalidateRequerySuggested();        }
    }

    // ----- Trạng thái editor -----
    private bool _isNew;
    public bool IsEditing => _selectedAccount is not null || _isNew;

    public string EditorTitle => _isNew ? "Thêm tài khoản mới"
                             : _selectedAccount is null ? "Chọn một tài khoản để sửa"
                             : $"Sửa tài khoản {_selectedAccount.UserName}";

    private string _editUserName = string.Empty;
    public string EditUserName
    {
        get => _editUserName;
        set { if (SetProperty(ref _editUserName, value)) MarkDirty(); }
    }

    private string _editPassword = string.Empty;
    public string EditPassword
    {
        get => _editPassword;
        set { if (SetProperty(ref _editPassword, value)) MarkDirty(); }
    }

    private string _editDisplayName = string.Empty;
    public string EditDisplayName
    {
        get => _editDisplayName;
        set { if (SetProperty(ref _editDisplayName, value)) MarkDirty(); }
    }

    private bool _editIsEnabled = true;
    public bool EditIsEnabled
    {
        get => _editIsEnabled;
        set { if (SetProperty(ref _editIsEnabled, value)) MarkDirty(); }
    }

    // ----- Commands -----
    public ICommand AddCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CheckAllCommand { get; }
    public ICommand UncheckAllCommand { get; }
    public ICommand SaveHeadlessCommand { get; }
    public ICommand RestoreDefaultsCommand { get; }

    public AccountManagerViewModel(
        AccountStore store,
        SettingsStore settings,
        Action<string> showMessage,
        Func<string, string, MessageBoxResult> showConfirm)
    {
        _store = store;
        _settings = settings;
        _showMessage = showMessage;
        _showConfirm = showConfirm;

        AddCommand = new RelayCommand(ExecuteAdd);
        DeleteCommand = new RelayCommand(ExecuteDelete, () => _selectedAccount is not null && !_isNew);
        SaveCommand = new RelayCommand(ExecuteSave, () => IsEditing && IsDirty);
        CancelCommand = new RelayCommand(ExecuteCancel, () => IsEditing);
        CheckAllCommand = new RelayCommand(() => SetAllFunctions(true));
        UncheckAllCommand = new RelayCommand(() => SetAllFunctions(false));
        SaveHeadlessCommand = new RelayCommand(ExecuteSaveHeadless);
        RestoreDefaultsCommand = new RelayCommand(ExecuteRestoreDefaults, () => IsEditing);

        // Chọn sẵn tài khoản đầu tiên cho tiện thao tác.
        if (Accounts.Count > 0)
            SelectedAccount = Accounts[0];

        BuildHeadlessOptions();
    }

    private void BuildHeadlessOptions()
    {
        HeadlessOptions.Clear();

        foreach (var function in SettingsStore.HeadlessCandidatesList)
        {
            var option = new FunctionOption(function, _settings.IsHeadless(function));
            option.OnChanged = MarkHeadlessDirty;
            HeadlessOptions.Add(option);
        }
    }

    private void ExecuteSaveHeadless()
    {
        foreach (var option in HeadlessOptions)
            _settings.SetHeadless(option.Function, option.IsChecked);

        _settings.Save();
        ClearDirty();
        _showMessage("Đã lưu cấu hình chạy ẩn.");
    }

    private void ExecuteAdd()
    {
        _isNew = true;
        _selectedAccount = null;

        EditUserName = string.Empty;
        EditPassword = string.Empty;
        EditDisplayName = string.Empty;
        EditIsEnabled = true;

        BuildFunctionOptions(new HashSet<AppFunction>());

        ClearDirty();

        OnPropertyChanged(nameof(SelectedAccount));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditorTitle));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ExecuteDelete() => DeleteAccount(_selectedAccount);

    /// <summary>Xoá một tài khoản cụ thể (dùng cho menu ngữ cảnh ở từng dòng danh sách).</summary>
    public void DeleteAccount(UserAccount? account)
    {
        if (account is null) return;

        if (_showConfirm("Xác nhận",
                $"Xoá tài khoản {account.UserName}?\n\nThao tác không thể hoàn tác.")
            != MessageBoxResult.Yes)
            return;

        Accounts.Remove(account);
        _store.Save();

        // Chỉ nạp lại editor khi vừa xoá đúng tài khoản đang sửa; xoá dòng khác không
        // được làm mất nội dung đang nhập (kể cả bản nháp "thêm tài khoản mới").
        if (ReferenceEquals(_selectedAccount, account))
        {
            _isNew = false;
            _selectedAccount = Accounts.Count > 0 ? Accounts[0] : null;
            OnPropertyChanged(nameof(SelectedAccount));
            LoadEditor(_selectedAccount);   // nạp lại và xoá cờ dirty
            OnPropertyChanged(nameof(IsEditing));
            OnPropertyChanged(nameof(EditorTitle));
        }

        CommandManager.InvalidateRequerySuggested();
    }

    private void ExecuteSave()
    {
        var userName = EditUserName.Trim();

        if (string.IsNullOrWhiteSpace(userName))
        {
            _showMessage("Nhập tên đăng nhập EGAS.");
            return;
        }

        if (_store.IsUserNameTaken(userName, _isNew ? null : _selectedAccount))
        {
            _showMessage($"Tên đăng nhập {userName} đã tồn tại.");
            return;
        }

        // Chỉ kiểm tra mật khẩu khi tạo mới. Với tài khoản đã có, cho phép để trống
        // nghĩa là giữ nguyên mật khẩu cũ (tránh việc PasswordBox hiển thị rỗng do
        // lỗi đồng bộ rồi vô tình xoá mất mật khẩu đã cấu hình).
        var password = EditPassword;
        var keepExistingPassword = !_isNew && _selectedAccount is not null
                                   && string.IsNullOrEmpty(password);

        if (!keepExistingPassword && string.IsNullOrWhiteSpace(password))
        {
            _showMessage("Nhập mật khẩu cho tài khoản.");
            return;
        }

        var functions = new HashSet<AppFunction>(
            FunctionOptions.Where(o => o.IsChecked).Select(o => o.Function));

        if (functions.Count == 0
            && _showConfirm("Xác nhận",
                "Tài khoản này không được gán chức năng nào và sẽ không dùng được gì.\n\nVẫn lưu?")
               != MessageBoxResult.Yes)
        {
            return;
        }

        if (_isNew)
        {
            var account = new UserAccount
            {
                UserName = userName,
                Password = password,
                DisplayName = string.IsNullOrWhiteSpace(EditDisplayName) ? userName : EditDisplayName.Trim(),
                IsEnabled = EditIsEnabled,
                Functions = functions,
            };

            Accounts.Add(account);
            _isNew = false;
            _selectedAccount = account;
            OnPropertyChanged(nameof(SelectedAccount));
        }
        else if (_selectedAccount is not null)
        {
            _selectedAccount.UserName = userName;
            if (!keepExistingPassword)
                _selectedAccount.Password = password;
            _selectedAccount.DisplayName = string.IsNullOrWhiteSpace(EditDisplayName)
                ? userName
                : EditDisplayName.Trim();
            _selectedAccount.IsEnabled = EditIsEnabled;
            _selectedAccount.Functions = functions;
        }

        _store.Save();

        IsDirty = false;
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(EditorTitle));
        _showMessage("Đã lưu tài khoản.");
        CommandManager.InvalidateRequerySuggested();
    }

    private void ExecuteCancel()
    {
        _isNew = false;
        _selectedAccount = Accounts.Count > 0 ? Accounts[0] : null;

        OnPropertyChanged(nameof(SelectedAccount));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditorTitle));
        LoadEditor(_selectedAccount);
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Khôi phục mặc định: bỏ trạng thái "thêm mới", quay về tài khoản đầu danh
    /// sách và nạp lại toàn bộ trường editor từ dữ liệu đã lưu (bỏ mọi thay đổi chưa lưu).
    /// </summary>
    private void ExecuteRestoreDefaults()
    {
        ExecuteCancel();
    }

    /// <summary>Hủy mọi thay đổi chưa lưu trong editor, quay về dữ liệu đã lưu.</summary>
    public void DiscardChanges() => ExecuteCancel();

    private void LoadEditor(UserAccount? account)
    {
        if (account is null)
        {
            EditUserName = string.Empty;
            EditPassword = string.Empty;
            EditDisplayName = string.Empty;
            EditIsEnabled = true;
            FunctionOptions.Clear();
            ClearDirty();
            return;
        }

        EditUserName = account.UserName;
        EditPassword = account.Password;
        EditDisplayName = account.DisplayName;
        EditIsEnabled = account.IsEnabled;

        BuildFunctionOptions(account.Functions);
        ClearDirty();
    }

    private void BuildFunctionOptions(HashSet<AppFunction> granted)
    {
        FunctionOptions.Clear();

        foreach (var function in AppFunctionExtensions.AllFunctions)
        {
            var option = new FunctionOption(function, granted.Contains(function));
            option.OnChanged = MarkDirty;
            FunctionOptions.Add(option);
        }
    }

    private void SetAllFunctions(bool value)
    {
        foreach (var option in FunctionOptions)
            option.IsChecked = value;
    }

    // ----- Theo dõi thay đổi chưa lưu -----
    private void MarkDirty()
    {
        IsDirty = true;
        OnPropertyChanged(nameof(IsDirty));

        // Checkbox quyền thay đổi → đếm lại số quyền đã chọn ở nhãn "Đã chọn: y".
        OnPropertyChanged(nameof(CheckedFunctionCount));

        CommandManager.InvalidateRequerySuggested();
    }

    private void MarkHeadlessDirty() => MarkDirty();

    private void ClearDirty()
    {
        IsDirty = false;
        OnPropertyChanged(nameof(IsDirty));
    }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}