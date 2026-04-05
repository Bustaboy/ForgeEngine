using System.Diagnostics;
using System.Text;

namespace GameForge.Editor.EditorDiagnostics;

/// <summary>Forwards <see cref="Trace"/> output (including Avalonia <c>LogToTrace()</c>) into <see cref="EditorDiagnosticsLog"/>.</summary>
internal sealed class EditorDiagnosticsTraceListener : TraceListener
{
    private readonly object _sync = new();
    private readonly StringBuilder _lineBuffer = new();

    public EditorDiagnosticsTraceListener()
        : base(nameof(EditorDiagnosticsTraceListener))
    {
    }

    public override void Write(string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        lock (_sync)
        {
            _lineBuffer.Append(message);
        }
    }

    public override void WriteLine(string? message)
    {
        string combined;
        lock (_sync)
        {
            _lineBuffer.Append(message);
            combined = _lineBuffer.ToString();
            _lineBuffer.Clear();
        }

        if (string.IsNullOrWhiteSpace(combined))
        {
            return;
        }

        EditorDiagnosticsLog.LogInfo($"[avalonia-trace] {combined}");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_sync)
            {
                if (_lineBuffer.Length > 0)
                {
                    var rest = _lineBuffer.ToString();
                    _lineBuffer.Clear();
                    if (!string.IsNullOrWhiteSpace(rest))
                    {
                        EditorDiagnosticsLog.LogInfo($"[avalonia-trace] {rest}");
                    }
                }
            }
        }

        base.Dispose(disposing);
    }
}
