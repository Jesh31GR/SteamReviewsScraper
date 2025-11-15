using System;
using System.IO;
using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using System.Threading.Tasks;
using System.Globalization;
using System.Diagnostics;

namespace SteamReviewScraper
{
    public static class AppDataOverview
    {
        public static void GetAppData(int appId, string appName, string inputUrl, string reviewsHtmlPath = "page.html")
        {
            int totalReviews = 0;
            int positives = 0;
            int negatives = 0;

            try
            {
                if (!File.Exists(reviewsHtmlPath))
                {
                    Console.WriteLine($"No se encontró el archivo {reviewsHtmlPath}.");
                    return;
                }

                var doc = new HtmlDocument();
                doc.Load(reviewsHtmlPath, Encoding.UTF8);

                var thumbDivs = doc.DocumentNode.SelectNodes("//div[contains(@class,'thumb')]");
                if (thumbDivs != null)
                {
                    foreach (var th in thumbDivs)
                    {
                        var upImg = th.SelectSingleNode(".//img[contains(@src,'icon_thumbsUp')]");
                        var downImg = th.SelectSingleNode(".//img[contains(@src,'icon_thumbsDown')]");

                        if (upImg != null)
                        {
                            positives++;
                            totalReviews++;
                        }
                        else if (downImg != null)
                        {
                            negatives++;
                            totalReviews++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error analizando reviews: {ex.Message}");
            }

            // Obtener el precio actual
            string price = FetchPrice(appId, inputUrl).GetAwaiter().GetResult();

            // Obtener currentDate de lanzamiento + developer
            var (releaseDate, developerName) =
                FetchReleaseAndDeveloper(appId).GetAwaiter().GetResult();

            // === NUEVO: Abrir SteamDB y pedir precio/descuento manual ===
            string steamDbUrl = $"https://steamdb.info/app/{appId}/";

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("    Abriendo SteamDB para datos manuales...");
            Console.ResetColor();

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = steamDbUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                Console.WriteLine("No se pudo abrir automáticamente el navegador. Ábrelo manualmente.");
            }

            Console.Write("\n    Introduce el precio de lanzamiento: ");
            string launchPriceManual = Console.ReadLine()?.Trim() ?? "No disponible";

            Console.Write("    Introduce el descuento de lanzamiento: ");
            string launchDiscountManual = Console.ReadLine()?.Trim() ?? "No disponible";

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("    Datos de lanzamiento ingresados correctamente.\n");
            Console.ResetColor();
            // === FIN NUEVO ===


            // === Cálculos adicionales ===
            double copiasEstimadas = totalReviews * 30;
            double steamFee = 0.7;     // 70% que recibe el desarrollador
            double refundRate = 0.9;   // 90% tras reembolsos estimados
            double netRevenueEstimado = 0;

            // === AHORA USAMOS PRECIO DE LANZAMIENTO EN LUGAR DEL PRECIO ACTUAL ===
            double precioNum = ExtraerPrecioNumerico(launchPriceManual);

            if (precioNum > 0)
            {
                netRevenueEstimado = copiasEstimadas * refundRate * steamFee * precioNum;
            }


            // === Exportar TXT ===
            string sanitizedAppName = Regex.Replace(appName ?? "", @"[<>:""/\\|?*]", "_");
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string outFolder = Path.Combine(desktop, $"reviews_{appId}_{sanitizedAppName}");
            Directory.CreateDirectory(outFolder);
            string outputPath = Path.Combine(outFolder, "app_data_overview.md");
            string currentDate = DateTime.Now.ToString("dd 'de' MMM yyyy", new CultureInfo("es-ES"));

            using (var writer = new StreamWriter(outputPath, false, Encoding.UTF8))
            {
                // Título principal
                writer.WriteLine("# ========= AppData Overview ========= ");
                writer.WriteLine();

                // Información general
                writer.WriteLine("- [Nombre]:                " + appName);
                writer.WriteLine("- [Fecha de lanzamiento]:  " + releaseDate);
                writer.WriteLine("- [AppID]:                 " + appId);
                writer.WriteLine("- [Desarrollador]:         " + developerName);
                writer.WriteLine("- [Página de Steam]:       https://store.steampowered.com/app/" + appId + "/");
                writer.WriteLine();

                // Bases de datos externas
                writer.WriteLine("## Bases de Datos Externas");
                writer.WriteLine();
                writer.WriteLine("- [SteamDB]:               https://steamdb.info/app/" + appId + "/");
                writer.WriteLine("- [SteamSpy]:              https://steamspy.com/app/" + appId);
                writer.WriteLine("- [SteamCharts]:           https://steamcharts.com/app/" + appId);
                writer.WriteLine();

                // Información de reseñas
                writer.WriteLine("## Información de Reseñas");
                writer.WriteLine();
                writer.WriteLine("- [Total reviews]:         " + totalReviews);
                writer.WriteLine("- [Positivas]:             " + positives);
                writer.WriteLine("- [Negativas]:             " + negatives);
                writer.WriteLine();

                // Estimaciones
                writer.WriteLine("## Estimaciones");
                writer.WriteLine();
                writer.WriteLine("- [Precio lanzamiento]:    " + launchPriceManual + " USD");
                writer.WriteLine("- [Descuento lanzamiento]: " + launchDiscountManual + "%");
                writer.WriteLine("- [Copias vendidas]:       ~" + copiasEstimadas.ToString("F0"));
                writer.WriteLine("- [Revenue estimado]:      ~" + netRevenueEstimado.ToString("F2") + " USD");
                writer.WriteLine("- [Precio actual]:         " + price + " (al " + currentDate + ")");
                writer.WriteLine();

                writer.WriteLine("---");
            }

            // === Consola ===
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("    ===== AppData Overview =====");
            Console.WriteLine($"    Nombre:     {appName}");
            Console.WriteLine($"    Developer:  {developerName}");
            Console.ForegroundColor = ConsoleColor.DarkGreen;
            Console.WriteLine($"    Precio lanzamiento:  ${launchPriceManual}");
            Console.WriteLine($"    Copias estimadas:   ~{copiasEstimadas:F0}");
            Console.WriteLine($"    Net revenue est.:   ~{netRevenueEstimado:F2} USD");
            Console.WriteLine($"    Precio hoy:          {price}");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("    ===== URLs =====");
            Console.WriteLine($"    SteamDB:  https://steamdb.info/app/{appId}/");
            Console.WriteLine($"    SteamSpy: https://steamspy.com/app/{appId}/");
            Console.WriteLine($"    Charts:   https://steamcharts.com/app/{appId}");
            Console.ResetColor();
        }

        // === Release date + Developer (sin cambios) ===
        private static async Task<(string releaseDate, string developerName)>
            FetchReleaseAndDeveloper(int appId)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                var url = $"https://store.steampowered.com/app/{appId}/";
                var html = await client.GetStringAsync(url);

                var releaseMatch = Regex.Match(
                    html,
                    "<div class=\"release_date\">[\\s\\S]*?<div class=\"date\">([^<]+)</div>",
                    RegexOptions.IgnoreCase
                );
                string releaseDate = releaseMatch.Success ? releaseMatch.Groups[1].Value.Trim() : "No disponible";

                var devMatch = Regex.Match(
                    html,
                    "<div class=\"summary column\" id=\"developers_list\">[\\s\\S]*?<a href=\"([^\"]+)\">([^<]+)</a>",
                    RegexOptions.IgnoreCase
                );

                string devName = devMatch.Success ? devMatch.Groups[2].Value.Trim() : "No disponible";

                return (releaseDate, devName);
            }
            catch
            {
                return ("Error", "Error");
            }
        }

        // Obtener precio actual desde Steam Store
        private static async Task<string> FetchPrice(int appId, string inputUrl)
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0");
                var url = $"https://store.steampowered.com/app/{appId}/";
                var html = await client.GetStringAsync(url);

                var match = Regex.Match(html, @"<div class=\""?game_purchase_price\""?[^>]*>\s*([^<]+)\s*</div>");
                if (!match.Success)
                    match = Regex.Match(html, @"<div class=\""?discount_final_price\""?[^>]*>\s*([^<]+)\s*</div>");

                if (match.Success)
                {
                    string price = match.Groups[1].Value.Trim();
                    return string.IsNullOrWhiteSpace(price) ? "No disponible" : price;
                }
                return "No disponible";
            }
            catch
            {
                return "Error al obtener precio";
            }
        }

        // Extrae número desde cualquier formato de precio
        private static double ExtraerPrecioNumerico(string price)
        {
            if (string.IsNullOrWhiteSpace(price))
                return 0;

            string limpio = Regex.Replace(price, @"[^\d,\.]", "").Replace(",", ".");
            if (double.TryParse(limpio, NumberStyles.Any, CultureInfo.InvariantCulture, out double valor))
                return valor;

            return 0;
        }
    }
}