using System.Text;

namespace MangosSuperUI.Services;

/// <summary>Small, strict editor for the key/value format used by mangosd.conf.</summary>
public sealed class MangosdConfigDocument
{
    private readonly List<string> _lines;
    private readonly string _newline;

    private MangosdConfigDocument(List<string> lines, string newline)
    {
        _lines = lines;
        _newline = newline;
    }

    public static MangosdConfigDocument Parse(string text)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        return new MangosdConfigDocument(normalized.Split('\n').ToList(), newline);
    }

    public static async Task<MangosdConfigDocument> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Parse(await File.ReadAllTextAsync(path, cancellationToken));

    public string? Get(string key)
    {
        var matches = Find(key).ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException($"mangosd.conf contains more than one active '{key}' setting.");
        return matches.Length == 0 ? null : matches[0].Value;
    }

    public int? GetInt(string key)
    {
        var raw = Get(key);
        if (raw == null) return null;
        if (!int.TryParse(raw, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            throw new InvalidOperationException($"mangosd.conf setting '{key}' is not an integer.");
        return value;
    }

    public void Set(string key, string value)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Any(c => char.IsWhiteSpace(c) || c == '='))
            throw new ArgumentException("Invalid config key.", nameof(key));
        var matches = Find(key).ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException($"mangosd.conf contains more than one active '{key}' setting.");
        if (matches.Length == 0)
        {
            if (_lines.Count > 0 && _lines[^1].Length != 0) _lines.Add("");
            _lines.Add($"{key} = {value}");
        }
        else
        {
            var line = _lines[matches[0].Index];
            var indentLength = line.Length - line.TrimStart().Length;
            var indent = line[..indentLength];
            _lines[matches[0].Index] = $"{indent}{key} = {value}";
        }
    }

    public void ApplyWorldConfiguration(MangosSuperUI.Models.WorldLaunchConfiguration configuration)
    {
        var value = MangosSuperUI.Models.WorldConfigurationCatalog.NormalizeAndValidate(configuration);
        Set("PlayerLimit", value.PlayerLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set("PlayerHardLimit", value.PlayerHardLimit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set("LoginPerTick", value.LoginPerTick.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public override string ToString() => string.Join(_newline, _lines);

    public async Task SaveAtomicAsync(string path, CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException("The mangosd.conf path has no parent directory.");
        Directory.CreateDirectory(directory);
        var temp = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.worldstate-{Guid.NewGuid():N}.tmp");
        UnixFileMode? mode = null;
        try
        {
            if (!OperatingSystem.IsWindows() && File.Exists(fullPath)) mode = File.GetUnixFileMode(fullPath);
        }
        catch { }

        try
        {
            await File.WriteAllTextAsync(temp, ToString(), new UTF8Encoding(false), cancellationToken);
            if (mode.HasValue && !OperatingSystem.IsWindows()) File.SetUnixFileMode(temp, mode.Value);
            File.Move(temp, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private IEnumerable<(int Index, string Value)> Find(string key)
    {
        for (var i = 0; i < _lines.Count; i++)
        {
            var trimmed = _lines[i].TrimStart();
            if (trimmed.Length == 0 || trimmed[0] is '#' or ';') continue;
            var equals = trimmed.IndexOf('=');
            if (equals <= 0) continue;
            var candidate = trimmed[..equals].Trim();
            if (!string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase)) continue;
            var value = trimmed[(equals + 1)..].Trim();
            yield return (i, value);
        }
    }
}
