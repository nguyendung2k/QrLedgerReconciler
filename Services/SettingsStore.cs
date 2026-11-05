using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using QrLedgerReconciler.Models;

namespace QrLedgerReconciler.Services;

/// <summary>
/// Đọc/ghi cấu hình ứng dụng (không phải tài khoản) vào
/// <c>%LocalAppData%\QrLedgerReconciler\settings.json</c>.
///
/// Hiện chỉ chứa danh sách các chức năng "chạy ẩn" (headless) — chạy không mở
/// cửa sổ Chrome, chỉ lấy kết quả cuối cùng. Danh sách này gồm TẤT CẢ các chức
/// năng; người dùng tự chọn chức năng nào chạy ẩn.
/// </summary>
public sealed class SettingsStore
{
    private static readonly AppFunction[] HeadlessCandidates = AppFunctionExtensions.AllFunctions.ToArray();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public string FilePath { get; }

    /// <summary>Các chức năng được bật "chạy ẩn" (headless).</summary>
    public HashSet<AppFunction> HeadlessFunctions { get; } = new();

    private SettingsStore(string filePath)
    {
        FilePath = filePath;
    }

    /// <summary>Tạo store và nạp cấu hình từ đĩa (không có file thì để rỗng).</summary>
    public static SettingsStore Load()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "QrLedgerReconciler");

        var store = new SettingsStore(Path.Combine(dir, "settings.json"));

        try
        {
            store.LoadFromDisk();
        }
        catch (Exception)
        {
            // File hỏng/không đọc được: giữ file cũ, chạy với cấu hình rỗng.
            store.BackupCorruptFile();
        }

        return store;
    }

    private void LoadFromDisk()
    {
        if (!File.Exists(FilePath))
            return;

        var json = File.ReadAllText(FilePath);
        var data = JsonSerializer.Deserialize<SettingsFile>(json, JsonOptions);

        if (data?.HeadlessFunctions is null)
            return;

        foreach (var function in data.HeadlessFunctions)
        {
            if (HeadlessCandidates.Contains(function))
                HeadlessFunctions.Add(function);
        }
    }

    private void BackupCorruptFile()
    {
        HeadlessFunctions.Clear();

        try
        {
            if (File.Exists(FilePath))
                File.Copy(FilePath, FilePath + $".bak-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
        }
        catch
        {
            // Không sao lưu được thì thôi.
        }
    }

    /// <summary>Chức năng có được chạy ẩn không.</summary>
    public bool IsHeadless(AppFunction function) => HeadlessFunctions.Contains(function);

    /// <summary>Bật/tắt chạy ẩn cho một chức năng (chỉ các chức năng đọc được hỗ trợ).</summary>
    public void SetHeadless(AppFunction function, bool on)
    {
        if (on)
            HeadlessFunctions.Add(function);
        else
            HeadlessFunctions.Remove(function);
    }

    /// <summary>Các chức năng có thể bật chạy ẩn (để UI liệt kê checkbox).</summary>
    public static IReadOnlyList<AppFunction> HeadlessCandidatesList => HeadlessCandidates;

    /// <summary>Lưu ra đĩa (ghi temp rồi replace để tránh hỏng file khi crash).</summary>
    public void Save()
    {
        var data = new SettingsFile
        {
            HeadlessFunctions = HeadlessFunctions.ToList(),
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

    private sealed class SettingsFile
    {
        public List<AppFunction>? HeadlessFunctions { get; set; }
    }
}
