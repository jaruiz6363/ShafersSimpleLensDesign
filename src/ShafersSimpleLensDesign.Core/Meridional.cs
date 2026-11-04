namespace ShafersSimpleLensDesign.Core;

/// <summary>
/// An exact meridional trace through spheres and planes, for the builder's real-ray targets: a ray entering parallel to
/// the axis at height h, carried surface by surface. Coordinates are axial (z, from the current surface's vertex) and
/// meridional (y); (M, N) are the direction cosines.
/// </summary>
public static class Meridional
{
    /// <summary>The ray after the last surface: its point there (z from that surface's vertex) and its direction.</summary>
    public readonly record struct Ray(double Y, double Z, double M, double N, bool Ok);

    public static Ray Trace(double[] n, double[] t, double[] c, double h)
    {
        double y = h, z = 0, m = 0, nn = 1;        // start on surface 1's vertex plane, parallel to the axis
        for (int k = 0; k < c.Length; k++)
        {
            // Into surface k's own frame: z is relative to its vertex.
            double s;
            double ny, nz;
            if (c[k] == 0)
            {
                if (nn <= 0) return default;
                s = -z / nn;
                ny = 0; nz = 1;
            }
            else
            {
                double r = 1 / c[k];
                double qz = z - r;
                double b = y * m + qz * nn, cc = y * y + qz * qz - r * r;
                double disc = b * b - cc;
                if (disc < 0) return default;
                double root = Math.Sqrt(disc);
                // The intersection nearer the vertex plane: the one on the same side of the center as the vertex.
                double s1 = -b - root, s2 = -b + root;
                double z1 = z + s1 * nn, z2 = z + s2 * nn;
                s = Math.Abs(z1) < Math.Abs(z2) ? s1 : s2;
                double yh = y + s * m, zh = z + s * nn;
                ny = -yh * c[k]; nz = 1 - zh * c[k];       // unit normal (towards the center for c > 0, flipped below)
            }
            y += s * m; z += s * nn;

            double mu = n[k] / n[k + 1];
            double cos1 = m * ny + nn * nz;
            if (cos1 < 0) { ny = -ny; nz = -nz; cos1 = -cos1; }
            double cos2sq = 1 - mu * mu * (1 - cos1 * cos1);
            if (cos2sq < 0) return default;
            double g = Math.Sqrt(cos2sq) - mu * cos1;
            m = mu * m + g * ny;
            nn = mu * nn + g * nz;
            if (k < c.Length - 1) z -= t[k];            // into the next surface's frame
        }
        return new Ray(y, z, m, nn, true);
    }
}
