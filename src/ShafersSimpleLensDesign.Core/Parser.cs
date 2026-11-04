using System.Globalization;

namespace ShafersSimpleLensDesign.Core;

/// <summary>
/// Reads a surface order written as text: surfaces separated by commas, each "ROLE GLASS THICKNESS".
/// <list type="bullet">
/// <item>ROLE: C (concentric about the chief ray), A (aplanatic), M (centered on the marginal ray's axial point), S (a stop plane), B or B=radius (a buried surface:
/// plane, or the radius given, until a target solves it), or F=radius (fixed); a trailing @ puts the stop there, a
/// trailing ~ marks a C or A surface between partner glasses (a buried surface that keeps its role).</item>
/// <item>GLASS: the medium after the surface: CATALOG:NAME, air, = (the glass before it again: a stop inside a lens),
/// =j (the glass after surface j: two elements of one glass)
/// or * (a glass the search chooses).</item>
/// <item>THICKNESS: to the next surface; omitted on the last, whose distance is to the image.</item>
/// </list>
/// Example, Shafer's Figure 1: "C SCHOTT:N-SK16 1, A@ air". Targets are written "petzval:2, lsa:1, lateral:r4": the
/// aberration, and the surface whose following thickness is varied, with r that surface's radius, or with p that
/// surface's position in its element.
/// </summary>
public static class Parser
{
    public static List<SurfaceSpec> Surfaces(string text)
    {
        var list = new List<SurfaceSpec>();
        var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            var tok = parts[i].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tok.Length < 2 || (tok.Length < 3 && i < parts.Length - 1))
                throw new ArgumentException($"surface {i + 1} '{parts[i]}': write ROLE GLASS THICKNESS, e.g. 'C SCHOTT:N-SK16 10'");
            var s = new SurfaceSpec();
            string role = tok[0].ToUpperInvariant();
            while (role.EndsWith('@') || role.EndsWith('~'))
            {
                if (role[^1] == '@') s.IsStop = true; else s.Partner = true;
                role = role[..^1];
            }
            if (role.StartsWith("F=") || role.StartsWith("B="))
            {
                s.Role = role[0] == 'F' ? Role.Fixed : Role.Buried;
                s.Radius = Number(role[2..], $"surface {i + 1} radius");
            }
            else
                s.Role = role switch
                {
                    "C" => Role.Concentric,
                    "A" => Role.Aplanatic,
                    "M" => Role.Marginal,
                    "S" => Role.Stop,
                    "B" => Role.Buried,
                    _ => throw new ArgumentException($"surface {i + 1}: role '{tok[0]}' is not C, A, M, S, B, B=radius or F=radius"),
                };
            s.Glass = tok[1].Equals("air", StringComparison.OrdinalIgnoreCase) ? "" : tok[1];
            if (tok.Length > 2) s.Thickness = Number(tok[2], $"surface {i + 1} thickness");
            list.Add(s);
        }
        return list;
    }

    public static List<Target> Targets(string text) =>
        text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Select(p =>
        {
            var kv = p.Split(':', StringSplitOptions.TrimEntries);
            if (kv.Length != 2) throw new ArgumentException($"target '{p}': write ABERRATION:SURFACE, e.g. petzval:2");
            // "lsa@0.7" brings the real ray of the 0.7 zone to the paraxial focus.
            double zone = 0;
            string name = kv[0];
            int at = name.IndexOf('@');
            if (at > 0) { zone = Number(name[(at + 1)..], $"target {p} zone"); name = name[..at]; }
            var what = name.ToLowerInvariant() switch
            {
                "petzval" => Aberration.Petzval,
                "spherical" or "sa" => Aberration.Spherical,
                "coma" => Aberration.Coma,
                "astigmatism" or "astig" => Aberration.Astigmatism,
                "distortion" => Aberration.Distortion,
                "lsa" or "realspherical" => Aberration.RealSpherical,
                "axial" => Aberration.AxialColor,
                "lateral" => Aberration.LateralColor,
                _ => throw new ArgumentException($"target '{name}': use petzval, spherical, lsa (real spherical), coma, astigmatism, distortion, axial or lateral"),
            };
            bool radius = kv[1].StartsWith('r') || kv[1].StartsWith('R');
            bool position = kv[1].StartsWith('p') || kv[1].StartsWith('P');
            return new Target(what, (int)Number(radius || position ? kv[1][1..] : kv[1], $"target {p} surface"), radius, zone, position);
        }).ToList();

    private static double Number(string s, string what) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v
        : throw new ArgumentException($"{what}: '{s}' is not a number");
}
