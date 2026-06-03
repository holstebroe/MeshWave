using System.IO;
using MeshWave.Common.Core;
using MeshWave.Models;
using NLog;
using NLog.Config;
using NLog.Targets;

namespace MeshWave.Services
{
    public static class LoggingConfiguration
    {
        public static void Configure(LoggingSettings settings)
        {
            if (!settings.Enabled)
            {
                LogManager.Configuration = null;
                return;
            }

            var config = new NLog.Config.LoggingConfiguration();

            var logFolder = MeshWaveEnvironment.CombineInAppData("logs");
            if (!Directory.Exists(logFolder))
            {
                Directory.CreateDirectory(logFolder);
            }

            var fileTarget = new FileTarget("logfile")
            {
                FileName = Path.Combine(logFolder, "meshwave.log"),
                Layout = "${longdate}|${level:uppercase=true}|${logger}|${message} ${exception:format=tostring}",
                ArchiveFileName = Path.Combine(logFolder, "meshwave.{#}.log"),
                ArchiveAboveSize = 10 * 1024 * 1024, // 10 MB
                ArchiveSuffixFormat = ".{#}",
                MaxArchiveFiles = 5,
                KeepFileOpen = false
            };

            config.AddTarget(fileTarget);

            var logLevel = settings.Verbose ? LogLevel.Debug : LogLevel.Info;
            config.AddRule(logLevel, LogLevel.Fatal, fileTarget);

            LogManager.Configuration = config;
        }

        public static string GetLogsFolder()
        {
            return MeshWaveEnvironment.CombineInAppData("logs");
        }

        public static string GetRecentLogs()
        {
            var logFolder = GetLogsFolder();
            var logFile = Path.Combine(logFolder, "meshwave.log");

            if (!File.Exists(logFile))
                return "No log file found.";

            try
            {
                // Read last 100 lines
                using var stream = new FileStream(logFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);

                var lines = new List<string>();
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    lines.Add(line);
                    if (lines.Count > 100)
                        lines.RemoveAt(0);
                }

                return string.Join(Environment.NewLine, lines);
            }
            catch (Exception ex)
            {
                return $"Error reading log file: {ex.Message}";
            }
        }
    }
}
