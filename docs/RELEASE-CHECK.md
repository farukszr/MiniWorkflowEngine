# Yerel doğrulama

Uygulama için hazır hesap veya süreç verisi oluşturulmaz. İlk yönetici hesabı
`--create-admin`, veritabanı/temel roller `--initialize-database` komutuyla kurulur.
Diğer kullanıcı ve süreçler yönetim ekranlarından oluşturulur.

```powershell
dotnet build MiniWorkflowEngine.slnx --configuration Release --no-restore
dotnet run --project tests/MiniWorkflowEngine.IntegrationChecks --configuration Release --no-build --no-restore
dotnet ef migrations has-pending-model-changes --project MiniWorkflowEngine.csproj --configuration Release --no-build
```

Entegrasyon kontrolleri kendi geçici veritabanlarında kullanıcı ve süreç
oluşturur; test sonunda bu veritabanları silinir. Uygulama veritabanı kullanılmaz.
Kontroller rol/sahiplik, CSRF, süreç sürümleri, koşullu adımlar, talep ekranları,
eski form, eşzamanlı karar ve SQL hatasında geri alma davranışlarını kapsar.

GitHub Actions dosyası hazırdır; uzak çalıştırma sonucu GitHub üzerinden ayrıca
kontrol edilmelidir. Kurulum için [README](../README.md), tasarım için
[mimari belgesi](ARCHITECTURE.md) kullanılabilir.
