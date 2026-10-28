using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QrLedgerReconciler.Models;

namespace QrLedgerReconciler.Services;

/// <summary>
/// Đọc/ghi danh sách tài khoản EGAS + mật khẩu admin vào
/// <c>%LocalAppData%\QrLedgerReconciler\accounts.json</c>.
///
/// Đặt ở LocalAppData (cùng chỗ với chrome-profile) thay vì cạnh file EXE để:
/// - không bị ghi đè khi cài lại / chạy từ Program Files,
/// - không lọt vào git.
///
/// Mật khẩu tài khoản EGAS lưu plaintext theo yêu cầu người dùng.
/// Mật khẩu admin chỉ dùng để mở màn hình quản lý nên lưu dạng SHA-256 + salt.
/// </summary>
public sealed class AccountStore
{
    private const string DefaultAdminPassword = "admin";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public ObservableCollection<UserAccount> Accounts { get; } = new();

    public string FilePath { get; }

    /// <summary>Hash mật khẩu admin (SHA-256 của salt + mật khẩu, Base64).</summary>
    public string AdminPasswordHash { get; private set; } = string.Empty;

    /// <summary>Salt ngẫu nhiên dùng chung cho hash admin (Base64).</summary>
    public string AdminSalt { get; private set; } = string.Empty;

    /// <summary>True khi file chưa từng tồn tại → đang dùng mật khẩu admin mặc định.</summary>
    public bool IsFirstRun { get; private set; }

    /// <summary>True khi mật khẩu admin vẫn là "admin" (mật khẩu mặc định).</summary>
    public bool IsDefaultAdminPassword => ValidateAdmin(DefaultAdminPassword);

    private AccountStore(string filePath)
    {
        FilePath = filePath;
    }

    /// <summary>Tạo store và nạp dữ liệu; chưa có file thì seed dữ liệu mặc định.</summary>
    public static AccountStore Load()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QrLedgerReconciler");

        var store = new AccountStore(Path.Combine(dir, "accounts.json"));

        try
        {
            store.LoadFromDisk();
        }
        catch (Exception)
        {
            // File hỏng/không đọc được: giữ file cũ lại để người dùng có thể
            // khôi phục tay, rồi chạy với dữ liệu mặc định.
            store.BackupCorruptFile();
        }

        if (store.Accounts.Count == 0)
        {
            store.SeedDefaults();
            store.Save();
        }

        return store;
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(FilePath))
        {
            IsFirstRun = true;
            return;
        }

        var json = File.ReadAllText(FilePath);
        var data = JsonSerializer.Deserialize<AccountFile>(json, JsonOptions);

        if (data is null) return;

        AdminPasswordHash = data.AdminPasswordHash ?? string.Empty;
        AdminSalt = data.AdminSalt ?? string.Empty;

        foreach (var account in data.Accounts ?? new List<UserAccount>())
        {
            account.Functions ??= new HashSet<AppFunction>();
            Accounts.Add(account);
        }

        if (string.IsNullOrEmpty(AdminPasswordHash) || string.IsNullOrEmpty(AdminSalt))
            SetAdminPassword(DefaultAdminPassword);
    }

    private void BackupCorruptFile()
    {
        IsFirstRun = true;
        Accounts.Clear();

        try
        {
            if (File.Exists(FilePath))
                File.Copy(FilePath, FilePath + $".bak-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
        }
        catch
        {
            // Không sao lưu được thì thôi, vẫn chạy tiếp với dữ liệu mặc định.
        }
    }

    /// <summary>
    /// Seed 4 tài khoản đang hard-code trước đây, mật khẩu rỗng và đủ 7 quyền
    /// để không làm gãy luồng sử dụng hiện tại.
    /// </summary>
    private void SeedDefaults()
    {
        if (string.IsNullOrEmpty(AdminPasswordHash))
            SetAdminPassword(DefaultAdminPassword);

        foreach (var userName in new[] { "2112223", "2212222", "2112229", "2112220" })
        {
            Accounts.Add(new UserAccount
            {
                UserName = userName,
                Password = string.Empty,
                DisplayName = userName,
                IsEnabled = true,
                Functions = new HashSet<AppFunction>(AppFunctionExtensions.AllFunctions),
            });
        }
    }

    /// <summary>Lưu ra đĩa (ghi temp rồi replace để tránh hỏng file khi crash).</summary>
    public void Save()
    {
        var data = new AccountFile
        {
            AdminPasswordHash = AdminPasswordHash,
            AdminSalt = AdminSalt,
            Accounts = Accounts.ToList(),
        };

        var json = JsonSerializer.Serialize(data, JsonOptions);

        var dir = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(dir);

        var tempPath = FilePath + ".tmp";
        File.WriteAllText(tempPath, json, Encoding.UTF8);

        if (File.Exists(FilePath))
            File.Replace(tempPath, FilePath, null);
        else
            File.Move(tempPath, FilePath);
    }

    public bool ValidateAdmin(string password)
        => !string.IsNullOrEmpty(AdminPasswordHash)
           && CryptographicOperations.FixedTimeEquals(
               Convert.FromBase64String(AdminPasswordHash),
               ComputeHash(AdminSalt, password));

    public void SetAdminPassword(string password)
    {
        if (string.IsNullOrEmpty(AdminSalt))
            AdminSalt = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));

        AdminPasswordHash = Convert.ToBase64String(ComputeHash(AdminSalt, password));
    }

    private static byte[] ComputeHash(string salt, string password)
        => SHA256.HashData(Encoding.UTF8.GetBytes(salt + password));

    /// <summary>Kiểm tra tên đăng nhập đã tồn tại (bỏ qua chính tài khoản đang sửa).</summary>
    public bool IsUserNameTaken(string userName, UserAccount? except = null)
        => Accounts.Any(a =>
            !ReferenceEquals(a, except) &&
            string.Equals(a.UserName, userName, StringComparison.OrdinalIgnoreCase));

    private sealed class AccountFile
    {
        public string? AdminPasswordHash { get; set; }
        public string? AdminSalt { get; set; }
        public List<UserAccount>? Accounts { get; set; }
    }
}