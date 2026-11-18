using System.Text.RegularExpressions;

namespace RakipAnalizSistemi.YapayZeka
{
    /// <summary>
    /// Siyasi analiz ve bildirimler için alakasız içerikleri (spor, magazin, hava durumu, yaşam, şans oyunları) eler.
    /// Başlık bunlardan birini içeriyorsa ve açıkça siyasi/kurumsal bir ifade (cumhurbaşkanı, parti, bakan, meclis vb.) içermiyorsa atılır.
    /// NOT: Bu dosyanın bir kopyası BulutNobetci klasöründe de bulunur; değişiklikleri iki yerde de yapın.
    /// </summary>
    public static class KonuSuzgeci
    {
        // Karşılaştırma harfleri düzleştirilmiş (ğüşıöç → gusioc) metin üzerinde yapılır; desenler de düz harfle yazılmıştır.
        private static readonly Regex Spor = new(
            @"futbol|basketbol|voleybol|hentbol|tenis\b|atletizm|olimpiyat|paralimpik|super lig|1\. lig|\btff\b|uefa|fifa|sampiyonlar ligi|avrupa ligi|konferans ligi|derbi|transfer|teknik direktor|antrenor|" +
            @"kulup\b|kulubu|stadyum|\bstad\b|hakem|var odasi|penalti|ofsayt|\bgol\b|golcu|kaleci|forvet|orta saha|defans|\bforma\b|milli takim|milli mac|\bmac\b|\bmaci\b|\bmacta\b|\bmaclar|karsilasma|puan durumu|fikstur|\blig\b|play-?off|\bkupa\b|" +
            @"sampiyonluk|galatasaray|fenerbahce|besiktas|trabzonspor|basaksehir fk|sivasspor|konyaspor|antalyaspor|rizespor|alanyaspor|gaziantep fk|kayserispor|samsunspor|eyupspor|bodrum fk|adana demirspor|hatayspor|ankaragucu|pendikspor|umraniyespor|bursaspor|" +
            @"belediyespor|\bspor\b|sporcu|gures|\bboks\b|formula 1|motogp|\bnba\b|euroleague|dunya kupasi|bahis|iddaa|canli skor|ilk 11|kadro aciklandi|tribun|taraftar|santrafor|a milli|genc milli|ampute|e-spor|espor",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static readonly Regex Magazin = new(
            @"magazin|\bdizi\b|dizisi|\bfilm\b|filmi|oyuncu|sarkici|\bunlu\b|unluler|\bdugun|nisanlan|nisan tore|bosan(di|ma)|\bask\b|sevgili|konser|album|\bklip\b|\bburc(lar)?\b|astroloji|\bfal\b|horoskop|yemek tarifi|\btarif\b|diyet|guzellik|\bmoda\b|kombin|survivor|masterchef|big brother|kismetse|" +
            @"hava durumu|hava nasil|yagmur|kar yagisi|firtina|nobetci eczane|altin fiyat|doviz kuru bugun|dolar kuru bugun|burs sonuc|okul tatil|cekilis|sans topu|sayisal loto|piyango|gebelik|bebek ismi|ruya tabiri|kripto para|bitcoin",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Spor haberini ancak güçlü siyasi ifadeler (hükümet, meclis, parti vb.) kurtarır; "Cumhurbaşkanı sporcularla buluştu" gibi spor odaklı haberler elenir
        private static readonly Regex SiyasiGuclu = new(
            @"hukumet|iktidar|muhalefet|meclis|milletvekil|tbmm|ittifak|kurultay|anayasa|\bparti|\bchp\b|\bakp\b|ak parti|\bmhp\b|iyi parti|dem parti|yeniden refah",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        // Açıkça siyasi/kurumsal ifadeler: spor veya magazin kelimesi geçse bile haberi kurtarır (ör. "Cumhurbaşkanı stadyum açılışında...")
        private static readonly Regex Siyasi = new(
            @"cumhurbaskan|\bparti|hukumet|iktidar|muhalefet|meclis|milletvekil|bakani\b|bakanligi|belediye baskan|tbmm|ittifak|kurultay|anayasa|buyuksehir belediye|\bchp\b|\bakp\b|ak parti|\bmhp\b|iyi parti|dem parti|yeniden refah|genel baskan|kabine|valilik|savci|sorusturma",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        private static string Duzlestir(string s) =>
            (s ?? "").Replace("İ", "i").Replace("I", "ı").ToLowerInvariant()
                     .Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ı', 'i').Replace('ö', 'o').Replace('ç', 'c').Replace("i̇", "i");

        /// <summary>false → bu başlık siyasi analize/bildirime uygun değil (spor, magazin, hava durumu vb.).</summary>
        public static bool Uygun(string? baslik)
        {
            string f = Duzlestir(baslik ?? "");
            if (Spor.IsMatch(f)) return SiyasiGuclu.IsMatch(f);
            if (Magazin.IsMatch(f)) return Siyasi.IsMatch(f);
            return true;
        }
    }
}
