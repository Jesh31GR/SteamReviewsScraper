using System;
using System.IO;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;
using HtmlAgilityPack;

namespace SteamReviewScraper
{
    class ExtractSteamReviews
    {
        public static void Run(int appId, string appName, string mode = "all")
        {
            string inputHtml = "page.html"; // HTML generado por Program.cs

            // Sanitizar appName para nombre de carpeta/archivo
            string sanitizedAppName = Regex.Replace(appName ?? "", @"[<>:""/\\|?*]", "_");

            // Carpeta destino en el Escritorio: reviews_{appId}_{sanitizedAppName}
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string outFolder = Path.Combine(desktop, $"reviews_{appId}_{sanitizedAppName}");

            if (!File.Exists(inputHtml))
            {
                Console.WriteLine($"No se encontró el archivo {inputHtml}. Ejecuta primero el script de scroll.");
                return;
            }

            Directory.CreateDirectory(outFolder);

            var doc = new HtmlDocument();
            doc.Load(inputHtml);

            var reviewNodes = doc.DocumentNode.SelectNodes("//div[contains(@class,'apphub_UserReviewCardContent')]");
            if (reviewNodes == null || reviewNodes.Count == 0)
            {
                Console.WriteLine("No se encontraron reviews en el HTML.");
                return;
            }

            // Archivos de salida según modo
            StreamWriter allFile = null;
            StreamWriter occidenteFile = null;
            StreamWriter orienteFile = null;

            int occidentalCount = 0;
            int orientalCount = 0;
            int totalCount = 0;

            try
            {
                if (string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
                {
                    string allPath = Path.Combine(outFolder, "all_reviews.txt");
                    allFile = new StreamWriter(allPath);
                }
                else
                {
                    // Los nombres se construirán al final, después de contar
                    // Temporalmente escribimos a archivos genéricos
                    occidenteFile = new StreamWriter(Path.Combine(outFolder, "temp_occidente.txt"));
                    orienteFile = new StreamWriter(Path.Combine(outFolder, "temp_oriente.txt"));
                }

                foreach (var review in reviewNodes)
                {
                    var recommendationNode = review.SelectSingleNode(".//div[@class='title']");
                    string recommendation = recommendationNode != null ? recommendationNode.InnerText.Trim() : "";

                    var hoursNode = review.SelectSingleNode(".//div[@class='hours']");
                    string hoursRaw = hoursNode != null ? hoursNode.InnerText.Trim() : "";
                    double hoursNumber = ParseHours(hoursRaw);

                    var dateNode = review.SelectSingleNode(".//div[@class='date_posted']");
                    string dateRaw = dateNode != null ? dateNode.InnerText.Trim() : "";
                    string dateIso = ParseDate(dateRaw);

                    var contentNode = review.SelectSingleNode(".//div[@class='apphub_CardTextContent']");
                    string content = "";
                    if (contentNode != null)
                    {
                        var dateDiv = contentNode.SelectSingleNode(".//div[@class='date_posted']");
                        if (dateDiv != null) dateDiv.Remove();

                        content = contentNode.InnerHtml;
                        content = Regex.Replace(content, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
                        content = HtmlAgilityPack.HtmlEntity.DeEntitize(content);
                        content = Regex.Replace(content, @"\r?\n+", " ").Trim();
                    }

                    string bloque = FormatearBloque(dateIso, recommendation, hoursNumber, content);

                    if (string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
                    {
                        allFile.WriteLine(bloque);
                        allFile.WriteLine(new string('-', 80));
                        totalCount++;
                    }
                    else
                    {
                        bool esOriental = EsOriental(content);
                        if (esOriental)
                        {
                            orienteFile.WriteLine(bloque);
                            orienteFile.WriteLine(new string('-', 80));
                            orientalCount++;
                        }
                        else
                        {
                            occidenteFile.WriteLine(bloque);
                            occidenteFile.WriteLine(new string('-', 80));
                            occidentalCount++;
                        }
                    }
                }

                if (string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
                {
                    allFile.Flush();
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"    Finalizado. Total de reviews: {totalCount}");
                    Console.ResetColor();
                }
                else
                {
                    occidenteFile.Flush();
                    orienteFile.Flush();

                    // Cerrar para renombrar correctamente
                    occidenteFile.Dispose();
                    orienteFile.Dispose();

                    // Crear nombres nuevos con los contadores
                    string prefix = mode.ToLowerInvariant() == "negative" ? "negative" : "positive";

                    string occidentalPathFinal = Path.Combine(outFolder, $"{prefix}_reviews_occidente_{occidentalCount}.txt");
                    string orientalPathFinal = Path.Combine(outFolder, $"{prefix}_reviews_oriente_{orientalCount}.txt");

                    // Renombrar los archivos temporales
                    File.Move(Path.Combine(outFolder, "temp_occidente.txt"), occidentalPathFinal, true);
                    File.Move(Path.Combine(outFolder, "temp_oriente.txt"), orientalPathFinal, true);

                    Console.WriteLine($"    Finalizado. Reviews de occidente: {occidentalCount} | Reviews de oriente: {orientalCount}");
                }

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"    Carpeta: {outFolder}\n");
                Console.ResetColor();
                //AbrirCarpeta(outFolder);
            }
            finally
            {
                allFile?.Dispose();
                occidenteFile?.Dispose();
                orienteFile?.Dispose();
            }
        }

        private static double ParseHours(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            var match = Regex.Match(text, @"([\d,.]+)");
            if (match.Success)
            {
                if (double.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double hours))
                    return hours;
            }
            return 0;
        }

        private static string ParseDate(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";

            text = text.Replace("Publicada el ", "").Trim();

            var months = new System.Collections.Generic.Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                {"enero",1},{"febrero",2},{"marzo",3},{"abril",4},{"mayo",5},{"junio",6},
                {"julio",7},{"agosto",8},{"septiembre",9},{"octubre",10},{"noviembre",11},{"diciembre",12}
            };

            var parts = text.Split(' ');
            if (parts.Length < 3) return text;

            if (int.TryParse(parts[0], out int day) && months.TryGetValue(parts[2], out int month) && int.TryParse(parts[4], out int year))
            {
                return new DateTime(year, month, day).ToString("yyyy-MM-dd");
            }

            return text;
        }

        private static bool EsOriental(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;

            foreach (var ch in s)
            {
                int code = ch;
                if ((code >= 0x4E00 && code <= 0x9FFF) || (code >= 0x3400 && code <= 0x4DBF)) return true; // CJK
                if ((code >= 0x1100 && code <= 0x11FF) || (code >= 0x3130 && code <= 0x318F) || (code >= 0xAC00 && code <= 0xD7AF)) return true; // Hangul
                if ((code >= 0x3040 && code <= 0x309F) || (code >= 0x30A0 && code <= 0x30FF)) return true; // Hiragana/Katakana
                if (code >= 0x0400 && code <= 0x04FF) return true; // Cirílico
            }
            return false;
        }

        private static string FormatearBloque(string fechaIso, string calificacion, double horas, string contenido)
        {
            return
$@"Fecha: {fechaIso}
Calificación: {calificacion}
Horas jugadas: {horas.ToString(CultureInfo.InvariantCulture)}
Contenido:
{contenido}";
        }

        private static void AbrirCarpeta(string path)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer",
                        Arguments = $"\"{path}\"",
                        UseShellExecute = true
                    });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    System.Diagnostics.Process.Start("open", path);
                }
                else
                {
                    System.Diagnostics.Process.Start("xdg-open", path);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"No se pudo abrir la carpeta automáticamente: {ex.Message}");
            }
        }
    }
}