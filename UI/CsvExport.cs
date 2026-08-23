using System;
using System.Globalization;
using System.IO;
using System.Text;
using BepInEx;
using ModProfiler.Profiling;

namespace ModProfiler.UI
{
    internal static class CsvExport
    {
        internal static string Write()
        {
            string dir = Path.Combine(Paths.PluginPath, "maschine-ModProfiler");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, $"modprofiler-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

            var sb = new StringBuilder();
            sb.AppendLine("mod,category,method,total_ms,calls,avg_ms_per_call");
            foreach (ModStats mod in ProfilerCore.Mods)
            {
                foreach (MethodStats m in mod.Methods)
                {
                    double totalMs = m.TotalTicks * ProfilerCore.MsPerTick;
                    double perCall = m.TotalCalls > 0 ? totalMs / m.TotalCalls : 0.0;
                    sb.Append(Quote(mod.Name)).Append(',')
                      .Append(m.Category).Append(',')
                      .Append(Quote(m.DisplayName)).Append(',')
                      .Append(totalMs.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                      .Append(m.TotalCalls).Append(',')
                      .Append(perCall.ToString("F4", CultureInfo.InvariantCulture))
                      .AppendLine();
                }
            }
            File.WriteAllText(path, sb.ToString());
            return path;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}
