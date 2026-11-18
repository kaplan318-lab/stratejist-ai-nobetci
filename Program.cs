// STRATEJIST AI - Bulut Nöbetçi
// Bilgisayar kapalıyken bile çalışır. GitHub Actions bu programı her 5 dakikada çalıştırır:
//  1) İzlenen konular için "son 1 saat" haberlerini kontrol eder; önemli gelişme bulursa Telegram'a mesaj yollar.
//  2) Çalışma süresi boyunca (yaklaşık 4 dakika) Telegram'dan gelen komutları dinler ve yanıtlar:
//       aday/rakip adı yazın -> haber araması     /rakip ekle X, /rakip sil X, /rakipler, /aday X, /yardim
// Gizli bilgiler (bot anahtarı, sohbet no, konu listesi) depoda değil GitHub "Secrets" içinde durur.
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string token = (Environment.GetEnvironmentVariable("TELEGRAM_TOKEN") ?? "").Trim();
string chat = (Environment.GetEnvironmentVariable("TELEGRAM_CHAT_ID") ?? "").Trim();
string konularHam = Environment.GetEnvironmentVariable("KONULAR") ?? "";
string kanalEnv = (Environment.GetEnvironmentVariable("KANAL_ID") ?? "").Trim();
bool hepsi = (Environment.GetEnvironmentVariable("HEPSINI_BILDIR") ?? "").Trim() == "true";
string durumDosyasi = Environment.GetEnvironmentVariable("DURUM_DOSYASI") ?? "durum.json";
int komutSuresi = int.TryParse(Environment.GetEnvironmentVariable("KOMUT_SURESI"), out var ks) ? ks : 0; // saniye
bool kuruCalisma = token.Length == 0 || chat.Length == 0; // bilgi eksikse mesaj göndermez, sadece ekrana yazar

// ---- Durum (önbellekte saklanır) ----
var durum = new Durum();
bool ilkCalisma = !File.Exists(durumDosyasi);
if (!ilkCalisma) try { durum = JsonSerializer.Deserialize<Durum>(File.ReadAllText(durumDosyasi)) ?? new Durum(); } catch { }
if (kanalEnv.Length > 0 && durum.KanalId.Length == 0) durum.KanalId = kanalEnv;
void Kaydet()
{
    durum.Gorulen = durum.Gorulen.TakeLast(6000).ToHashSet();
    File.WriteAllText(durumDosyasi, JsonSerializer.Serialize(durum));
}

string Katla(string s) => (s ?? "").Replace("İ", "i").Replace("I", "ı").ToLowerInvariant().Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ı', 'i').Replace('ö', 'o').Replace('ç', 'c').Replace("i̇", "i");
string Kimlik(string baslik) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(new string(Katla(baslik).Where(char.IsLetterOrDigit).ToArray()))))[..16];
string AramaAdi(string k) { var p = k.Split(' ', StringSplitOptions.RemoveEmptyEntries); return p.Length > 2 ? p[0] + " " + p[^1] : k.Trim(); }
string Kok(string konu) { var k = Katla(konu.Split(' ', StringSplitOptions.RemoveEmptyEntries).Last()); return k.Length > 5 ? k[..5] : k; }

List<string> EtkinKonular()
{
    var l = Regex.Split(konularHam, @"[\r\n;,|]+").Select(k => k.Trim()).Where(k => k.Length >= 3)
        .Concat(durum.Konular)
        .Where(k => !durum.Silinen.Any(s => string.Equals(s, k, StringComparison.OrdinalIgnoreCase)))
        .Distinct(StringComparer.OrdinalIgnoreCase).Take(100).ToList();
    return l;
}

var onemli = new Regex(@"son dakika|istifa|gözaltı|tutuklan|soruşturma|yolsuzluk|skandal|ihraç|suçlama|iddia|ifşa|hakaret|dava|ceza|kaza|hayatını kaybet|öldü|saldırı|operasyon|kriz|çöküş|zam geldi|yasak|kapatıl|el konul|mahkeme|tepki|erken seçim|anket", RegexOptions.IgnoreCase);
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(40) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (compatible; StratejistAI-Nobetci)");

async Task<List<(string baslik, string kaynakAdi, string link, DateTime tarih, string ozet)>> Cek(string url)
{
    var l = new List<(string, string, string, DateTime, string)>();
    try
    {
        var xml = await http.GetStringAsync(url);
        foreach (var it in XDocument.Parse(xml).Descendants("item"))
        {
            string b = WebUtility.HtmlDecode(Regex.Replace((string?)it.Element("title") ?? "", "<.*?>", "")).Trim();
            string kaynak = "";
            int tire = b.LastIndexOf(" - ", StringComparison.Ordinal);
            if (tire > 0) { kaynak = b[(tire + 3)..].Trim(); b = b[..tire].Trim(); }
            DateTime.TryParse((string?)it.Element("pubDate"), out var t);
            // Özet: haberin açıklaması (Bing gerçek özet verir; Google başlığı tekrarlar, o zaman boş bırakılır)
            string oz = WebUtility.HtmlDecode(Regex.Replace((string?)it.Element("description") ?? "", "<.*?>", " ")).Trim();
            oz = Regex.Replace(oz, @"\s+", " ");
            if (oz.StartsWith(b, StringComparison.OrdinalIgnoreCase)) oz = oz[b.Length..].Trim(' ', '-', '—', '·');
            if (kaynak.Length > 0 && oz.EndsWith(kaynak, StringComparison.OrdinalIgnoreCase)) oz = oz[..^kaynak.Length].Trim();
            if (oz.Length < 25 || Katla(oz) == Katla(b)) oz = "";
            string link = ((string?)it.Element("link") ?? "").Trim();
            // Bing bağlantısı kendi yönlendirme adresini sarar (çok uzun); gerçek haber adresi "url=" parametresinde durur
            if (link.Contains("apiclick.aspx"))
            {
                var qs = System.Web.HttpUtility.ParseQueryString(new Uri(link).Query);
                if (qs["url"] is { Length: > 0 } gercek) link = gercek;
            }
            if (kaynak.Length == 0) kaynak = it.Elements().FirstOrDefault(x => x.Name.LocalName == "Source")?.Value.Trim() ?? "";
            if (kaynak.Length == 0 && Uri.TryCreate(link, UriKind.Absolute, out var uri)) kaynak = uri.Host.Replace("www.", "");
            if (b.Length >= 10) l.Add((b, kaynak, link, t == default ? DateTime.UtcNow : t.ToUniversalTime(), oz.Length > 300 ? oz[..300].TrimEnd() + "…" : oz));
        }
    }
    catch (Exception ex) { Console.WriteLine("  (arama hatası: " + ex.Message + ")"); }
    return l;
}

// Telegram HTML'inde yalnızca & < > özel; Türkçe harfler olduğu gibi kalır. Bağlantı özniteliği için tırnak da kaçırılır.
string E(string s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

// Haber satırı (HTML): kalın başlık, özet, kaynak/saat ve KISA bağlantı ("Habere git" — uzun adres görünmez)
string HaberSatiri(string baslik, string ozet, string kaynakAdi, DateTime tarih, string link, bool tarihli = false) =>
    $"• <b>{E(baslik)}</b>\n" + (ozet.Length > 0 ? $"  {E(ozet)}\n" : "") +
    $"  {E(kaynakAdi)} — {(tarihli ? tarih.ToLocalTime().ToString("dd.MM HH:mm") : tarih.ToLocalTime().ToString("HH:mm"))}" + (link.StartsWith("http") ? $" · <a href=\"{E(link)}\">Habere git ↗</a>" : "") + "\n";

// hedef: boşsa sahibin özel sohbeti; doluysa (ör. yayın kanalı) oraya gider. koru: iletme/kaydetme kapalı (kanal aboneleri mesajı başkasına aktaramaz)
async Task Gonder(string metin, bool html = false, string? hedef = null, bool koru = false)
{
    if (!html && metin.Length > 3900) metin = metin[..3900] + "…";
    if (kuruCalisma) { Console.WriteLine($"[KURU ÇALIŞMA - gönderilmedi → {(hedef ?? "sahip")}]\n" + metin); return; }
    var alanlar = new Dictionary<string, string> { ["chat_id"] = hedef ?? chat, ["text"] = metin, ["disable_web_page_preview"] = "true" };
    if (html) alanlar["parse_mode"] = "HTML";
    if (koru) alanlar["protect_content"] = "true";
    using var icerik = new FormUrlEncodedContent(alanlar);
    var r = await http.PostAsync($"https://api.telegram.org/bot{token}/sendMessage", icerik);
    Console.WriteLine("Telegram: " + (int)r.StatusCode);
    await Task.Delay(1200);
}

// ---------------- 1) Haber nabzı (çalışma süresince her 5 dakikada bir çağrılır) ----------------
async Task HaberNabzi()
{
var mesajlar = new List<string>();
// Çok konulu listelerde her nabızda 25 konu (sırayla döner; her konu en geç ~20 dakikada bir kontrol edilir)
var tumKonular = EtkinKonular();
int bas = tumKonular.Count == 0 ? 0 : durum.NabizSirasi % tumKonular.Count;
var nabizDilimi = tumKonular.Skip(bas).Concat(tumKonular.Take(bas)).Take(25).ToList();
durum.NabizSirasi = tumKonular.Count == 0 ? 0 : (bas + 25) % tumKonular.Count;
foreach (var konu in nabizDilimi)
{
    string ad = AramaAdi(konu), kok = Kok(konu);
    bool yeniKonu = durum.Taranan.Add(Katla(konu)); // bu konu ilk kez görülüyorsa mevcut haberleri sessizce hafızaya al (mesaj yağmurunu önler)
    var adaylar = await Cek($"https://news.google.com/rss/search?q={Uri.EscapeDataString(ad + " when:1h")}&hl=tr&gl=TR&ceid=TR:tr");
    adaylar.AddRange(await Cek($"https://www.bing.com/news/search?q={Uri.EscapeDataString(ad)}&format=rss&setlang=tr&cc=TR"));
    var yeni = adaylar.Where(a => Katla(a.baslik).Contains(kok) && a.tarih > DateTime.UtcNow.AddHours(-3) && RakipAnalizSistemi.YapayZeka.KonuSuzgeci.Uygun(a.baslik))
                      .GroupBy(a => Kimlik(a.baslik)).Select(g => (id: g.Key, h: g.First()))
                      .Where(x => durum.Gorulen.Add(x.id)).ToList();
    Console.WriteLine($"{konu}: {yeni.Count} yeni haber");
    if (!ilkCalisma && !yeniKonu) durum.GunlukHaber += yeni.Count;
    if (yeni.Count == 0 || ilkCalisma || yeniKonu) continue; // ilk çalışmada yalnızca hafızaya al (mesaj yağmurunu önler)

    var onemliler = yeni.Where(x => onemli.IsMatch(x.h.baslik)).ToList();
    bool hepsiEtkin = durum.HepsiAyar ?? hepsi;
    if (DateTime.UtcNow < durum.SusturBitis) continue; // susturulmuş: haberler hafızaya alındı, mesaj gönderilmez
    var secilen = yeni; string neden;
    if (hepsiEtkin) neden = "yeni haber";
    else if (onemliler.Count > 0) { secilen = onemliler; neden = "önemli gelişme"; }
    else if (yeni.Count >= 4) neden = $"olağan dışı yoğunluk: son saatte {yeni.Count} haber";
    else continue;
    var sb = new StringBuilder($"⚡ <b>OLASI GELİŞME — {E(konu)}</b> ({E(neden)})\n\n");
    int gosterilen = 0;
    foreach (var x in secilen.OrderByDescending(x => x.h.tarih).Take(5))
    {
        string satir = HaberSatiri(x.h.baslik, x.h.ozet, x.h.kaynakAdi, x.h.tarih, x.h.link) + "\n";
        if (sb.Length + satir.Length > 3600) break;
        sb.Append(satir); gosterilen++;
    }
    if (secilen.Count > gosterilen) sb.AppendLine($"(+{secilen.Count - gosterilen} haber daha)");
    mesajlar.Add(sb.ToString());
}
Kaydet();
if (ilkCalisma) { Console.WriteLine("İlk çalışma: mevcut haberler hafızaya alındı; bundan sonra yalnızca yeni gelişmeler bildirilecek."); ilkCalisma = false; }
foreach (var m in mesajlar.Take(3))
{
    await Gonder(m, true);                                                      // sahibe
    if (durum.KanalId.Length > 0) await Gonder(m, true, durum.KanalId, true);   // yayın kanalına (aboneler yalnızca okur; mesaj iletilemez/kaydedilemez)
}
}

// Günlük sağlık mesajı: her sabah 07:00'den sonra bir kez "çalışıyorum" + son 24 saatin haber sayısı gelir.
// Bu mesaj hiç gelmezse bulut nöbetçi durmuş demektir (sessiz arıza uyarısı).
async Task GunlukOzet()
{
    var tr = DateTime.UtcNow.AddHours(3);
    if (tr.Hour < 7 || durum.SonOzet.Date == tr.Date) return;
    durum.SonOzet = tr; int n = durum.GunlukHaber; durum.GunlukHaber = 0; Kaydet();
    await Gonder($"☀️ <b>Günaydın — nöbetçi çalışıyor</b>\nSon 24 saatte {n} yeni haber izlendi.\nİzlenenler: {E(string.Join(", ", EtkinKonular()))}\nBildirim: {(DateTime.UtcNow < durum.SusturBitis ? "🔕 susturulmuş" : "🔔 açık")}", true);
}

await HaberNabzi();

// ---------------- 2) Telegram komutları ----------------
const string Yardim = "🤖 STRATEJİST AI — Telegram komutları\n\n" +
    "• Bir aday/rakip adı yazın (ör: Mansur Yavaş) → son 3 günün haberleri\n" +
    "• /ara Ad 30 → son 30 günün haberleri\n" +
    "• /rakip ekle Ad → izlemeye al (gelişmelerde otomatik mesaj)\n" +
    "• /rakip sil Ad → izlemeden çıkar\n" +
    "• /rakipler → izlenenler\n" +
    "• /aday Ad → bizim aday adını kaydet\n" +
    "• /durum → bildirim durumu\n" +
    "• /kanal → yayın kanalı (aboneler yalnızca okur): /kanal <numara|@ad>, /kanal kapat\n" +
    "• /sustur [saat] → bildirimleri sustur (varsayılan 8 saat), /ac → aç\n" +
    "• /hepsi ac|kapat → her yeni haber / yalnızca önemli\n" +
    "• /yardim → bu liste\n\nYanıt birkaç saniye ile 5 dakika arasında gelir.";

async Task Ara(string ad, int gun)
{
    string kok = Kok(ad);
    string sorgu = gun <= 30 ? $"{AramaAdi(ad)} when:{gun}d" : $"{AramaAdi(ad)} after:{DateTime.UtcNow.AddDays(-gun):yyyy-MM-dd}";
    var l = await Cek($"https://www.bing.com/news/search?q={Uri.EscapeDataString(AramaAdi(ad))}&format=rss&setlang=tr&cc=TR"); // Bing: gerçek özet verir (son günler)
    l.AddRange(await Cek($"https://news.google.com/rss/search?q={Uri.EscapeDataString(sorgu)}&hl=tr&gl=TR&ceid=TR:tr"));
    var secilen = l.Where(a => Katla(a.baslik).Contains(kok) && RakipAnalizSistemi.YapayZeka.KonuSuzgeci.Uygun(a.baslik)).GroupBy(a => Kimlik(a.baslik)).Select(g => g.First()).OrderByDescending(a => a.tarih).ToList();
    if (secilen.Count == 0) { await Gonder($"🔎 {ad}: son {gun} günde haber bulunamadı."); return; }
    // (Bing sonuçları önce geldiği için aynı haber tekrarında özetli sürüm seçilir)
    var sb = new StringBuilder($"🔎 <b>{E(ad)}</b> — son {gun} gün: {secilen.Count} haber\n\n");
    int g = 0;
    foreach (var a in secilen.Take(8))
    {
        string satir = HaberSatiri(a.baslik, a.ozet, a.kaynakAdi, a.tarih, a.link, true) + "\n";
        if (sb.Length + satir.Length > 3600) break;
        sb.Append(satir); g++;
    }
    if (secilen.Count > g) sb.AppendLine($"(+{secilen.Count - g} haber daha)");
    if (!EtkinKonular().Any(k => Kok(k) == kok)) sb.AppendLine($"İzlemeye almak için: <code>/rakip ekle {E(ad)}</code>");
    await Gonder(sb.ToString(), true);
}

async Task Isle(string metin)
{
    metin = metin.Trim();
    if (metin.Length == 0) return;
    string cmd = "ara", arg = metin;
    if (metin.StartsWith('/'))
    {
        var p = metin.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        cmd = Regex.Replace(p[0].TrimStart('/').ToLowerInvariant(), "@.*$", "");
        arg = p.Length > 1 ? p[1].Trim() : "";
    }
    switch (cmd)
    {
        case "start": case "yardim": case "yardım": case "help": await Gonder(Yardim); return;
        case "durum":
        {
            bool susturuldu = DateTime.UtcNow < durum.SusturBitis;
            await Gonder($"📊 Durum\n• İzlenen konu: {EtkinKonular().Count}\n• Bizim aday: {(durum.Aday.Length > 0 ? durum.Aday : "yok")}\n• Bildirim: {(susturuldu ? $"🔕 susturuldu ({durum.SusturBitis.ToLocalTime():dd.MM HH:mm}'e kadar)" : "🔔 açık")}\n• Kapsam: {((durum.HepsiAyar ?? hepsi) ? "her yeni haber" : "yalnızca önemli gelişme")}\n• Hafızadaki haber: {durum.Gorulen.Count:N0}");
            return;
        }
        case "sustur":
        {
            int saat = int.TryParse(arg, out var sh) && sh is >= 1 and <= 720 ? sh : 8;
            durum.SusturBitis = DateTime.UtcNow.AddHours(saat); Kaydet();
            await Gonder($"🔕 Bildirimler {saat} saat susturuldu ({durum.SusturBitis.ToLocalTime():dd.MM HH:mm}'e kadar). Açmak için /ac");
            return;
        }
        case "ac": case "sesac":
            durum.SusturBitis = DateTime.MinValue; Kaydet(); await Gonder("🔔 Bildirimler açık."); return;
        case "hepsi":
        {
            if (arg.Trim().Length == 0) { await Gonder("Kullanım: /hepsi ac  (her yeni haber)  veya  /hepsi kapat  (yalnızca önemli gelişme)"); return; }
            bool ac = arg.Trim().ToLowerInvariant() is "ac" or "aç" or "açık" or "evet" or "on";
            durum.HepsiAyar = ac; Kaydet();
            await Gonder(ac ? "⚡ Artık her yeni haber bildirilecek." : "✅ Yalnızca önemli gelişmeler bildirilecek."); return;
        }
        case "kanal":
        {
            // YAYIN KANALI: gelişme bildirimleri bu kanala da gönderilir; kanal aboneleri yalnızca okuyabilir, bota komut veremez.
            var p = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) { await Gonder(durum.KanalId.Length > 0 ? $"📣 Yayın kanalı: {durum.KanalId}\nKapatmak için: /kanal kapat" : "Yayın kanalı yok. Kanalı oluşturup botu yönetici (mesaj gönderme yetkili) yapın; bot size kanal numarasını bildirecek."); return; }
            if (p[0].ToLowerInvariant() is "kapat" or "sil" or "off") { durum.KanalId = ""; Kaydet(); await Gonder("📴 Yayın kanalı kapatıldı (bot kanalda kalır ama yazmaz)."); return; }
            if (p[0].ToLowerInvariant() is "ayril" or "ayrıl")
            {
                string hedefKanal = p.Length > 1 ? p[1] : durum.KanalId;
                if (hedefKanal.Length == 0) { await Gonder("Kullanım: /kanal ayril <kanal numarası>"); return; }
                if (hedefKanal == durum.KanalId) { durum.KanalId = ""; Kaydet(); }
                if (long.TryParse(hedefKanal, out var ln)) await Api("leaveChat", new { chat_id = ln }); else await Api("leaveChat", new { chat_id = hedefKanal });
                await Gonder($"🚪 Kanaldan ayrıldım: {hedefKanal}"); return;
            }
            string yeniKanal = p[0];
            if (!(yeniKanal.StartsWith("@") || long.TryParse(yeniKanal, out _))) { await Gonder("Kanal numarası (-100…) veya @kullaniciadi yazın."); return; }
            durum.KanalId = yeniKanal; Kaydet();
            await Gonder($"✅ Yayın kanalı ayarlandı: {yeniKanal}\nBundan sonraki gelişme bildirimleri kanala da gidecek (abonelerde iletme/kaydetme kapalı).");
            await Gonder("📣 <b>STRATEJİST AI yayın kanalı</b>\nBu kanalda yalnızca otomatik gelişme bildirimleri paylaşılır.", true, yeniKanal, true);
            return;
        }
        case "rakipler": { var k = EtkinKonular(); await Gonder(k.Count == 0 ? "İzlenen konu yok. /rakip ekle Ad" : "👁 İzlenenler:\n• " + string.Join("\n• ", k) + (durum.Aday.Length > 0 ? $"\n\n🎯 Bizim aday: {durum.Aday}" : "")); return; }
        case "aday":
            if (arg.Length == 0) { await Gonder(durum.Aday.Length > 0 ? $"🎯 Bizim aday: {durum.Aday}" : "Aday kayıtlı değil. /aday Ad"); return; }
            durum.Aday = arg; Kaydet(); await Gonder($"🎯 Bizim aday kaydedildi: {arg}"); return;
        case "ekle": arg = "ekle " + arg; goto case "rakip";
        case "sil": arg = "sil " + arg; goto case "rakip";
        case "rakip":
        {
            var p = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 2) { await Gonder("Kullanım: /rakip ekle Ad  veya  /rakip sil Ad"); return; }
            string ad = p[1].Trim();
            if (p[0].ToLowerInvariant() is "ekle" or "add")
            {
                durum.Silinen.RemoveAll(s => string.Equals(s, ad, StringComparison.OrdinalIgnoreCase));
                if (!durum.Konular.Any(k => string.Equals(k, ad, StringComparison.OrdinalIgnoreCase))) durum.Konular.Add(ad);
                Kaydet(); await Gonder($"✅ İzlemeye alındı: {ad}\nÖnemli gelişmelerde otomatik mesaj gelecek.");
            }
            else
            {
                durum.Konular.RemoveAll(k => string.Equals(k, ad, StringComparison.OrdinalIgnoreCase));
                if (!durum.Silinen.Any(s => string.Equals(s, ad, StringComparison.OrdinalIgnoreCase))) durum.Silinen.Add(ad);
                Kaydet(); await Gonder($"🗑 İzlemeden çıkarıldı: {ad}");
            }
            return;
        }
        default:
        {
            // /ara Ad [gün]  veya düz metin
            if (cmd != "ara") arg = metin.TrimStart('/'); // bilinmeyen komut: metni ad say
            var parcalar = arg.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            int gun = 3;
            if (parcalar.Count > 1 && int.TryParse(parcalar[^1], out var g) && g is >= 1 and <= 3650) { gun = g; parcalar.RemoveAt(parcalar.Count - 1); }
            string ad = string.Join(' ', parcalar);
            if (ad.Length < 3) { await Gonder(Yardim); return; }
            await Ara(ad, gun);
            return;
        }
    }
}

if (kuruCalisma && Environment.GetEnvironmentVariable("TEST_KOMUT") is { Length: > 0 } tk)
    foreach (var c in tk.Split('|')) { Console.WriteLine("\n>>> " + c); await Isle(c); }

// ---------------- Güvenlik: yalnızca sahibi komut verebilir, yabancılar sessizce yok sayılır ----------------
async Task Api(string yontem, object govde)
{
    if (kuruCalisma) { Console.WriteLine($"[KURU] {yontem} {JsonSerializer.Serialize(govde)}"); return; }
    try { using var c = new StringContent(JsonSerializer.Serialize(govde), Encoding.UTF8, "application/json"); await http.PostAsync($"https://api.telegram.org/bot{token}/{yontem}", c); }
    catch (Exception ex) { Console.WriteLine($"{yontem} hatası: {ex.Message}"); }
}

async Task Guncelle(JsonElement u)
{
    if (u.TryGetProperty("update_id", out var uid) && uid.TryGetInt64(out var idv)) durum.Offset = idv + 1;

    // Bot bir GRUBA eklenirse hemen çıkar (grupta herkes yazabilir). Bir KANALA eklenirse sahibine sorar: onaylamadığı kanala hiçbir şey yazılmaz.
    if (u.TryGetProperty("my_chat_member", out var mcm) && mcm.TryGetProperty("chat", out var mc))
    {
        string tip = mc.GetProperty("type").GetString() ?? "";
        string kid = mc.GetProperty("id").GetRawText();
        if (tip == "private") return;
        if (tip == "channel")
        {
            string durumu = mcm.TryGetProperty("new_chat_member", out var ncm) && ncm.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
            if (durumu is "administrator" && kid != durum.KanalId && !durum.SorulanKanallar.Contains(kid))
            {
                durum.SorulanKanallar.Add(kid); Kaydet();
                string ad = mc.TryGetProperty("title", out var ti) ? ti.GetString() ?? "" : "";
                await Gonder($"📣 <b>{E(ad)}</b> kanalına yönetici olarak eklendim.\nBu kanalı YAYIN KANALI yapmak için (aboneler yalnızca okur, kimse yazamaz/komut veremez):\n<code>/kanal {E(kid)}</code>\nİstemiyorsanız: <code>/kanal ayril {E(kid)}</code>", true);
            }
            else if (durumu is "left" or "kicked" && kid == durum.KanalId) { durum.KanalId = ""; Kaydet(); await Gonder("⚠️ Yayın kanalından çıkarıldım; yayın kapatıldı."); }
            return;
        }
        Console.WriteLine($"Yabancı sohbete eklenme ({tip}): ayrılıyor"); await Api("leaveChat", new { chat_id = mc.GetProperty("id").GetInt64() });
        return;
    }
    JsonElement m;
    if (u.TryGetProperty("channel_post", out _) || u.TryGetProperty("edited_channel_post", out _)) return;   // kanal gönderileri bot için komut değildir; yok sayılır
    if (!(u.TryGetProperty("message", out m) || u.TryGetProperty("edited_message", out m))) return;

    var sohbet = m.GetProperty("chat");
    string sohbetTipi = sohbet.GetProperty("type").GetString() ?? "";
    string sohbetNo = sohbet.GetProperty("id").GetRawText();
    if (sohbetTipi != "private") { await Api("leaveChat", new { chat_id = sohbet.GetProperty("id").GetInt64() }); Console.WriteLine($"Özel olmayan sohbet ({sohbetTipi}) reddedildi"); return; }

    // Gönderen: botlar ve sahibi dışındaki herkes yok sayılır (cevap bile verilmez; bot gizli kalır)
    bool gonderenSahip = m.TryGetProperty("from", out var from)
        && from.TryGetProperty("id", out var fid) && fid.GetRawText() == chat
        && !(from.TryGetProperty("is_bot", out var ib) && ib.GetBoolean());
    if (sohbetNo != chat || !gonderenSahip) { Console.WriteLine("Yetkisiz mesaj yok sayıldı"); return; }

    if (m.TryGetProperty("text", out var tx)) { Console.WriteLine("Komut: " + tx.GetString()); await Isle(tx.GetString() ?? ""); }
}

if (kuruCalisma && Environment.GetEnvironmentVariable("TEST_GUNCELLEMELER") is { Length: > 0 } tg)
{
    chat = "111"; // test sahibi
    using var tdoc = JsonDocument.Parse(tg);
    foreach (var u in tdoc.RootElement.EnumerateArray()) { Console.WriteLine("\n--- güncelleme: " + u.ToString()[..Math.Min(110, u.ToString().Length)]); await Guncelle(u); }
}

if (!kuruCalisma && komutSuresi > 0)
{
    // Komut menüsü yalnızca sahibine görünsün; herkese açık menü boş olsun (bir kez yapılır)
    if (!durum.MenuAyarli || !durum.KanalMenu)
    {
        await Api("setMyCommands", new { commands = Array.Empty<object>() });
        await Api("setMyCommands", new
        {
            scope = new { type = "chat", chat_id = long.Parse(chat) },
            commands = new[]
            {
                new { command = "yardim", description = "Komut listesi" }, new { command = "rakipler", description = "Izlenenler" },
                new { command = "ara", description = "Haber ara: /ara Ad 30" }, new { command = "aday", description = "Bizim aday" }, new { command = "kanal", description = "Yayin kanali" }
            }
        });
        await Api("setMyDescription", new { description = "Ozel bot: yalnizca sahibi kullanabilir." });
        durum.MenuAyarli = true; durum.KanalMenu = true; Kaydet();
    }

    var bitis = DateTime.UtcNow.AddSeconds(komutSuresi);
    var sonNabiz = DateTime.UtcNow;
    while (DateTime.UtcNow < bitis)
    {
        try
        {
            if (DateTime.UtcNow - sonNabiz >= TimeSpan.FromMinutes(5)) { await HaberNabzi(); sonNabiz = DateTime.UtcNow; }
            await GunlukOzet();
            string url = $"https://api.telegram.org/bot{token}/getUpdates?timeout=0&allowed_updates=%5B%22message%22%2C%22my_chat_member%22%2C%22channel_post%22%5D" + (durum.Offset > 0 ? $"&offset={durum.Offset}" : "");
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
            foreach (var u in doc.RootElement.GetProperty("result").EnumerateArray()) await Guncelle(u);
            Kaydet();
        }
        catch (Exception ex) { Console.WriteLine("Komut döngüsü hatası: " + ex.Message); }
        await Task.Delay(TimeSpan.FromSeconds(15));
    }
}
class Durum
{
    public HashSet<string> Gorulen { get; set; } = new();
    public List<string> Konular { get; set; } = new();   // Telegram'dan eklenenler
    public List<string> Silinen { get; set; } = new();   // Telegram'dan çıkarılanlar (Secrets'taki listede olsa bile)
    public string Aday { get; set; } = "";
    public HashSet<string> Taranan { get; set; } = new();   // daha önce görülmüş konular
    public long Offset { get; set; }
    public bool MenuAyarli { get; set; }
    public bool KanalMenu { get; set; }
    public int NabizSirasi { get; set; }
    public string KanalId { get; set; } = "";                         // yayın kanalı (boşsa kapalı)
    public HashSet<string> SorulanKanallar { get; set; } = new();     // sahibine onay sorulmuş kanallar
    public DateTime SusturBitis { get; set; }      // /sustur: bu zamana kadar gelişme mesajı gönderilmez (haberler yine hafızaya alınır)
    public bool? HepsiAyar { get; set; }           // /hepsi ac|kapat: Secrets'taki değeri geçersiz kılar
    public DateTime SonOzet { get; set; }          // günlük sağlık mesajının son gönderildiği (Türkiye saati) zaman
    public int GunlukHaber { get; set; }           // son özetten bu yana izlenen yeni haber sayısı
}
