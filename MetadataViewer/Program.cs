using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace ImageMetadataViewer
{
    class Program
    {
        private static readonly HttpClient httpClient = new HttpClient();

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            Console.InputEncoding = Encoding.UTF8;

            httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] potentialPaths = {
                Path.Combine(baseDir, "exiftool.exe"),
                Path.Combine(baseDir, "..", "..", "..", "exiftool.exe"),
                "exiftool.exe"
            };

            string? exifToolPath = potentialPaths.FirstOrDefault(File.Exists) ?? "exiftool.exe";

            while (true)
            {
                Console.WriteLine("-------------------------------------------------------");
                Console.Write("Enter your file path or URL (or 'e' to exit): ");
                string? inputPath = Console.ReadLine()?.Trim('"', '\'');

                if (string.IsNullOrWhiteSpace(inputPath)) continue;
                if (inputPath.Equals("e", StringComparison.OrdinalIgnoreCase)) break;

                string targetPath = inputPath;
                string? tempFilePath = null;

                bool isUrl = inputPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                             inputPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

                if (isUrl)
                {
                    Console.WriteLine("\n📥 Downloading file from URL...");
                    tempFilePath = await DownloadFileAsync(inputPath);

                    if (tempFilePath == null) continue;
                    targetPath = tempFilePath;
                }
                else if (!File.Exists(targetPath))
                {
                    Console.WriteLine("❌ Error: File not found.\n");
                    continue;
                }

                // Option 1
                if (File.Exists(exifToolPath))
                {
                    RunExifTool(exifToolPath, targetPath);

                    Console.WriteLine("[SECURITY ACTION]");
                    Console.Write("Press [S] to STRIP all metadata, or [Enter] to scan another file: ");
                    var key = Console.ReadKey();

                    if (key.Key == ConsoleKey.S || key.Key == ConsoleKey.S)
                    {
                        Console.WriteLine("\n");
                        StripMetadata(exifToolPath, targetPath, isUrl, inputPath);
                    }
                    else
                    {
                        Console.WriteLine("\n");
                    }
                }

                // Option 2
                else
                {
                    RunMetadataExtractor(targetPath);
                    Console.WriteLine("\n⚠️ Install ExifTool (exiftool.exe) to enable the Strip Metadata feature.\n");
                }

                // Delete temp file
                if (isUrl && tempFilePath != null && File.Exists(tempFilePath))
                {
                    try { File.Delete(tempFilePath); } catch { }
                }
            }
        }

        static void StripMetadata(string exifToolPath, string filePath, bool isUrl, string originalInput)
        {
            Console.WriteLine("🧹 Stripping all metadata (EXIF, IPTC, XMP, C2PA, GPS)...");
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exifToolPath,
                        Arguments = $"-all= -overwrite_original \"{filePath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };
                process.Start();
                process.WaitForExit();

                if (isUrl)
                {
                    string fileName = "Cleaned_Image.jpg";
                    try
                    {
                        string extracted = Path.GetFileName(new Uri(originalInput).LocalPath);
                        if (!string.IsNullOrWhiteSpace(extracted) && extracted.Contains("."))
                        {
                            fileName = "Cleaned_" + extracted;
                        }
                    }
                    catch { }

                    string newPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                    if (File.Exists(newPath)) File.Delete(newPath);

                    File.Move(filePath, newPath);
                    Console.WriteLine($"✅ Done! The clean image is saved at:\n   {newPath}\n");
                }
                else
                {
                    Console.WriteLine($"✅ Done! Your original file is now 100% clean and safe to share.\n");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error during stripping: {ex.Message}\n");
            }
        }

        static async Task<string?> DownloadFileAsync(string url)
        {
            try
            {
                byte[] fileBytes = await httpClient.GetByteArrayAsync(url);
                string tempPath = Path.GetTempFileName();
                await File.WriteAllBytesAsync(tempPath, fileBytes);
                Console.WriteLine("✅ Download complete!\n");
                return tempPath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error downloading file: {ex.Message}\n");
                return null;
            }
        }

        static void RunExifTool(string exePath, string imagePath)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = exePath,
                        Arguments = $"-a -G1 -s \"{imagePath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        StandardOutputEncoding = Encoding.UTF8
                    }
                };

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(output))
                {
                    Console.WriteLine(output);
                    PrintSummaryReport(output);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred while calling ExifTool: {ex.Message}");
            }
        }

        static void RunMetadataExtractor(string path)
        {
            try
            {
                var directories = ImageMetadataReader.ReadMetadata(path);
                bool hasData = false;
                StringBuilder sb = new StringBuilder();

                foreach (var directory in directories)
                {
                    if (!directory.Tags.Any()) continue;
                    hasData = true;
                    Console.WriteLine($"📂 [{directory.Name.ToUpper()}]");

                    foreach (var tag in directory.Tags)
                    {
                        string line = $"{tag.Name,-30} : {tag.Description}";
                        Console.WriteLine($"   - {line}");
                        sb.AppendLine(line);
                    }
                    Console.WriteLine();
                }

                if (!hasData) Console.WriteLine("⚠️ No metadata found in this file.");
                else PrintSummaryReport(sb.ToString());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
        }

        static void PrintSummaryReport(string data)
        {
            string aiPattern = @"(c2pa|trainedAlgorithmicMedia|algorithmicallyEnhanced|compositeWithTrainedAlgorithmicMedia|generative|diffusion|ai[\s\-_]*generated|neural|upscaler|synthesiz)";
            bool hasAI = Regex.IsMatch(data, aiPattern, RegexOptions.IgnoreCase);
            bool hasGPS = Regex.IsMatch(data, @"GPS\s*(Latitude|Position|Coordinates)", RegexOptions.IgnoreCase);

            Match makeMatch = Regex.Match(data, @"\bMake\s*:\s*(.+)");
            Match modelMatch = Regex.Match(data, @"\bModel\s*:\s*(.+)");

            string makeModel = (makeMatch.Success || modelMatch.Success) ? $"{makeMatch.Groups[1].Value.Trim()} {modelMatch.Groups[1].Value.Trim()}".Trim() : "";

            MatchCollection swMatches = Regex.Matches(data, @"(?:ActionsSoftwareAgentName|Claim_Generator_InfoName|Software|CreatorTool|ProcessingSoftware|HistorySoftwareAgent)\s*:\s*(.+)");
            var softwareSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in swMatches) softwareSet.Add(m.Groups[1].Value.Trim());

            string deviceOrService = "Unknown (Stripped or Missing)";
            if (softwareSet.Count > 0 && !string.IsNullOrEmpty(makeModel)) deviceOrService = $"{makeModel} | Edited via: {string.Join(", ", softwareSet)}";
            else if (softwareSet.Count > 0) deviceOrService = string.Join(", ", softwareSet);
            else if (!string.IsNullOrEmpty(makeModel)) deviceOrService = makeModel;

            string origin = "Unknown / Social Media Stripped";
            string gpsInfo = hasGPS ? "📍 DETECTED (Privacy Risk: Geolocation data exposed)" : "Not Found";
            string riskLevel = "⚪ UNKNOWN";
            string notes = "No hardware specs or AI traces found. Image may have been compressed, downloaded via social media, or stripped of metadata.";

            if (hasAI)
            {
                origin = "AI-Generated or Algorithmically Enhanced";
                riskLevel = "🔴 HIGH (Synthetic / AI Manipulated)";
                notes = "Detected Content Credentials (C2PA) or AI algorithmic tags. This image is synthetic or heavily modified by AI tools.";
            }
            else if (!string.IsNullOrEmpty(makeModel))
            {
                origin = "Physical Device (Camera / Smartphone)";
                riskLevel = hasGPS ? "🟡 MEDIUM (Physical origin, but Privacy Risk)" : "🟢 LOW (Original physical photo)";
                notes = softwareSet.Count > 0 ? "Detected original camera hardware, but photo has been passed through post-processing software." : "Detected natural camera hardware structure. Low likelihood of AI manipulation.";
            }

            Console.WriteLine("\n");
            Console.WriteLine("📊 SUMMARY REPORT (RISK & ORIGIN)");
            Console.WriteLine($"📸 Origin        : {origin}");
            Console.WriteLine($"⚙️ Device/Service: {deviceOrService}");
            Console.WriteLine($"🌍 GPS Location  : {gpsInfo}");
            Console.WriteLine($"⚠️ Risk Level    : {riskLevel}");
            Console.WriteLine($"💡 Notes         : {notes}");
        }
    }
}
