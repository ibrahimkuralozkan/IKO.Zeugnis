# IKO.Zeugnis

Öğretmenlerin iPad, Windows veya Mac üzerinde karne doldurduğu; yöneticinin (Verwaltung) dosyaları dağıttığı ve dönüşleri birleştirdiği platform bağımsız uygulama. Önceki projelere bağlı olmadan sıfırdan yazıldı.

## İlke

- **Tek uygulama, iki rol:** Aynı PWA'da `Lehrkraft` ve `Verwaltung` ekranları. Kurulum ya da yönetici yetkisi gerekmez; tarayıcıdan açılır ve ana ekrana eklenebilir.
- **Sunucu yok:** Tüm veriler şifreli dosyalarda durur. Yayınlanan uygulama boştur; okul adı, logo ve şablonlar dahil hiçbir veri içermez.
- **Arayüz Almanca,** geliştirme notları Türkçedir.

## Dosya türleri

| Uzantı | Kimde | İçerik |
|---|---|---|
| `.zschule` | Verwaltung | Tüm okul: öğrenciler, öğretmenler, kataloglar, karneler, dağıtılan görevler, kayıt geçmişi |
| `.zauftrag` | Öğretmene giden | Yalnız o öğretmenin sınıfı, öğrencileri ve izinli alanları |
| `.zantwort` | Öğretmenden dönen | Doldurulan değerler |

Üçü de aynı zarfı kullanır: PBKDF2-SHA256 (310.000 tur) → AES-256-GCM, parola en az 12 karakter. Dosya türü kimliği doğrulanan ek veriye (AAD) bağlıdır; örneğin bir dönüş dosyası okul dosyası gibi açılamaz. Biçim `Zeugnis.Exchange/EnvelopeCodec.cs` içinde tanımlıdır. Tarayıcıda şifreleme Web Crypto ile yapılır (`wwwroot/js/zeugnis.js`), çünkü .NET WASM AES-GCM desteklemez. İki uygulama aynı biçimi üretir; tarayıcıda şifrelenen bir dosyanın .NET'te açıldığı doğrulandı.

## Birleştirme kuralları (`Zeugnis.Exchange/MergeEngine.cs`)

1. Her alanın bir anahtarı vardır: `r:Deutsch.Lesen.03` (ölçek), `t:Allgemein` (metin), `g:Sport` (not), `s:Versetzung` (seçim). Öğretmene `r:Sport.*` gibi izin desenleri verilir.
2. Dönüş, yalnızca bu okul dosyasında verilmiş ve geri alınmamış bir göreve ait olabilir. Aynı dönüş ikinci kez alınmaz.
3. İzinsiz bir öğrenci ya da alan varsa **dönüşün tamamı** reddedilir.
4. Öğretmenin değiştirmediği değerler dikkate alınmaz.
5. Görev verildikten sonra Verwaltung da aynı alanı farklı bir değerle değiştirdiyse bu bir **çakışmadır**. Karar verilmezse Verwaltung'un değeri korunur.
6. Aynı öğretmenin sonradan gönderdiği düzeltme dönüşleri çakışma sayılmaz.

Her alan revizyon numarası, değiştiren kişi ve zaman bilgisi taşır.

## Projeler

- `Zeugnis.Domain`: Okul, öğrenci, katalog, alan anahtarları, karne değerleri
- `Zeugnis.Exchange`: Dosya biçimleri, şifreleme, görev oluşturma, birleştirme
- `Zeugnis.App`: Blazor WebAssembly PWA
- `Zeugnis.Tests`: xUnit testleri

## Geliştirme

```powershell
dotnet test IKO.Zeugnis.sln
dotnet run --project Zeugnis.App --launch-profile http   # http://localhost:5032
```

`main` dalına her gönderimde GitHub Actions testleri çalıştırır ve PWA'yı GitHub Pages'e yayınlar. Yayın paketinde veri dosyası bulunursa yayın durdurulur.

## Sürüm planı

- [x] **0.1:** İskelet, alan modeli, şifreleme, birleştirme motoru, iki rollü PWA kabuğu
- [ ] **0.2:** Eski `IKO.ZeugnisManager.Web` AppData'sının (öğrenciler, kataloglar, öğretmenler, Word şablonları) `.zschule` içine aktarılması
- [ ] **0.3:** Öğretmen değerlendirme ekranı (iPad öncelikli), dönüş oluşturma
- [ ] **0.4:** Verwaltung'da görev dağıtımı, dönüş önizleme ve çakışma ekranı
- [ ] **0.5:** Word karne çıktısı (`Zeugnis.Export`, OpenXml, tarayıcıda)
- [ ] Sonra: hareketsizlikte otomatik kapanma, kullanıcı onaylı PWA güncellemesi, katalog düzenleyici

## Kurallar

- Gerçek öğrenci verileri, parolalar ve `.zschule`/`.zauftrag`/`.zantwort` dosyaları asla Git'e eklenmez (`.gitignore` bunları dışlar).
- Her işlevsel değişiklikte `Directory.Build.props` içindeki sürüm artırılır.
- Uyarılar hata sayılır (`TreatWarningsAsErrors`).
