using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace TxtToCsv
{
    class Program
    {
        static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Uso: dotnet run -- <ruta_al_txt> -csv");
                return 1;
            }

            string inputPath = args[0];
            if (!File.Exists(inputPath))
            {
                Console.WriteLine($"No existe el archivo: {inputPath}");
                return 1;
            }

            // Debe venir -csv
            bool toCsv = args.Any(a => string.Equals(a, "-csv", StringComparison.OrdinalIgnoreCase));
            if (!toCsv)
            {
                Console.WriteLine("Debes usar: -csv");
                return 1;
            }

            // Crear salida automáticamente cambiando extensión
            string outputPath = Path.ChangeExtension(inputPath, ".csv");

            try
            {
                var text = File.ReadAllText(inputPath, DetectEncoding(inputPath));

                var blocks = Regex.Split(text, @"\r?\n-{3,}\r?\n", RegexOptions.Multiline)
                                  .Select(b => b.Trim())
                                  .Where(b => !string.IsNullOrWhiteSpace(b))
                                  .ToList();

                using var sw = new StreamWriter(outputPath, false, new UTF8Encoding(true));
                sw.WriteLine("Fecha;Calificacion;Horas;Idioma;Contenido");

                foreach (var block in blocks)
                {
                    string fechaLine = GetLineAfterPrefix(block, "Fecha:").Replace("Posted:", "", StringComparison.OrdinalIgnoreCase).Trim();
                    string calif = GetLineAfterPrefix(block, "Calificación:").Trim();
                    string horasRaw = GetLineAfterPrefix(block, "Horas jugadas:").Trim();
                    double horas = ParseHours(horasRaw);
                    string contenido = StripHtml(ExtractContenido(block));
                    string fechaCsv = ToYYYY_M_D(fechaLine);
                    string idioma = DetectLanguage(contenido);

                    sw.WriteLine(string.Join(";",
                        Csv(fechaCsv),
                        Csv(calif),
                        horas.ToString(CultureInfo.InvariantCulture),
                        Csv(idioma),
                        Csv(contenido)
                    ));
                }

                Console.WriteLine($"CSV creado: {outputPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                return 2;
            }
        }

        static Encoding DetectEncoding(string path)
        {
            // Simple: si empieza con BOM UTF-8, usar UTF-8 con BOM; si no, UTF-8 normal.
            using var fs = File.OpenRead(path);
            byte[] bom = new byte[3];
            int read = fs.Read(bom, 0, 3);
            if (read == 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return new UTF8Encoding(true);
            return new UTF8Encoding(false);
        }

        static string GetLineAfterPrefix(string block, string prefix)
        {
            foreach (var line in block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed.Substring(prefix.Length).Trim();
                }
            }
            return "";
        }

        static string ExtractContenido(string block)
        {
            var lines = block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            int idx = Array.FindIndex(lines, l => l.Trim().Equals("Contenido:", StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return "";
            var sb = new StringBuilder();
            for (int i = idx + 1; i < lines.Length; i++)
                sb.AppendLine(lines[i]);
            return Regex.Replace(sb.ToString(), @"\r?\n+$", "").Trim();
        }

        static double ParseHours(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var m = Regex.Match(text, @"([\d]+(?:[.,]\d+)?)");
            if (m.Success && double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double h))
                return h;
            return 0;
        }

        static string StripHtml(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            // Quitar tags simples
            s = Regex.Replace(s, "<br\\s*/?>", "\n", RegexOptions.IgnoreCase);
            s = Regex.Replace(s, "<.*?>", "");
            // Normalizar espacios y saltos
            s = Regex.Replace(s, @"\r?\n+", " ").Trim();
            return System.Net.WebUtility.HtmlDecode(s);
        }

        static string ToYYYY_M_D(string raw)
        {
            // Limpiar espacios/comas repetidas
            string s = Regex.Replace(raw ?? "", @"\s+", " ").Trim();
            s = s.Trim(',');

            // ¿Falta año? (no hay 4 dígitos seguidos)
            bool hasYear = Regex.IsMatch(s, @"\b\d{4}\b");
            string sWithYear = s;

            int currentYear = DateTime.Now.Year;
            if (!hasYear)
            {
                // Anexamos el año actual para facilitar el parseo
                // Si viene "11 February" -> "11 February {currentYear}"
                // Si viene "October 31"  -> "October 31 {currentYear}"
                sWithYear = s + " " + currentYear.ToString();
            }

            // Probaremos varios estilos y culturas (US/GB/ES) porque el orden puede variar
            string[] formats = new[]
            {
                "MMMM d, yyyy", "MMM d, yyyy", "MMMM d yyyy", "MMM d yyyy",
                "d MMMM, yyyy", "d MMM, yyyy", "d MMMM yyyy", "d MMM yyyy",
                // Por si acaso:
                "yyyy-M-d", "yyyy/M/d"
            };

            var cultures = new[]
            {
                new CultureInfo("en-US"), // "October 29, 2024"
                new CultureInfo("en-GB"), // "29 October 2024"
                new CultureInfo("es-ES")  // "29 octubre 2024"
            };

            foreach (var ci in cultures)
            {
                foreach (var f in formats)
                {
                    if (DateTime.TryParseExact(sWithYear, f, ci, DateTimeStyles.None, out DateTime dt))
                    {
                        // Formato pedido: "YYYY/M/D" sin ceros a la izquierda
                        return $"{dt.Year}/{dt.Month}/{dt.Day}";
                    }
                }
                // También intentamos parseo libre de la cultura por si el texto es “October 28, 2024”
                if (DateTime.TryParse(sWithYear, ci, DateTimeStyles.AllowWhiteSpaces, out DateTime dtFree))
                    return $"{dtFree.Year}/{dtFree.Month}/{dtFree.Day}";
            }

            // Si todo falla, devolvemos el texto en crudo (pero no debería pasar con tus ejemplos)
            return s;
        }

        // ===== NUEVO: detección de idioma muy ligera (heurística) =====
        static readonly char[] EsChars = "áéíóúñ".ToCharArray();
        static readonly char[] PtChars = "áéíóúãõç".ToCharArray();
        static readonly char[] FrChars = "àâçéèêëîïôùûüœ".ToCharArray();
        static readonly char[] DeChars = "äöüß".ToCharArray();
        static readonly char[] ItChars = "àèéìòóù".ToCharArray();

        static readonly HashSet<string> StopEs = new(StringComparer.OrdinalIgnoreCase)
        { "de","la","que","el","en","y","a","los","se","del","las","por","un","para","con","no","una","su","al","lo" };

        static readonly HashSet<string> StopEn = new(StringComparer.OrdinalIgnoreCase)
        { "the","and","to","of","a","in","that","is","for","it","on","as","with","was","are","this","by","be","or","from" };

        static readonly HashSet<string> StopPt = new(StringComparer.OrdinalIgnoreCase)
        { "de","a","o","que","e","do","da","em","um","para","com","não","uma","os","no","se","na","por","mais","as" };

        static readonly HashSet<string> StopFr = new(StringComparer.OrdinalIgnoreCase)
        { "de","la","et","le","à","les","des","en","du","que","pour","est","dans","une","un","sur","pas","plus","au","par" };

        static readonly HashSet<string> StopDe = new(StringComparer.OrdinalIgnoreCase)
        { "und","die","der","in","zu","den","das","nicht","von","sie","ist","des","sich","mit","dem","dass","ein","im","für","an" };

        static readonly HashSet<string> StopIt = new(StringComparer.OrdinalIgnoreCase)
        { "di","e","che","la","il","a","per","in","un","è","del","si","dei","con","le","della","dal","al","ma","più" };

        static string DetectLanguage(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "und";

            // Normalizamos a minúsculas para búsquedas simples
            string lower = text.ToLowerInvariant();

            // Conteo por stopwords
            var tokens = Regex.Matches(lower, @"\p{L}+")
                              .Select(m => m.Value)
                              .ToList();

            int countEs = tokens.Count(t => StopEs.Contains(t));
            int countEn = tokens.Count(t => StopEn.Contains(t));
            int countPt = tokens.Count(t => StopPt.Contains(t));
            int countFr = tokens.Count(t => StopFr.Contains(t));
            int countDe = tokens.Count(t => StopDe.Contains(t));
            int countIt = tokens.Count(t => StopIt.Contains(t));

            // Señales por diacríticos/caracteres típicos (ponderan +2 cada grupo presente)
            int extraEs = EsChars.Any(c => lower.IndexOf(c) >= 0) ? 2 : 0;
            int extraPt = PtChars.Any(c => lower.IndexOf(c) >= 0) ? 2 : 0;
            int extraFr = FrChars.Any(c => lower.IndexOf(c) >= 0) ? 2 : 0;
            int extraDe = DeChars.Any(c => lower.IndexOf(c) >= 0) ? 2 : 0;
            int extraIt = ItChars.Any(c => lower.IndexOf(c) >= 0) ? 2 : 0;

            var scores = new Dictionary<string, int>
            {
                ["es"] = countEs + extraEs,
                ["en"] = countEn,
                ["pt"] = countPt + extraPt,
                ["fr"] = countFr + extraFr,
                ["de"] = countDe + extraDe,
                ["it"] = countIt + extraIt
            };

            // Ganador por mayor puntuación con un umbral mínimo suave
            var best = scores.OrderByDescending(kv => kv.Value).First();
            if (best.Value >= 3) return best.Key;

            // Si no alcanza el umbral, intentamos desempatar por longitud de texto y presencia de patrones
            // Heurística final: si hay solo letras ASCII y espacios, inclínate por inglés
            bool asciiOnly = lower.All(ch => ch <= 0x7F);
            if (asciiOnly && (countEn >= 1 || tokens.Count >= 5))
                return "en";

            return "und"; // desconocido/indeterminado
        }

        static string Csv(string contenido)
        {
            if (contenido is null) contenido = "";
            // NOTA: usamos ';' como separador, así que debemos citar cuando hay ';'
            bool mustQuote =
                contenido.Contains(';') ||
                contenido.Contains(',') ||
                contenido.Contains('"') ||
                contenido.Contains('\n') ||
                contenido.Contains('\r');
            if (mustQuote)
            {
                contenido = contenido.Replace("\"", "\"\"");
                return $"\"{contenido}\"";
            }
            return contenido;
        }
    }
}