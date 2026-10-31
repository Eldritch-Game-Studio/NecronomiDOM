namespace NecronomiDom
{
    /// <summary>Severity of an import diagnostic. Parse errors never change the tree a browser would build; they are warnings.</summary>
    public enum WebDiagnosticSeverity { Warning, Error }

    /// <summary>
    /// One markup or stylesheet diagnostic with its source position (markup and CSS profiles:
    /// parse errors are reported with file:line as warnings). Line and column are 1-based.
    /// </summary>
    public sealed record WebDiagnostic(WebDiagnosticSeverity Severity, string File, int Line, int Column, string Code, string Message)
    {
        public override string ToString() => $"{File}:{Line}:{Column}: {Severity.ToString().ToLowerInvariant()} {Code}: {Message}";
    }
}
