# MiniWorkflowEngine

ASP.NET Core MVC ile geliştirilmiş bir Low-Code iş akışı uygulaması.
Yönetici süreç ve form tasarlar. Çalışan yayımlanan formdan talep gönderir.
Görevli, atandığı adımı tamamlar, onaylar, reddeder veya düzeltme için iade eder.

Low-Code / BPM / Workflow Developer rollerinde süreç modelleme, iş kuralları,
yetkilendirme ve veri tutarlılığı becerilerini göstermek için hazırlanmıştır.

## İlk kez çalıştırma

Bilgisayarında **Windows**, **.NET SDK 10.0.403** ve **SQL Server Express LocalDB**
kurulu olmalı.

### 1. Proje klasöründe PowerShell aç

Dosya Gezgini'nde `MiniWorkflowEngine.csproj` dosyasının bulunduğu klasörü aç.
Pencerenin üstündeki adres çubuğuna `powershell` yaz ve Enter'a bas. Aşağıdaki
komutları açılan pencerede çalıştıracaksın.

### 2. Veritabanını hazırla

Önce yerel HTTPS sertifikasına güvenmek için şu komutu çalıştır. Onay penceresi
açılırsa **Evet**'e tıkla.

```powershell
dotnet dev-certs https --trust
```

Sonra uygulamanın veritabanını hazırlamak için şu komutu çalıştır.

```powershell
dotnet run --project MiniWorkflowEngine.csproj --launch-profile https -- --initialize-database
```

**“Veritabanı ve uygulama rolleri hazırlandı”** mesajını görünce devam et.
`Cannot open database` hatası görürsen [veritabanı bağlantısını kontrol et](#veritabanı-açılamıyor-hatası).

### 3. İlk yönetici hesabını oluştur

Aynı PowerShell penceresine şu komutu yaz. Uygulama zaten çalışıyorsa 1. adımdaki
yöntemle ayrı bir PowerShell penceresi açıp komutu orada çalıştır.

```powershell
dotnet run --project MiniWorkflowEngine.csproj --launch-profile https -- --create-admin
```

PowerShell sırayla şu bilgileri soracak.

| Ekrandaki soru | Ne yapmalısın? |
| --- | --- |
| Yönetici e-posta adresi | Girişte kullanacağın e-posta adresini yaz ve Enter'a bas. |
| Ad soyad | Adını ve soyadını yaz ve Enter'a bas. |
| Parola | Kendi parolanı yaz ve Enter'a bas. Yazdığın karakterler ekranda görünmez. |
| Parola tekrarı | Aynı parolayı tekrar yaz ve Enter'a bas. |

Parolan en az 10 karakter içermeli. Büyük harf, küçük harf, rakam ve özel
karakter kullan. **“Yönetici hesabı oluşturuldu”** mesajını görünce hesabın hazır.

### 4. Uygulamayı aç ve giriş yap

Uygulama açıksa doğrudan tarayıcıdaki giriş sayfasına dön. Henüz açmadıysan
PowerShell'de şu komutu çalıştır ve pencereyi açık tut.

```powershell
dotnet run --project MiniWorkflowEngine.csproj --launch-profile https
```

Giriş ekranına 3. adımda belirlediğin e-posta adresini ve parolayı yaz. **Giriş yap**
düğmesine bas. Henüz yayımlanmış bir süreç yoksa **Ana Sayfa** ilk kurulumu adım adım
gösterir. [Giriş ekranını aç](https://localhost:7062/Account/Login)

### Ekran görüntüleri

[Örnek masraf onayı akışının ekran görüntülerini inceleyin](docs/screenshots/README.md).

## Özellikler

- **Süreç tanımları → Görsel süreç oluştur** ile başlangıç, kullanıcı görevi, onay, karar ve bitiş düğümlerini bağlama.
- Aynı tasarım ekranında dinamik form alanları oluşturma ve bu alanları karar değişkeni olarak kullanma.
- Süreç detayındaki **Süreç önizleme** ile örnek değerlerden ilk görevi veya bitişi görme.
- **Departmanlar** üzerinden birim oluşturma ve kullanıcı üyeliği atama. Görevleri role, departmana ve kişiye göre sınırlandırma.
- **Raporlar** üzerinden süreç, departman, tarih ve durum bazında talep sayısı, gecikme ve ortalama kapanış süresi.
- Dinamik ve sürümlü süreç tanımları; taslak, yayımlama ve arşivleme.
- Sıralı onay adımları; tutar/departman koşulları ve otomatik adım atlama.
- Kayıtlı süreç sürümünde örnek tutar ve departmanla önizleme.
- Identity kullanıcıları, çoklu roller ve yönetici ekranları.
- Talep oluşturma, taslak düzenleme, gönderme, gerekçeli ret ve iptal.
- Taleplerim, role göre bekleyen onaylar ve işlem geçmişi.
- **Bekleyen onaylar** menüsünün yanında, kullanıcıdan karar bekleyen talep sayısı.
- **Bekleyen onaylar** ve **Tüm talepler** listelerinde adım bazında bekleme süresi ve **Yalnızca gecikenler** filtresi. Bir adım 24 saat beklediğinde gecikmiş sayılır.
- Yönetici, **Tüm talepler → İncele** üzerinden bekleyen bir adımı doğrudan onaylayabilir veya gerekçesiyle reddedebilir. Başka bir role atanmış adım için verilen karar yönetici kararı olarak kaydedilir.
- **Kullanıcı yönetimi → Şifre kurtarma** ekranından 30 dakikalık tek kullanımlık kod oluşturulur. Yönetici kodu güvenli bir kanaldan paylaşır. Kullanıcı giriş ekranındaki **Şifremi unuttum** bağlantısını kullanır.
- **Yönetim geçmişi** ekranında kullanıcı, rol, yayımlama, yönetici onay kararları ve kurtarma kodu oluşturma işlemleri görülür.
- Kendi talebine karar engeli, CSRF, hesap kilidi ve güvenli oturum.
- Eski form/çift karar koruması; durum ve geçmişi birlikte kaydeden transaction.
- Türkçe, responsive Razor arayüzü.

### Görsel bir süreç yayımlama

1. Başlangıçtan sonraki düğüme **İlerle** bağlantısı ekleyin. Kullanıcı görevinde rol seçin ve **Tamamla** çıkışını bağlayın.
2. Bir onay düğümünde **Onay** ve **Ret** bağlantılarını oluşturun. **Ret** bağlantısını sonucu **Reddedildi** olan bir bitiş düğümüne bağlayın.
3. Karar düğümünde bir **Koşul sağlanırsa** ve bir **Varsayılan yol** bağlantısı tanımlayın. Örneğin form tasarımcısında oluşturduğunuz `oncelik` alanını karar değişkeni olarak seçin.
4. **Taslağı kaydet → Süreç önizleme → Yayımla** adımlarını izleyin. Çalışan yayımlanan süreci **Talep oluştur** ekranında seçebilir.

## Teknolojiler

.NET 10 · ASP.NET Core MVC · C# · SQL Server · EF Core 10 · ASP.NET Core Identity ·
HTML/CSS/JavaScript · Bootstrap

## Veritabanı bağlantısı

Varsayılan bağlantı `appsettings.Development.json` içindedir; Windows kimlik
doğrulamasıyla `MiniWorkflowEngine` veritabanını kullanır. SQL Server bağlantısı
`ConnectionStrings__DefaultConnection` ortam değişkeniyle değiştirilebilir.
Normal açılışta otomatik migration veya hesap oluşturma çalışmaz.

Projeyi güncellediğinde mevcut veritabanına yeni tabloları eklemek için 2. adımdaki
`--initialize-database` komutunu yeniden çalıştırabilirsin. Mevcut kullanıcı ve talepler
korunur.

### Veritabanı açılamıyor hatası

`Cannot open database "MiniWorkflowEngine" requested by the login` hatası,
uygulamanın veritabanını açamadığını gösterir. Önceki bir kurulumdan kalan
`MiniWorkflowEngine.mdf` ve `MiniWorkflowEngine_log.ldf` dosyaları Windows kullanıcı
klasöründe (`%USERPROFILE%`) duruyor olabilir.

- **İlk kurulumsa** 2. adımdaki `--initialize-database` komutunu çalıştır. Başarı
  mesajını gördükten sonra 3. adımdaki `--create-admin` komutuna geç.
- **Daha önce kullandığın veritabanının dosyaları duruyorsa** önce bu dosyaları
  `(localdb)\MSSQLLocalDB` sunucusuna `MiniWorkflowEngine` adıyla yeniden bağla.
  Bağlantı sağlandıktan sonra `--create-admin` komutunu tekrar çalıştır.

## Doğrulama

```powershell
dotnet tool restore
dotnet build MiniWorkflowEngine.slnx --configuration Release
dotnet run --project tests/MiniWorkflowEngine.IntegrationChecks --configuration Release --no-build
dotnet ef migrations has-pending-model-changes --project MiniWorkflowEngine.csproj --configuration Release --no-build
```

Kontroller bir konsol doğrulama projesidir; `dotnet test` yerine yukarıdaki komut
kullanılır. Gerçek SQL Server LocalDB ve MVC test sunucusu üzerinde çalışır;
geçici veritabanları sonunda silinir. Eşzamanlı kararlar ve SQL hatasında geri alma
kontrolleri de dahildir. GitHub Actions için Windows CI dosyası hazırlanmıştır.
