# Yalın Markdown

Windows için sade ve hafif bir Markdown editörü.

## Özellikler

- Yan yana Markdown editörü ve canlı önizleme (mevcut yerleşim korunur)
- Birden fazla dosyayla çalışmak için sekmeler
- `.md`, `.markdown` ve `.txt` dosyalarını açma ve sürükleyip bırakarak ekleme
- Uygulama yeniden açıldığında sekmeleri ve kaydedilmemiş içerikleri geri yükleme
- İngilizce, Türkçe, Almanca, Fransızca, İspanyolca, İtalyanca, Çince, Japonca, Korece ve Rusça arayüz
- İlk açılışta İngilizce; sonraki açılışlarda son seçilen dili hatırlama
- Yeni dosya, aç, kaydet ve farklı kaydet
- Seçili metni kalın yapma
- Yazı tipi değiştirme
- Koyu ve açık tema
- Kelime ve karakter sayacı
- Agent çalışma alanı analizi (`AGENTS.md`, `SOUL.md`, `SKILL.md` ve diğer talimat dosyaları)
- Belge ve bölüm bazında yaklaşık token maliyeti
- Olası gizli bilgi, tehlikeli komut, çelişki, tekrar ve bozuk yol kontrolleri
- Yaklaşık birleşik agent bağlamı önizlemesi
- Görev metnine göre skill tetiklenme simülasyonu
- Harici NuGet paketi olmadan hafif WPF uygulaması

## Agent Araçları

1. Araç çubuğundaki `Agent Tools` düğmesini açın.
2. `Choose Workspace` ile agent dosyalarının bulunduğu proje klasörünü seçin.
3. `Analyze Active`, açık belgeyi; `Analyze Workspace`, çalışma alanındaki Markdown dosyalarını analiz eder.
4. `Findings` sekmesindeki bir sonuca çift tıklayarak ilgili dosya ve satıra gidin.
5. `Sections` sekmesinde en fazla token kullanan bölümleri inceleyin.
6. `Merged Context` sekmesinde etkin dosya için yaklaşık talimat birleşimini görün.
7. `Skill Match` sekmesine örnek görev yazıp olası `SKILL.md` eşleşmelerini test edin.

Analizler hızlı ve sezgiseldir. Gerçek bağlam yükleme ve skill tetikleme davranışı kullanılan agent'a göre değişebilir.

## Kısayollar

- `Ctrl+N`: Yeni belge
- `Ctrl+O`: Dosya aç
- `Ctrl+S`: Kaydet
- `Ctrl+Shift+S`: Farklı kaydet
- `Ctrl+B`: Kalın

## Derleme

```powershell
dotnet build -c Release
```

Çalıştırılabilir dosya: `bin/Release/net10.0-windows/YalinMarkdown.exe`

## Büyük dosyalar ve veri güvenliği

Bu sürümde canlı önizleme, büyük belgelerde en fazla ilk 80.000 karakteri / 1.200 satırı gösterir; çok uzun satırlar da önizlemede kısaltılır. **Belgenin kaynak metni kısaltılmaz** ve tamamı kaydedilir. Önizlemedeki bilgilendirme, görünümün kısmi olduğunu belirtir.

- Editörün WPF `TextBox` sınırları nedeniyle **8 MiB üzerindeki dosyalar kontrollü olarak reddedilir**. Bu sınıra kadar da donanım ve belge yapısına bağlı gecikmeler yaşanabilir; sınırsız büyüklükte dosya desteği iddia edilmez.
- Dosya açma asenkrondur. Kelime/karakter/yaklaşık token göstergeleri **her düzenlemede anında** artımlı güncellenir; tam metin taraması olağan tek değişikliklerde tekrarlanmaz.
- Önizlemede normal satır ekleme/silme ve içerik düzenleme yalnızca değişen blokları yeniler. Fenced-code içi tek satır değişiklikleri sadece ilgili kod bloğunu günceller. Karmaşık Markdown yapısal değişikliklerinde önizleme, arayüzü uzun süre bloke etmemek için dispatcher üzerinde 40 satırlık küçük dilimler hâlinde yeniden hazırlanır; bu durumlarda güncelleme kısa süre gecikebilir.
- Mevcut bir dosyaya kaydetme, aynı klasörde geçici dosya üzerinden `File.Replace` kullanır. Dosya sistemi atomik değiştirmeyi desteklemiyorsa kaydetme hata verir ve orijinal dosyanın üzerine doğrudan yazılmaz.
- UTF-8, UTF-16 ve UTF-32 metin dosyaları desteklenir; varsa BOM korunur. Tanınmayan ve bozuk kodlamalı dosyalar, veriyi sessizce dönüştürmemek için açılmaz.
- Oturum kurtarma dosyası yalnızca kullanıcının yerel uygulama verisi klasöründe yazılır. Kaydedilmemiş belgeler yerel oturum yedeğinde tutulur, temiz/kaydedilmiş belgeler yeniden diskten okunur. Oturumda dosya yolları ve kaydedilmemiş belge içerikleri yer alabilir; **oturum JSON'unu paylaşmayın**.

## Geliştirme ve test

Windows 11 ve .NET 10 SDK ile:

```powershell
python tools/audit-source.py
dotnet run --project tests/YalinMarkdown.SmokeTests/YalinMarkdown.SmokeTests.csproj -c Release
dotnet build MarkdownEditor.csproj -c Release
```

`tools/audit-source.py` kaynak ağacında yaygın kişisel yol, e-posta ve anahtar desenlerini denetler. Kapsamı sınırlı bir korumadır; yayımlama öncesinde ayrıca manuel inceleme gerekir. Derlenmiş EXE, PDB, log, oturum, yedek, makineye özgü konfigürasyonlar ve ZIP arşivleri depoya eklenmez.