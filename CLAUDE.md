# IKO.Zeugnis – Claude devir notu (handoff)

Bu dosya yeni bir Claude Code oturumunun (başka bir bilgisayarda da) projeyi hemen devralması içindir.
Önce bu dosyayı, sonra `README.md`'yi oku; ardından `git log --oneline` ve `git status` ile durumu kontrol et.

## Kullanıcı ve çalışma kuralları

- **İletişim Türkçe**, uygulama arayüzü **Almanca** (öğretmenler için), kod yorumları Almanca/İngilizce.
- Bu **tamamen yeni** bir uygulama. Eski projelerden (`IKO.ZeugnisManager.Ipad`, `.Web`, `.V2`, `ZeugnisPilot` …) **kod alınmaz**.
  İstisna (kullanıcı kararı 20.11.2026): eski `IKO.ZeugnisManager.Web` uygulamasının **katalog metinleri, Word şablonları ve karne kuralları** yeni uygulamaya taşınabilir. **Öğrenci, öğretmen, not verisi asla.** Taşınan katalog/şablonlar repoya ve yayına girmez; yalnız okul dosyasına içe aktarılır (uygulama boş kalır).
- Commit/push yalnızca kullanıcı isteyince. Commit mesajı İngilizce, sonunda attribution satırı.
- Her işlevsel değişiklikte `Directory.Build.props` sürümü artırılır (SemVer). Uyarılar hata sayılır.
- **Gerçek öğrenci verisi, parolalar, `.schuldatei/.an-lehrkraft/.an-verwaltung` dosyaları asla Git'e girmez.** Testlerde yalnız uydurma veri (`Test-Kennwort-123` vb.).
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
- Okul dosyası yalnız bellekte; kaydetmek = yeni revizyonlu `.schuldatei` indirmek (LOGINEO'ya konur, eskisi silinir).

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
Yayın: `.github/workflows/deploy-pwa.yml` – her `main` push'unda test + publish; Pages'e dağıtım repo değişkeni `PAGES_ENABLED=true` iken (ayarlı). Yayın paketinde veri dosyası (`.schuldatei/.an-lehrkraft/.an-verwaltung/.docx`) çıkarsa iş akışı durur. Base path otomatik olarak repo adı (`/IKO.Zeugnis/`); service worker base'i kendi konumundan hesaplar.

## Durum (20.11.2026)

Tamamlanan: v0.1 iskelet/şifreleme/birleştirme · v0.2 temel veriler + CSV · v0.2.1 katalog düzenleyici · v0.3 değerlendirme ekranı + şifreli taslak · v0.4 görevler + dönüş birleştirme (çakışma kararı) · v0.5 Word çıktısı · v0.5.1 sol panel (Lehrkraft/Verwaltung) · v0.5.2 ana sayfada adım adım açıklama · v0.6 hareketsizlikte otomatik kapanma + onaylı PWA güncellemesi · v0.7 yönü gösteren dosya uzantıları · v0.8 eski uygulamadan katalog/şablon devri için Word doldurucu geliştirmeleri · v0.9 sınıf zeugnis tarihleri, "Zahl" alan türü (Fehlzeiten), Schulformempfehlung · v0.10 öğretmen başına çok sınıflı görev, Rücklauf, koşullu bölümler (Religion katılımı, Deutschförderung), "Datum" alan türü · v0.11 öğretmen başına yarıyıl parolası · v0.12 katalog düzenleyicide Fach açılır listesi · v0.13 sınıf öğretmeni kontrol görevi.

- **Kontrol (v0.13, kullanıcı kararı: karneyi sınıf öğretmeni imzalar, her şeyi kontrol edip düzeltebilmeli)**: `AssignmentFactory.CreateReview(f, sınıf, …)` → sınıf öğretmenine sınıfın tüm öğrencileri + `AllFields` (`r:* g:* t:* s:* n:* d:*`), güncel değerlerle; `IssuedAssignment.IsReview`. Aufträge'de "Kontrolle durch Klassenlehrkraft" kartı (sınıf başına, açık branş görevi sayısı, "Kontrolle läuft"). `GradingContext.AuthorsOf` → değerlendirme ekranında "eingetragen von …" (düzenleyen kişi hariç). `MergeItem.CorrectedFrom` / `MergePreview.Corrections`: başka birinin değerini değiştiren alanlar önizlemede "Korrekturen an Werten anderer" olarak ayrı listelenir. Kontrolden sonra gelen branş düzeltmesi normal çakışma olur.

- **Fach listesi (v0.12, `Shared/SubjectSelect.razor`, `Zeugnis.Export/SubjectOptions`)**: seçenekler = okul dosyasındaki tüm katalogların dersleri + o sınıf düzeyi/eğitim yolunun Word şablonunda olup katalogda henüz olmayan alanlardan çıkarılan dersler ("aus Word-Vorlage"; anahtarın ilk parçası, `SE`→Deutsch, `Teilnahme-X`→X, `Fehlstunden`→Fehlzeiten vb.). "Neues Fach …" serbest yazım; ders yokken otomatik serbest mod, ders gelince listeye döner.

- **Yarıyıl parolası (v0.11, kullanıcı kararı)**: `SchoolFile.TeacherPasswords` (öğretmen Id → parola; sadece şifreli okul dosyasında, görev dosyalarında yok). `AssignmentFactory.Create` istekte parola yoksa `MasterData.TeacherPassword` kullanır (ilk görevde üretilir). `RenewTeacherPassword` yeni parola verir; verilmiş görevler eski parolasını (`IssuedAssignment.Password`) korur. Aufträge'de parola alanı yok; Lehrkräfte'de göster/yenile. Yeni yarıyıl = yeni okul dosyası = yeni parolalar.

- **Çok sınıflı görev (v0.10)**: `AssignmentRequest.ClassNames`; `IssuedAssignment.ClassNames` (+ görüntü `ClassName` = "3a, 4a"; eski dosyalar için `Classes()`), `AssignmentFile.ClassNames|ClassGrades` → her öğrenci kendi sınıf düzeyinin kataloğunu alır. Dosya adı `Auftrag_3a+4a_…`. Sınıf yeniden adlandırma görevlerdeki tüm sınıfları günceller.
- **Rücklauf (`AssignmentStatus`)**: açık görevler için Missing/Overdue/Partial/Complete/Closed ve doldurulan alan sayısı (görünür bölümler, izinli alanlar). Aufträge sayfasında özet kartı + "Stand" sütunu.
- **Koşullu bölümler**: `CatalogSection.VisibleWhen` = `s:Anahtar=değer` / `s:Anahtar!=değer` (`SectionCondition`). Gizli bölüm ekranda görünmez, ilerlemeye sayılmaz, Word'de boş. `CatalogItem.ReportText`: seçeneğin Word metni (`""` = hiçbir şey; null = etiket). Yer tutucular ayrıca `{ErSie}` ve alan değerleri `{n:…}` `{d:…}` `{t:…}` (boşsa `__`/`____`). Word tag'inde `a||b` = ilk dolu değer. Yeni tür `FieldKind.Date` (`d:`, ISO saklanır, Word'de TT.MM.JJJJ). Dönüştürücü: `s:Teilnahme-Religion` (ja/nein, nein → "{Vorname} hat nicht am Religionsunterricht teilgenommen."), tüm Religion bölümleri `!=nein`; `txt_Religion` → `t:Religion||s:Teilnahme-Religion`. `s:Deutschfoerderung` (nein/ja + eski uzun cümle) + `d:Deutschfoerderung-Seit` + `n:Deutschfoerderung-Stunden` (`=ja`). Katalog düzenleyici koşul ve Word metnini yalnız gösterir; düzenleme henüz yok.

- **Dosya adları (v0.7, kullanıcı kararı)**: uzantı alıcıyı gösterir. `Auftrag_3a_Deutsch_Herr_Ali.an-lehrkraft` (Verwaltung → öğretmen), `Rueckgabe_3a_Deutsch_Herr_Ali_20261120-1430.an-verwaltung` (öğretmen → Verwaltung), `Schuldatei_2026-27_HJ1_R3.schuldatei` (R = revizyon). Eski `.zschule/.zauftrag/.zantwort` artık açılmaz; `.gitignore` ve yayın kontrolü ikisini de dışlar.

- **Word çıktısı (`Zeugnis.Export`)**: şablon `SchoolFile.Templates` içinde, sınıf düzeyi + eğitim yolu başına bir `.docx`. Ana metin + üst/alt bilgi doldurulur, biçim korunur, satır sonu → Word satır sonu, dolan alanlar düz metne çevrilir. Tag'ler: `student.name|firstname|lastname|birthdate|header`, `class.name|teacher`, `school.name|address|town`, `term`, `term.schoolyear|halfyear`, `date` ve alan anahtarları. `g:` → not kelimesi (sehr gut…), `s:` → seçeneğin metni, `t:` metin. `Anahtar#değer`: Word onay kutusu işaretlenir, düz metin alanına `●` yazılır. Seçenek metinlerinde yer tutucular (`ReportText`): `{Name} {Vorname} {Nachname} {Klasse} {Schuljahresende}` (ekranda da). Bilinmeyen tag'ler değiştirilmez. Verwaltung → Zeugnisse: yükleme, öğrenci başına `.docx`, sınıf ZIP.
- **Eski uygulamadan devir (v0.8)**: repo dışında tek seferlik dönüştürücü (`C:\Temp\claude\legacy\Converter`, eski `CatalogService` + `Katalog.cs` derlenir). Çıktı: `Belgeler\IKO.Zeugnis-Import\{1,2}. Halbjahr\Kataloge_*HJ.zkatalog` + `Word-Vorlagen\KlasseN[_SE]_*HJ.docx` + `Umwandlungsbericht.txt`. 12 katalog/şablon, tüm eski tag'ler eşlendi (`w_X_id_v` → `r:Key.id#v`, `aw_X_Note` → `g:X`, `txt_X` → `t:X`, Versetzung → `s:Versetzung`). 5 basamaklı eski ölçek. 1. HJ şablonlarında Versetzung/Wiederbeginn satırları kaldırıldı. Kataloglar ve şablonlar repoya girmez. Dönüştürücü kopyası: `Belgeler\IKO.Zeugnis-Import\_Konverter`; `probe 2:3` / `probe 1:4` argümanıyla sahte verili örnek karne üretir (Word COM ile PDF'e çevirip `C:\Temp\claude\legacy\scripts\pdf2png.ps1` ile görsel kontrol edildi).
- **Sınıf ve ek bilgiler (v0.9)**: `SchoolClass.ConferenceDate|ReportDate|ResumptionDate|ResumptionTime` (Verwaltung → Klassen → "Zeugnisangaben je Klasse", `MasterData.SetClassReportDates`). Yeni alan türü `FieldKind.Number` (`n:`), sadece rakam. Sabit anahtarlar (`ReportText`): `n:Fehlstunden`, `n:Fehlstunden-unentschuldigt`, `n:Verspaetungen`, `s:Schulform` (hauptschule|realschule|gymnasium), `s:Schulform-eingeschraenkt` (keine|realschule|gymnasium). Cümle tag'leri: `class.konferenz|ortdatum|wiederbeginn` (+ ham `class.konferenzdatum|zeugnisdatum|wiederbeginndatum`), `student.fehlstunden|verspaetungen` (boşsa 0), `student.schulform.titel|einleitung|auswahl|eingeschraenkt` (katalogda `s:Schulform` yoksa boş). Haftanın günü tarihten (Almanca, kültürden bağımsız).
- **Hareketsizlik (`Shared/IdleGuard.razor`, `zeugnis.idle`)**: 20 dk, 2 dk önce uyarı. Öğretmen: taslak zaten şifreli kayıtlı, görev kapanır. Verwaltung: kaydedilmemiş değişiklik varsa önce yeni revizyon indirilir, sonra kapanır (kaydetme başarısızsa açık kalır). Zaman damgası ile ölçülür (iPad arka plan zamanlayıcıları için).
- **Güncelleme (`Shared/UpdateNotice.razor`, `zeugnis.sw`)**: yeni service worker bekler; banner'da onaylanınca `SKIP_WAITING` + yeniden yükleme. Okul dosyası kaydedilmemişse düğme kapalı.
- **Visual Studio**: `launchSettings.json` içindeki "Visual Studio" profili F5'te `dotnet watch run --launch-profile http` çalıştırır (VS `http` profilinde 5000'e düşüyordu). VS'nin sunucusu çalışırken uygulamayı aynı klasörde `dotnet build` ile derleme; sayfa %96'da takılır (SRI). Kontrol için kaynağı geçici klasöre kopyalayıp orada derle/çalıştır.

## Sıradaki işler (öncelik sırasıyla)

0. Dosya alışverişinde açık kalan: LOGINEO LMS "Aufgabe" ile gerçek deneme. Katalog düzenleyicide `VisibleWhen`/`ReportText` düzenleme alanı.
1. **iPad gerçek cihaz testi** canlı adreste (Safari, Dosyalar uygulaması, LOGINEO indirme/yükleme, ana ekrana ekleme, çevrimdışı açılış, güncelleme banner'ı).
2. Word: Verwaltung'da şablonda kullanılabilecek tag'leri listeleyen yardım ekranı; ZIP öncesi eksik alan kontrolü. Öğretmen tarafı hareketsizlik kapanışı tarayıcıda gerçek `.an-lehrkraft` ile henüz denenmedi.
3. Yeni yarıyıl: önceki okul dosyasından temel verileri ve katalogları kopyalayarak yeni dosya oluşturma.
4. İsteğe bağlı: `.xlsx` doğrudan içe aktarma, sınıf görünümü (tüm öğrenciler × bir kriter tablosu).
