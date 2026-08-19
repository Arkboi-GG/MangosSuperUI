namespace MangosSuperUI.Services.WeaponForge;

/// <summary>Severity of a single Forge diagnostic. An <see cref="Error"/> fails the stage it was
/// raised in (the mesh is rejected, the M2 is invalid, the package fails verification). A
/// <see cref="Warning"/> is surfaced but does not fail — it flags something a human should look at
/// (a hero-scale triangle count, thin island guttering). <see cref="Info"/> is provenance.</summary>
public enum ForgeSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>One structured finding from any stage of the validation ladder (WEAPON_GEN.md §7).
/// Codes are stable, machine-greppable slugs (e.g. "mesh.normal.zero", "m2.view.count") so the
/// validation report and downstream tooling can key on them rather than parsing prose.</summary>
public sealed record ForgeDiagnostic(
    ForgeSeverity Severity,
    string Code,
    string Message,
    string? Context = null)
{
    public override string ToString() =>
        $"[{Severity}] {Code}: {Message}" + (Context is null ? "" : $" ({Context})");
}

/// <summary>An append-only bag of diagnostics with a pass/fail verdict. Threaded through every
/// compiler stage so a single structured report can be emitted at the end.</summary>
public sealed class ForgeDiagnostics
{
    private readonly List<ForgeDiagnostic> _items = new();

    public IReadOnlyList<ForgeDiagnostic> Items => _items;

    /// <summary>The stage/label these diagnostics belong to (e.g. "input", "m2", "blp", "package").</summary>
    public string Stage { get; }

    public ForgeDiagnostics(string stage) => Stage = stage;

    public void Add(ForgeSeverity severity, string code, string message, string? context = null)
        => _items.Add(new ForgeDiagnostic(severity, code, message, context));

    public void Error(string code, string message, string? context = null)
        => Add(ForgeSeverity.Error, code, message, context);

    public void Warn(string code, string message, string? context = null)
        => Add(ForgeSeverity.Warning, code, message, context);

    public void Info(string code, string message, string? context = null)
        => Add(ForgeSeverity.Info, code, message, context);

    public void AddRange(ForgeDiagnostics other) => _items.AddRange(other._items);

    public bool HasErrors => _items.Any(d => d.Severity == ForgeSeverity.Error);
    public int ErrorCount => _items.Count(d => d.Severity == ForgeSeverity.Error);
    public int WarningCount => _items.Count(d => d.Severity == ForgeSeverity.Warning);

    /// <summary>The stage passes when it produced no errors. Warnings do not fail a stage.</summary>
    public bool Ok => !HasErrors;
}
