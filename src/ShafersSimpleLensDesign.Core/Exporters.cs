using AberrationCalculator.Core.Glass;
using AberrationCalculator.Core.IO;
using AberrationCalculator.Core.Models;

namespace ShafersSimpleLensDesign.Core;

/// <summary>Writes one design in several lens formats, all with AberrationCalculator.IO's writers.</summary>
public static class Exporters
{
    public static readonly string[] AllFormats = { "zmx", "seq", "len", "otx", "json", "lhlt" };

    /// <summary>Writes the requested formats into <paramref name="folder"/> as baseName.ext; returns the files written
    /// and, through <paramref name="warnings"/>, what could not be.</summary>
    public static List<string> Write(OpticalSystem system, GlassCatalog catalog, string folder, string baseName,
        IEnumerable<string> formats, List<string> warnings)
    {
        Directory.CreateDirectory(folder);
        var written = new List<string>();
        foreach (var fmt in formats.Select(f => f.Trim().ToLowerInvariant()).Distinct())
        {
            string path = Path.Combine(folder, $"{baseName}.{fmt}");
            try
            {
                switch (fmt)
                {
                    case "zmx": ZmxWriter.Write(system, path); break;
                    case "seq": CodeVWriter.Write(system, path, catalog); break;
                    case "len": OsloWriter.Write(system, path, catalog); break;
                    case "otx": OptalixWriter.Write(system, path, catalog); break;
                    case "json":
                        var r = OptilandWriter.Write(system, path, catalog, false);
                        if (r.ByNameOnly.Count > 0 || r.InstallError != null) warnings.Add("Optiland: " + r.Describe());
                        break;
                    case "lhlt": LhltWriter.Write(system, path); break;
                    default:
                        warnings.Add($"unknown format '{fmt}' (use {string.Join(", ", AllFormats)})");
                        continue;
                }
                written.Add(path);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
            {
                warnings.Add($"{fmt}: not written - {ex.Message}");
            }
        }
        return written;
    }
}
