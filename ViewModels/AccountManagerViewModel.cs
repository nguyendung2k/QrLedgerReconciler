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

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }
}

/// <summary>
/// Màn hình quản lý tài khoản EGAS: thêm / sửa / xoá tài khoản, cấu hình
/// mật khẩu và gán các chức năng (phân quyền) cho từng tài khoản.
/// </summary>
public sealed class AccountManagerViewModel : INotifyPropertyChanged
{
    private readonly AccountStore _store;
    private readonly Action<string> _showMessage;
    private readonly Func<string, string, MessageBoxResult> _showConfirm;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Store dùng chung — các cửa sổ code-behind cần truy cập để đổi mật khẩu admin.</summary>
    public AccountStore Store => _store;

    public ObservableCollection<UserAccount> Accounts => _store.Accounts;
    public ObservableCollection<FunctionOption> FunctionOptions { get; } = new();

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

            CommandManager.InvalidateRequerySuggested();
        }
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
        set => SetProperty(ref _editUserName, value);
    }

    private string _editPassword = string.Empty;
    public string EditPassword
    {
        get => _editPassword;
        set => SetProperty(ref _editPassword, value);
    }

    private string _editDisplayName = string.Empty;
    public string EditDisplayName
    {
        get => _editDisplayName;
        set => SetProperty(ref _editDisplayName, value);
    }

    private bool _editIsEnabled = true;
    public bool EditIsEnabled
    {
        get => _editIsEnabled;
        set => SetProperty(ref _editIsEnabled, value);
    }

    // ----- Commands -----
    public ICommand AddCommand { get; }
    public ICommand DeleteCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand CheckAllCommand { get; }
    public ICommand UncheckAllCommand { get; }

    public AccountManagerViewModel(
        AccountStore store,
        Action<string> showMessage,
        Func<string, string, MessageBoxResult> showConfirm)
    {
        _store = store;
        _showMessage = showMessage;
        _showConfirm = showConfirm;

        AddCommand = new RelayCommand(ExecuteAdd);
        DeleteCommand = new RelayCommand(ExecuteDelete, () => _selectedAccount is not null && !_isNew);
        SaveCommand = new RelayCommand(ExecuteSave, () => IsEditing);
        CancelCommand = new RelayCommand(ExecuteCancel, () => IsEditing);
        CheckAllCommand = new RelayCommand(() => SetAllFunctions(true));
        UncheckAllCommand = new RelayCommand(() => SetAllFunctions(false));

        // Chọn sẵn tài khoản đầu tiên cho tiện thao tác.
        if (Accounts.Count > 0)
            SelectedAccount = Accounts[0];
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

        OnPropertyChanged(nameof(SelectedAccount));
        OnPropertyChanged(nameof(IsEditing));
        OnPropertyChanged(nameof(EditorTitle));
        CommandManager.InvalidateRequerySuggested();
    }

    private void ExecuteDelete()
    {
        if (_selectedAccount is null) return;

        if (_showConfirm("Xác nhận",
                $"Xoá tài khoản {_selectedAccount.UserName}?\n\nThao tác không thể hoàn tác.")
            != MessageBoxResult.Yes)
            return;

        var removed = _selectedAccount;
        Accounts.Remove(removed);

        if (!_isNew)
        {
            _selectedAccount = Accounts.Count > 0 ? Accounts[0] : null;
            OnPropertyChanged(nameof(SelectedAccount));
            LoadEditor(_selectedAccount);
        }

        _store.Save();
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

        OnPropertyChanged(nameof(IsEditing));
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

    private void LoadEditor(UserAccount? account)
    {
        if (account is null)
        {
            EditUserName = string.Empty;
            EditPassword = string.Empty;
            EditDisplayName = string.Empty;
            EditIsEnabled = true;
            FunctionOptions.Clear();
            return;
        }

        EditUserName = account.UserName;
        EditPassword = account.Password;
        EditDisplayName = account.DisplayName;
        EditIsEnabled = account.IsEnabled;

        BuildFunctionOptions(account.Functions);
    }

    private void BuildFunctionOptions(HashSet<AppFunction> granted)
    {
        FunctionOptions.Clear();

        foreach (var function in AppFunctionExtensions.AllFunctions)
            FunctionOptions.Add(new FunctionOption(function, granted.Contains(function)));
    }

    private void SetAllFunctions(bool value)
    {
        foreach (var option in FunctionOptions)
            option.IsChecked = value;
    }

    private void SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        OnPropertyChanged(propertyName);
    }

    private void OnPropertyChanged([CallerMemberName] string propertyName = "")
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}