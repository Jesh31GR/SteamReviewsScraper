using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace SteamReviewScraper
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Uso: dotnet run -- <URL_principal_o_de_reviews_de_Steam> [-n | -po]");
                Console.WriteLine("Ejemplo (store):  https://store.steampowered.com/app/2794610/Massacre_At_The_Mirage/");
                Console.WriteLine("Ejemplo (reviews): https://steamcommunity.com/app/2794610/reviews/?browsefilter=mostrecent");
                Console.WriteLine("Flags opcionales: -n  |  -po");
                return;
            }

            string inputUrl = args[0].Trim();
            string mode = "all";
            if (args.Skip(1).Any(a => string.Equals(a, "-n", StringComparison.OrdinalIgnoreCase))) mode = "negative";
            if (args.Skip(1).Any(a => string.Equals(a, "-po", StringComparison.OrdinalIgnoreCase))) mode = "positive";

            int appId = 0;
            string appSlug = null;

            var storeMatch = Regex.Match(inputUrl, @"store\.steampowered\.com/app/(\d+)/([^/?#]+)/?", RegexOptions.IgnoreCase);
            if (storeMatch.Success)
            {
                appId = int.Parse(storeMatch.Groups[1].Value);
                appSlug = storeMatch.Groups[2].Value;
            }
            else
            {
                var reviewsMatch = Regex.Match(inputUrl, @"steamcommunity\.com/app/(\d+)/(?:reviews|negativereviews|positivereviews)", RegexOptions.IgnoreCase);
                if (reviewsMatch.Success)
                {
                    appId = int.Parse(reviewsMatch.Groups[1].Value);
                }
            }

            if (appId == 0)
            {
                Console.WriteLine("No se pudo extraer el AppID. Asegúrate de pasar una URL válida de Steam.");
                return;
            }

            string appName = null;
            if (!string.IsNullOrWhiteSpace(appSlug))
            {
                appName = WebUtility.UrlDecode(appSlug.Replace('_', ' ')).Trim();
            }

            // Elegir path según modo
            string pathSegment = mode == "negative" ? "negativereviews"
                              : mode == "positive" ? "positivereviews"
                              : "reviews";

            string reviewsUrl =
                $"https://steamcommunity.com/app/{appId}/{pathSegment}/?browsefilter=mostrecent&snr=1_5_100010_&p=1&filterLanguage=all";
            Console.WriteLine($"Navegando a: {reviewsUrl}");

            string outputHtml = "page.html";

            using var playwright = await Playwright.CreateAsync();
            var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            var page = await browser.NewPageAsync();
            await page.GotoAsync(reviewsUrl, new PageGotoOptions { WaitUntil = WaitUntilState.Load });

            if (string.IsNullOrWhiteSpace(appName))
            {
                try
                {
                    var domName = await page.TextContentAsync(".apphub_AppName.ellipsis");
                    if (string.IsNullOrWhiteSpace(domName))
                    {
                        domName = await page.TextContentAsync(".apphub_AppName, .app_tag, .apphub_AppNameContainer");
                    }
                    if (!string.IsNullOrWhiteSpace(domName))
                    {
                        appName = domName.Trim();
                    }
                }
                catch
                {
                    // Ignorar
                }
            }

            appName ??= string.Empty;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("    Iniciando scroll dinámico...");
            Console.ResetColor();

            int previousHeight = 0;
            var stopwatch = new Stopwatch();
            stopwatch.Start();

            while (true)
            {
                int currentHeight = await page.EvaluateAsync<int>("() => document.body.scrollHeight");
                if (currentHeight == previousHeight)
                    break;

                previousHeight = currentHeight;
                await page.EvaluateAsync("() => window.scrollTo(0, document.body.scrollHeight)");
                await Task.Delay(2500);
            }

            stopwatch.Stop();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"    Scroll completado en {stopwatch.Elapsed.TotalSeconds:F2} segundos.");
            Console.ResetColor();

            string htmlContent = await page.ContentAsync();
            File.WriteAllText(outputHtml, htmlContent);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"    HTML guardado en {outputHtml}");
            Console.ResetColor();

            //AppDataOverview.GetAppData(appId, appName, inputUrl, outputHtml);

            if (string.Equals(mode, "all", StringComparison.OrdinalIgnoreCase))
            {
                // Solo ejecuta AppDataOverview si NO hay banderas (-n o -po)
                AppDataOverview.GetAppData(appId, appName, inputUrl, outputHtml);
            }

            await browser.CloseAsync();

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("    Ejecutando extracción de reviews...");
            Console.ResetColor();
            ExtractSteamReviews.Run(appId, appName, mode);
        }
    }
}
