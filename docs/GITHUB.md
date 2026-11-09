# GitHub sunum ve teslim rehberi

Önerilen depo adı: **MiniWorkflowEngine**

Önerilen açıklama:

> ASP.NET Core MVC (.NET 10), EF Core ve SQL Server ile sürümlü süreç tanımları,
> sıralı/koşullu onay, Identity rolleri ve izlenebilir talep yönetimi.

Önerilen etiketler: `aspnet-core`, `dotnet`, `csharp`, `workflow`, `bpm`,
`entity-framework-core`, `sql-server`, `portfolio`.

## İnceleme sırası

1. README'deki kurulum adımları ve ilk yönetici hesabı.
2. [Mimari](ARCHITECTURE.md): sürümleme, transaction ve yetki kararları.
3. `Services/Workflow/WorkflowEngine.cs`: motorun ana akışı.
4. `tests/MiniWorkflowEngine.IntegrationChecks`: HTTP ve eşzamanlılık kontrolleri.

## Yerel doğrulama

```powershell
dotnet restore MiniWorkflowEngine.slnx
dotnet tool restore
dotnet build MiniWorkflowEngine.slnx --configuration Release --no-restore
dotnet run --project tests/MiniWorkflowEngine.IntegrationChecks --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project MiniWorkflowEngine.csproj --configuration Release --no-build
```

`.github/workflows/ci.yml` derleme, model ve entegrasyon kontrollerini Windows
runner üzerinde çalıştıracak şekilde hazırlanmıştır. LocalDB gerektiği için
Windows kullanılır. İlk push/PR sonrasında Actions sonucunu ayrıca kontrol edin.
Akış dağıtım yapmaz. Resmi [checkout](https://github.com/actions/checkout) ve
[setup-dotnet](https://github.com/actions/setup-dotnet) action'ları kullanılır.

## İlk paylaşım

Git deposu yoksa mevcut proje klasöründe başlatıp dosyaları inceleyerek ilk
commit'i oluşturun. GitHub'da seçtiğiniz hesap altında boş bir depo açıp onun
verdiği uzak adresi kullanın. Uzak depoya otomatik gönderim yapılmaz.

Kişisel bağlantı bilgileri, sertifikalar, SQL veri dosyaları ve yerel logları
paylaşmayın. `.gitignore` bunların yaygın türlerini dışlar. Bağımlılık lisansları
`wwwroot/lib` altında korunmuştur; projenin kendi lisansı ayrıca seçilmelidir.

## Kısa portföy anlatımı

“Süreç tanımını koddan ayırıp veritabanında sürümlü hale getirdim. Bir talep,
oluşturulduğu sürümün sıralı adımlarını izliyor; tutar ve departman koşulları
uygun adımları seçiyor. Rol ve sahiplik kararlarını sunucuda tekrar kontrol
ediyorum. Eski formlar, eşzamanlı kararlar ve işlem geçmişi tutarlılığı gerçek
SQL Server LocalDB testleriyle doğrulanıyor.”
