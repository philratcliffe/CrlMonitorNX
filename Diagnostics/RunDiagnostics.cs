using System.Collections.Concurrent;

namespace CrlMonitor.Diagnostics;

internal sealed class RunDiagnostics
{
    private readonly ConcurrentQueue<string> _stateWarnings = new();
    private readonly ConcurrentQueue<string> _signatureWarnings = new();
    private readonly ConcurrentQueue<string> _configurationWarnings = new();
    private readonly ConcurrentQueue<string> _runtimeWarnings = new();

    public IReadOnlyCollection<string> StateWarnings => this._stateWarnings;
    public IReadOnlyCollection<string> SignatureWarnings => this._signatureWarnings;
    public IReadOnlyCollection<string> ConfigurationWarnings => this._configurationWarnings;
    public IReadOnlyCollection<string> RuntimeWarnings => this._runtimeWarnings;

    public void AddStateWarning(string message)
    {
        Enqueue(this._stateWarnings, message);
    }

    public void AddSignatureWarning(string message)
    {
        Enqueue(this._signatureWarnings, message);
    }

    public void AddConfigurationWarning(string message)
    {
        Enqueue(this._configurationWarnings, message);
    }

    public void AddRuntimeWarning(string message)
    {
        Enqueue(this._runtimeWarnings, message);
    }

    private static void Enqueue(ConcurrentQueue<string> queue, string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            queue.Enqueue(message);
        }
    }
}
