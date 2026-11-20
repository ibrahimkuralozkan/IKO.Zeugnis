# IKO.Zeugnis – Claude devir notu (handoff)

Bu dosya yeni bir Claude Code oturumunun (başka bir bilgisayarda da) projeyi hemen devralması içindir.
Önce bu dosyayı, sonra `README.md`'yi oku; ardından `git log --oneline` ve `git status` ile durumu kontrol et.

## Kullanıcı ve çalışma kuralları

- **İletişim Türkçe**, uygulama arayüzü **Almanca** (öğretmenler için), kod yorumları Almanca/İngilizce.
- Bu **tamamen yeni** bir uygulama. Eski projelerden (`IKO.ZeugnisManager.Ipad`, `.Web`, `.V2`, `ZeugnisPilot` …) **kod veya veri alınmaz** – kullanıcı bunu açıkça istedi.
- Commit/push yalnızca kullanıcı isteyince. Commit mesajı İngilizce, sonunda attribution satırı.
- Her işlevsel değişiklikte `Directory.Build.props` sürümü artırılır (SemVer). Uyarılar hata sayılır.
- **Gerçek öğrenci verisi, parolalar, `.zschule/.zauftrag/.zantwort` dosyaları asla Git'e girmez.** Testlerde yalnız uydurma veri (`Test-Kennwort-123` vb.).
- Kullanıcının okul bilgisayarında admin yetkisi yok → kurulum gerektiren çözümler (MAUI workload vb.) yok; PWA bilinçli seçim.
- Hukuki (DSGVO) kesinlik vaat etme; teknik önlemleri anlat.

## Amaç

Öğretmenler iPad/Windows/Mac'te karne değerlendirmesi girer. Verwaltung (kullanıcı) öğretmenlere şifreli görev dosyaları dağıtır, dönüşleri birleştirir. Sunucu yok; her şey şifreli dosyalar ve LOGINEO (okul bulutu) üzerinden. İleride başka okullara satılabilir → okul adı, logo, katalog, şablon uygulamada değil, okul dosyasında.

## Mimari

| Proje | İçerik |
|---|---|
| `Zeugnis.Domain` | `FieldKey` (`r:` ölçek, `g:` not, `t:` metin, `s:` seçim), `FieldScope` desenleri (`r:Sport.*`), `Student/Teacher/SchoolClass/Term`, `Catalog/CatalogSection/CatalogItem`, `StudentReport` + `FieldValue` (revizyonlu) |
| `Zeugnis.Exchange` | `EnvelopeCodec` (PBKDF2-SHA256 310k → AES-256-GCM, dosya türü + opsiyonel `Ref` AAD'de), `Documents` (`SchoolFile`, `AssignmentFile`, `ResponseFile`, `IssuedAssignment`), `MasterData`, `StudentCsvImport`, `CatalogEditor`, `CatalogTransfer` (.zkatalog), `AssignmentFactory` (Create/Build/FindOverlaps/GeneratePassword), `ResponseBuilder`, `ResponseReader`, `FieldLabels`, `MergeEngine` (Preview/Commit), `GradingContext` |
| `Zeugnis.App` | Blazor WASM PWA. `/lehrkraft`, `/verwaltung/*` (Übersicht, Schule, Lehrkräfte, Klassen, Schüler, Kataloge, Zeugnisse, Aufträge). `Shared/GradingView.razor` iki rolde ortak. `wwwroot/js/zeugnis.js`: Web Crypto, indirme, unload uyarısı, şifreli taslak (localStorage) |
| `Zeugnis.Tests` | xUnit, 87 test |

### Önemli tasarım kararları

- **.NET WASM'da AES-GCM/PBKDF2 yok** → tarayıcıda `zeugnis.js` (Web Crypto) ham şifrelemeyi yapar, dosya biçimi C#'ta (`EnvelopeCodec`). İki yön de doğrulandı (.NET ↔ tarayıcı).
- **Görev parolası** okul dosyasında (`IssuedAssignment.Password`) saklanır; dönüş dosyasının başlığındaki `Ref` (= AssignmentId "N") ile Verwaltung parolasız eşleştirir. `Ref` AAD'ye bağlı, değiştirilirse dosya açılmaz.
- **Birleştirme**: görev verilirken `Baseline` kaydedilir. Öğretmen değiştirmediyse yok sayılır; Verwaltung da aynı alanı değiştirdiyse (revizyon farklı) **çakışma**, varsayılan Verwaltung'un değeri. İzinsiz öğrenci/alan → dönüşün tamamı reddedilir. Tekrar import engelli. Commit sonrası baseline güncellenir (düzeltme dönüşleri çakışma sayılmaz).
- **Katalog anahtarları sabit**: bölüm anahtarı başlıktan slug (nokta yok), başlık değişse de değişmez; kriter Id'leri (`01`, `02`…) asla yeniden kullanılmaz (`NextItemNumber`).
- **Ölçek değerleri** konum olarak saklanır (`"1"` = en iyi). Not `"1"`–`"6"`, seçim = seçenek Id'si.
- Öğretmen taslağı: her değişiklikten 1,5 sn sonra aynı parolayla şifrelenip `localStorage["zeugnis.draft.{AssignmentId}"]`'e yazılır; görev tekrar açılınca geri yüklenir.
- Okul dosyası yalnız bellekte; kaydetmek = yeni revizyonlu `.zschule` indirmek (LOGINEO'ya konur, eskisi silinir).

### Bilinen tuzaklar

- `.gitignore` içindeki Mac kuralı `*.app` Windows'ta `Zeugnis.App` klasörünü de gizliyordu → sonda `!Zeugnis.App/` istisnası var.
- Blazor `<select value=…>` seçenekler değişince yanlış gösteriyor → seçeneklerde `selected="@(…)"` kullan (Schueler, Kataloge, TeacherSelect).
- Derlemeden önce çalışan geliştirme sunucusunu durdur (dosya kilitleri):
  `Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | ? { $_.CommandLine -like '*Zeugnis.App*' } | % { Stop-Process -Id $_.ProcessId -Force }`
- Tarayıcı testinde dosya seçimi: `DataTransfer` ile `input[type=file]`'a `File` atanıp `change` tetiklenir; indirmeleri yakalamak için `zeugnis.download` geçici olarak değiştirilir.
- Web Crypto yalnız HTTPS veya localhost'ta çalışır → iPad testi için yayın (GitHub Pages) gerekir.

## Çalıştırma

```powershell
dotnet test IKO.Zeugnis.sln
dotnet run --project Zeugnis.App --launch-profile http   # http://localhost:5032
```

**Canlı:** https://ibrahimkuralozkan.github.io/IKO.Zeugnis/ (repo **public**, 20.11.2026'da kullanıcı onayıyla açıldı).
Yayın: `.github/workflows/deploy-pwa.yml` – her `main` push'unda test + publish; Pages'e dağıtım repo değişkeni `PAGES_ENABLED=true` iken (ayarlı). Yayın paketinde veri dosyası (`.zschule/.zauftrag/.zantwort/.docx`) çıkarsa iş akışı durur. Base path otomatik olarak repo adı (`/IKO.Zeugnis/`); service worker base'i kendi konumundan hesaplar.

## Durum (20.11.2026)

Tamamlanan: v0.1 iskelet/şifreleme/birleştirme · v0.2 temel veriler + CSV · v0.2.1 katalog düzenleyici · v0.3 değerlendirme ekranı + şifreli taslak · v0.4 görevler + dönüş birleştirme (çakışma kararı) · v0.5 Word çıktısı · v0.5.1 sol panel (Lehrkraft/Verwaltung) · v0.5.2 ana sayfada adım adım açıklama · v0.6 hareketsizlikte otomatik kapanma + onaylı PWA güncellemesi.

- **Word çıktısı (`Zeugnis.Export`)**: şablon `SchoolFile.Templates` içinde, sınıf düzeyi + eğitim yolu başına bir `.docx`. İçerik denetimi tag'leri: `student.name|firstname|lastname|birthdate`, `class.name`, `school.name|address|town`, `term`, `date` ve alan anahtarları (`g:`, `t:`, `s:`, `r:`). İşaret kutusu: `r:Deutsch.Lesen.03#2` (değer 2 ise işaretli). Bilinmeyen tag'ler değiştirilmez. Verwaltung → Zeugnisse: yükleme, öğrenci başına `.docx`, sınıf ZIP.
- **Hareketsizlik (`Shared/IdleGuard.razor`, `zeugnis.idle`)**: 20 dk, 2 dk önce uyarı. Öğretmen: taslak zaten şifreli kayıtlı, görev kapanır. Verwaltung: kaydedilmemiş değişiklik varsa önce yeni revizyon indirilir, sonra kapanır (kaydetme başarısızsa açık kalır). Zaman damgası ile ölçülür (iPad arka plan zamanlayıcıları için).
- **Güncelleme (`Shared/UpdateNotice.razor`, `zeugnis.sw`)**: yeni service worker bekler; banner'da onaylanınca `SKIP_WAITING` + yeniden yükleme. Okul dosyası kaydedilmemişse düğme kapalı.
- **Visual Studio**: `launchSettings.json` içindeki "Visual Studio" profili F5'te `dotnet watch run --launch-profile http` çalıştırır (VS `http` profilinde 5000'e düşüyordu). VS'nin sunucusu çalışırken uygulamayı aynı klasörde `dotnet build` ile derleme; sayfa %96'da takılır (SRI). Kontrol için kaynağı geçici klasöre kopyalayıp orada derle/çalıştır.

## Sıradaki işler (öncelik sırasıyla)

1. **iPad gerçek cihaz testi** canlı adreste (Safari, Dosyalar uygulaması, LOGINEO indirme/yükleme, ana ekrana ekleme, çevrimdışı açılış, güncelleme banner'ı).
2. Word: Verwaltung'da şablonda kullanılabilecek tag'leri listeleyen yardım ekranı; ZIP öncesi eksik alan kontrolü. Öğretmen tarafı hareketsizlik kapanışı tarayıcıda gerçek `.zauftrag` ile henüz denenmedi.
3. Yeni yarıyıl: önceki okul dosyasından temel verileri ve katalogları kopyalayarak yeni dosya oluşturma.
4. İsteğe bağlı: `.xlsx` doğrudan içe aktarma, sınıf görünümü (tüm öğrenciler × bir kriter tablosu).
