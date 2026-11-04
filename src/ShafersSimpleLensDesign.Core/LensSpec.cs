namespace ShafersSimpleLensDesign.Core;

/// <summary>
/// What a surface does, in Shafer's method. Every surface that controls the monochromatic aberrations is either
/// concentric about the chief ray or aplanatic for the marginal ray; its radius follows from the paraxial rays.
/// </summary>
public enum Role
{
    /// <summary>Centered where the incident chief ray crosses the axis: the chief ray passes undeviated, so the surface
    /// adds no coma, astigmatism or distortion. It adds spherical aberration and Petzval curvature.</summary>
    Concentric,

    /// <summary>The marginal ray's axial point at its aplanatic conjugate: no spherical aberration, coma or astigmatism.
    /// It adds Petzval curvature and distortion, and changes the convergence by the index ratio. Flat for a collimated
    /// beam.</summary>
    Aplanatic,

    /// <summary>A plane with no power where the aperture stop is, between two surfaces.</summary>
    Stop,

    /// <summary>A radius given outright.</summary>
    Fixed,

    /// <summary>
    /// A buried surface: a cemented interface between two glasses of (nearly) one index and different dispersions. It
    /// leaves the design wavelength almost untouched and changes the color; its radius is free, usually solved for axial
    /// or lateral color. Concentric about the chief ray it changes axial color only.
    /// </summary>
    Buried,

    /// <summary>
    /// Centered on the incident marginal ray's axial point, curvature −u/y: the marginal ray passes undeviated (A = 0), so
    /// the surface adds no spherical aberration or coma. Between partner glasses ("M~") it is the buried surface that
    /// changes lateral color only, by H Δ(δn/n) wherever it sits in the element.
    /// </summary>
    Marginal,
}

/// <summary>
/// One surface of a surface order: its role, the medium after it (a glass CATALOG:NAME, or empty for air), and the
/// axial distance to the next surface. A free thickness is solved for one of the targets.
/// </summary>
public sealed class SurfaceSpec
{
    public Role Role { get; set; }

    /// <summary>The aperture stop is at this surface's vertex.</summary>
    public bool IsStop { get; set; }

    /// <summary>
    /// A concentric or aplanatic surface between partner glasses: a buried surface that keeps its role ("C~", "A~"). With
    /// its role it adds no coma or astigmatism whatever the small index step across it; its radius follows from the
    /// rules, so its position in the element is the knob for color. Concentric, it changes axial color only.
    /// </summary>
    public bool Partner { get; set; }

    /// <summary>A buried surface of either kind: free radius (<see cref="Role.Buried"/>) or with a role (<see cref="Partner"/>).</summary>
    public bool IsBuried => Role == Role.Buried || Partner;

    /// <summary>The glass after this surface; empty for air.</summary>
    public string Glass { get; set; } = "";

    /// <summary>To the next surface. Ignored on the last surface, whose distance is to the paraxial image.</summary>
    public double Thickness { get; set; }

    /// <summary>For <see cref="Role.Fixed"/> and <see cref="Role.Buried"/>: the radius (0 for a plane); a buried surface's
    /// starting radius when it is solved.</summary>
    public double Radius { get; set; }

    public override string ToString() =>
        $"{Role}{(IsStop ? "@" : "")} {(Glass.Length == 0 ? "air" : Glass)} {Thickness}";
}

/// <summary>An aberration the design is solved for, and the surface whose thickness is varied to do it.</summary>
public enum Aberration
{
    /// <summary>Petzval sum.</summary>
    Petzval,

    /// <summary>Third-order spherical aberration (Seidel S1).</summary>
    Spherical,

    /// <summary>Third-order distortion (Seidel S5).</summary>
    Distortion,

    /// <summary>
    /// Real spherical aberration: where a real ray from the edge of the entrance pupil (or the zone
    /// <see cref="LensSpec.SphericalZone"/>) crosses the axis, less the paraxial focus. Zero there balances the third
    /// order against the higher orders, as a designer correcting spherical aberration by ray trace does.
    /// </summary>
    RealSpherical,

    /// <summary>Paraxial axial color: the shortest wavelength's focus less the longest's.</summary>
    AxialColor,

    /// <summary>Paraxial lateral color: the shortest wavelength's full-field image height less the longest's.</summary>
    LateralColor,

    /// <summary>Third-order coma (Seidel S2). Every concentric and aplanatic surface is free of it, so only a fixed or
    /// buried surface off the stop brings it in, and moving the stop is what takes it out.</summary>
    Coma,

    /// <summary>Third-order astigmatism (Seidel S3).</summary>
    Astigmatism,
}

/// <summary>Solve <paramref name="What"/> to zero by varying the thickness after surface <paramref name="Knob"/> (1-based, as
/// in the surface order), or, with <paramref name="Radius"/>, that surface's radius (a buried or fixed surface's).
/// <paramref name="Zone"/>, for <see cref="Aberration.RealSpherical"/>, is the pupil zone whose real ray is brought to the
/// paraxial focus (0: the spec's <see cref="LensSpec.SphericalZone"/>); two such targets at two zones correct the fifth
/// order as well as the third. With <paramref name="Position"/>, the knob is where surface Knob sits in its element: the
/// thickness before it, the one after it taking up the difference so the element keeps its thickness.</summary>
public sealed record Target(Aberration What, int Knob, bool Radius = false, double Zone = 0, bool Position = false);

/// <summary>
/// A lens to build: its first-order specification and a surface order. Lengths in the surface order are in any unit;
/// the finished lens is scaled to <see cref="Efl"/>. Object at infinity. Wavelengths in micrometers.
/// </summary>
public sealed class LensSpec
{
    public const double LambdaD = 0.5875618, LambdaF = 0.4861327, LambdaC = 0.6562725;

    public double Efl { get; set; } = 100;
    public double FNumber { get; set; } = 8;
    public double HalfField { get; set; } = 3;
    public double[] Wavelengths { get; set; } = { LambdaF, LambdaD, LambdaC };

    /// <summary>The weight of each wavelength in the spot merit, in the order of <see cref="Wavelengths"/>; null for equal.</summary>
    public double[]? WavelengthWeights { get; set; }
    public double DesignWavelength { get; set; } = LambdaD;

    /// <summary>The pupil zone (fraction of the entrance-pupil radius) whose real ray <see cref="Aberration.RealSpherical"/>
    /// brings to the paraxial focus.</summary>
    public double SphericalZone { get; set; } = 1.0;

    /// <summary>When the targets cannot be solved directly from the starting lens, reach them by continuation: start from
    /// the aberrations the starting lens has and bring them to zero in steps, each solved from the last. Slower to fail, so
    /// off by default in searches.</summary>
    public bool Continuation { get; set; }
    public List<SurfaceSpec> Surfaces { get; set; } = new();
    public List<Target> Targets { get; set; } = new();

    /// <summary>A copy sharing the surface and target lists.</summary>
    public LensSpec Copy() => new()
    {
        Efl = Efl, FNumber = FNumber, HalfField = HalfField, Wavelengths = Wavelengths, WavelengthWeights = WavelengthWeights,
        DesignWavelength = DesignWavelength,
        SphericalZone = SphericalZone, Continuation = Continuation, Surfaces = Surfaces, Targets = Targets,
    };
}
