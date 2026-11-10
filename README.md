# MiniWorkflowEngine

MiniWorkflowEngine, şirket içindeki talepleri doğru kişiye yönlendirir ve verilen kararları kaydeder.\
Yönetici onay kurallarını sürükle-bırak ile tanımlar.\
Çalışan talebini gönderir ve durumunu takip eder.\
Yetkili, kendisine atanan talepleri onaylar, reddeder veya düzeltmeye gönderir.

Örneğin şirket, 10.000 TL üzerindeki masraf taleplerini onaylayıcıya yönlendirebilir. Çalışan, talebin hangi aşamada olduğunu ve sonucu uygulamadan görebilir.

**[Ekran görüntülerini gör](docs/screenshots/README.md)**

## Kurulum

Windows, **.NET SDK 10.0.403** ve **SQL Server Express LocalDB** gerekli.

1. Projeyi bilgisayarına indir. Dosya Gezgini'nde `MiniWorkflowEngine.csproj` dosyasının bulunduğu klasörü aç. Adres çubuğuna `powershell` yazıp Enter'a bas.

2. Yerel HTTPS sertifikasını onayla ve veritabanını hazırla. Sertifika onayı sorulursa **Evet**'i seç. İkinci komuttan sonra **Veritabanı ve uygulama rolleri hazırlandı** mesajını bekle.

   ```powershell
   dotnet dev-certs https --trust
   dotnet run --project MiniWorkflowEngine.csproj --launch-profile https -- --initialize-database
   ```

3. İlk yönetici hesabını oluştur. E-posta adresini ve ad soyadını yaz, ardından parolanı iki kez gir. Parola en az 10 karakter olmalı ve büyük harf, küçük harf, rakam ve özel karakter içermeli. Yazarken parola ekranda görünmez. **Yönetici hesabı oluşturuldu** mesajını görünce devam et.

   ```powershell
   dotnet run --project MiniWorkflowEngine.csproj --launch-profile https -- --create-admin
   ```

4. Uygulamayı başlat ve PowerShell penceresini açık tut. [Giriş ekranını aç](https://localhost:7062/Account/Login) ve oluşturduğun yönetici hesabıyla giriş yap.

   ```powershell
   dotnet run --project MiniWorkflowEngine.csproj --launch-profile https
   ```

## İlk akışını dene

İlk girişte ana sayfadan **10.000 TL masraf örneğini aç**. **Teste geç → Kaydet ve test et** seçeneğine bas. **Örnek tutar** alanına `15000` yazıp **Önizle** düğmesine bas. Akış **Masrafı incele** adımına gider. `10000` ile tekrar denediğinde doğrudan tamamlanır.

Akışı kullanıma açmak için **Yayımlamaya geç → Yayımla** yolunu izle. Çalışanlar süreci **Talep oluştur** ekranından seçebilir. Onaylayıcılar kendilerine atanan talepleri **Bekleyen onaylar** ekranında görür.
