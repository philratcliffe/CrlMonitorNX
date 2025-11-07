using System.Collections.Concurrent;
using System.Collections.Generic;

namespace CrlMonitor.Diagnostics;

internal sealed class RunDiagnostics
{
    private readonly ConcurrentQueue<string> _stateWarnings = new();
    private readonly ConcurrentQueue<string> _signatureWarnings = new();
    private readonly ConcurrentQueue<string> _configurationWarnings = new();
    private readonly ConcurrentQueue<string> _runtimeWarnings = new();

    public IReadOnlyCollection<string> StateWarnings => _stateWarnings;
    public IReadOnlyCollection<string> SignatureWarnings => _signatureWarnings;
    public IReadOnlyCollection<string> ConfigurationWarnings => _configurationWarnings;
    public IReadOnlyCollection<string> RuntimeWarnings => _runtimeWarnings;

    public void AddStateWarning(string message) => Enqueue(_stateWarnings, message);
    public void AddSignatureWarning(string message) => Enqueue(_signatureWarnings, message);
    public void AddConfigurationWarning(string message) => Enqueue(_configurationWarnings, message);
    public void AddRuntimeWarning(string message) => Enqueue(_runtimeWarnings, message);

    private static void Enqueue(ConcurrentQueue<string> queue, string message)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            queue.Enqueue(message);
        }
    }
}
