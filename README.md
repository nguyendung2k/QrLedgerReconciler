# QR Ledger Reconciler

Ứng dụng console .NET đối chiếu **TỔNG CỘNG** của sổ cái EGAS TK `112716` với **Tổng số tiền thanh toán** của QR Code tĩnh HDBank.

## Điều kiện

- .NET SDK 8 trở lên.
- Có quyền truy cập mạng nội bộ EGAS và cổng HDBank.
- Lần đầu chạy Playwright, cài Chromium bằng lệnh `pwsh bin/Debug/net8.0/playwright.ps1 install chromium`.

## Cấu hình an toàn

Không ghi mật khẩu vào `appsettings.json`. Thiết lập biến môi trường trong PowerShell:

```powershell
$env:EGAS_USERNAME = "2112229"
$env:EGAS_PASSWORD = "<mật khẩu>"
```

## Chạy

```powershell
dotnet restore
dotnet build
pwsh bin/Debug/net8.0/playwright.ps1 install chromium
dotnet run -- --from "6/7/2026 14:00" --to "7/7/2026 13:59"
```

Khoảng QR được truy vấn chính xác từ `14:00:00` đến `13:59:59`; sổ cái sử dụng phút kết thúc `13:59`.

Mã trả về `0` khi khớp, `1` khi có chênh lệch và `2` khi tham số thời gian không hợp lệ.
