# IKO.Zeugnis

Öğretmenlerin iPad, Windows veya Mac üzerinde karne doldurduğu; yöneticinin (Verwaltung) dosyaları dağıttığı ve dönüşleri birleştirdiği platform bağımsız uygulama. Tamamen yeni bir uygulamadır; önceki projelerden kod veya veri almaz.

## İlke

- **Tek uygulama, iki rol:** Aynı PWA'da `Lehrkraft` ve `Verwaltung` ekranları. Kurulum ya da yönetici yetkisi gerekmez; tarayıcıdan açılır ve ana ekrana eklenebilir.
- **Sunucu yok:** Tüm veriler şifreli dosyalarda durur. Yayınlanan uygulama boştur; okul adı, logo ve şablonlar dahil hiçbir veri içermez.
- **Arayüz Almanca,** geliştirme notları Türkçedir.

## Dosya türleri

| Uzantı | Kimde | İçerik |
|---|---|---|
| `.schuldatei` | Verwaltung | Tüm okul: öğrenciler, öğretmenler, kataloglar, karneler, dağıtılan görevler, kayıt geçmişi |
| `.an-lehrkraft` | Öğretmene giden | Yalnız o öğretmenin sınıfı, öğrencileri ve izinli alanları |
| `.an-verwaltung` | Öğretmenden dönen | Doldurulan değerler |

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
- [x] **0.2:** Verwaltung içinde temel veriler: okul bilgileri, öğretmenler, sınıflar, öğrenciler (elle ve CSV ile; Excel için „CSV UTF-8“ ya da klasik CSV, Latin-1 umlautlar tanınır), kaydedilmemiş değişiklik uyarısı
- [x] **0.2.1:** Katalog düzenleyici: her sınıf düzeyi ve eğitim yolu için bir katalog; bölümler (işaretleme, not, serbest metin, seçim), kriterler, değerlendirme ölçeği, kopyalama, `.zkatalog` dışa/içe aktarma. Anahtarlar sabit kalır; silinen kimlikler yeniden kullanılmaz
- [x] **0.3:** Değerlendirme ekranı (iPad öncelikli; öğretmen ve Verwaltung aynı ekranı kullanır): öğrenci listesi ve ilerleme, dokunmatik ölçek/not/seçim düğmeleri, serbest metin, şifreli yerel ara kayıt (sayfa yenilense de veri kaybolmaz), dönüş dosyası oluşturma
- [x] **0.4:** Verwaltung'da görevler: öğretmen, sınıf, öğrenci ve bölüm seçimi; otomatik parola (okul dosyasında saklanır); çakışma uyarısı; dosyayı yeniden üretme, süre uzatma, kapatma. Dönüşler: birden çok dosya, parolasız eşleştirme, önizleme, alan bazında çakışma kararı, dönüş kaydı
- [x] **0.5:** Word şablonu yükleme ve karne çıktısı (`Zeugnis.Export`, OpenXml, tarayıcıda): öğrenci başına `.docx`, sınıf için ZIP. 0.5.1–0.5.2: sol panel, ana sayfada adım adım açıklama
- [x] **0.6:** 20 dakika işlem yapılmazsa açık görev/okul dosyası kapanır (2 dakika önce uyarı; kaydedilmemiş okul dosyası önce indirilir), yeni sürüm yalnızca kullanıcı onayıyla yüklenir
- [x] **0.15:** Auftrag oluşturma, dönüş alma, süre uzatma, kapatma/açma ve öğretmen parolası yenileme sonrasında yeni Schuldatei revizyonu otomatik indirilir
- [x] **0.16:** Katalogdaki her not, Ankreuzgruppe, metin, seçim, sayı ve tarih bağımsız bir elemandır; Auftrag seçimi tür/Fach gruplaması yapmadan kullanıcının katalog sırasını aynen korur
- [x] **0.17:** Katalog elemanları tutma simgesinden sürüklenip listenin herhangi bir aralığına bırakılabilir; element numaraları yeni konuma göre otomatik güncellenir, ▲/▼ alternatifi korunur
- [x] **0.17.1:** Sürükleme fare/dokunma için pointer tabanlıdır; taşınan kart görünür bir hayalet olarak imleci takip eder, hedef aralık vurgulanır ve ekran kenarında otomatik kaydırma çalışır
- [x] **0.17.2:** Element ekleme formundaki Titel, Fach, Art ve Hinzufügen alanları çakışmayan responsive grid düzenine geçirildi
- [x] **0.18:** Yeni element oluştururken başlangıç/ara/son konumu seçilebilir; mevcut `Element N` numarası açılır listeden değiştirilerek kart doğrudan istenen sıraya taşınır
- [x] **0.18.1:** Element taşındıktan sonra pozisyon select'leri kalıcı kart anahtarı ve yeni indeks anahtarıyla yeniden oluşturulur; ekranda aynı numaranın iki kez görünmesi engellenir
- [x] **0.18.2:** `nur unvollständige` filtresi tamamlanmış seçili öğrenciyi artık istisna olarak göstermez; liste ve gezinme yalnız gerçekten eksik öğrencileri içerir
- [x] **0.19:** Lehrkraft ve Verwaltung değerlendirme ekranlarında boş Ankreuz satırları ile boş Note/Text/Auswahl/Zahl/Datum elementleri açık turuncu vurgulanır; doldurulunca vurgu kalkar
- [ ] Sonra: iPad gerçek cihaz testi, Word tag yardım ekranı, yeni yarıyıl için okul dosyası devri

## Kurallar

- Gerçek öğrenci verileri, parolalar ve `.schuldatei`/`.an-lehrkraft`/`.an-verwaltung` dosyaları asla Git'e eklenmez (`.gitignore` bunları dışlar).
- Her işlevsel değişiklikte `Directory.Build.props` içindeki sürüm artırılır.
- Uyarılar hata sayılır (`TreatWarningsAsErrors`).

## Yayın (GitHub Pages)

**Canlı uygulama:** https://ibrahimkuralozkan.github.io/IKO.Zeugnis/

Repo public; Pages kaynağı "GitHub Actions", repo değişkeni `PAGES_ENABLED=true`. `main` dalına her push testleri çalıştırır ve geçerse uygulamayı yayınlar. Yayınlanan uygulama boştur; okul verisi yalnızca şifreli dosyalarda durur. Yayını durdurmak için `PAGES_ENABLED` değişkenini silmek yeterli.

## İş akışı (kısa)

1. **Verwaltung** → okul dosyası, öğretmenler, sınıflar, öğrenciler, kataloglar.
2. **Aufträge** → öğretmen, sınıf ve bölümleri seç → `.an-lehrkraft` iner, parola ekranda görünür. **Okul dosyasını kaydet.**
3. Dosyayı LOGINEO üzerinden, parolayı ayrı bir kanaldan öğretmene ilet.
4. **Lehrkraft** → öğretmen dosyayı açar, doldurur (ara kayıt otomatik), `.an-verwaltung` oluşturur ve LOGINEO'ya koyar.
5. **Aufträge → Rückgaben einlesen** → önizleme, çakışma varsa karar ver, "Übernehmen" → okul dosyasını kaydet.

Dönüş dosyasının başlığında şifrelenmemiş ama doğrulanan bir görev numarası bulunur (`Ref`). Verwaltung bu numarayla doğru görevi ve parolasını bulur. Numara kişisel veri içermez ve değiştirilirse dosya açılmaz.
