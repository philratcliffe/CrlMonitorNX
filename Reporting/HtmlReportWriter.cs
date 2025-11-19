using System.Globalization;
using System.Text;
using CrlMonitor.Licensing;
using CrlMonitor.Models;
using Standard.Licensing;

namespace CrlMonitor.Reporting;

internal static class HtmlReportWriter
{
    public static async Task WriteAsync(string path, CrlCheckRun run, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(run);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _ = Directory.CreateDirectory(directory);
        }

        var html = BuildHtml(run);
        await File.WriteAllTextAsync(path, html, Encoding.UTF8, cancellationToken).ConfigureAwait(false);

        // Copy favicon to same directory as report
        await CopyFaviconAsync(directory, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyFaviconAsync(string? targetDirectory, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(targetDirectory))
        {
            return;
        }

        var sourceFavicon = Path.Combine(AppContext.BaseDirectory, "Reporting", "favicon.png");
        var targetFavicon = Path.Combine(targetDirectory, "crlmonitor-favicon.png");

        if (File.Exists(sourceFavicon))
        {
            await Task.Run(() => File.Copy(sourceFavicon, targetFavicon, overwrite: true), cancellationToken).ConfigureAwait(false);
        }
    }

    private static string BuildHtml(CrlCheckRun run)
    {
        var summary = BuildSummary(run.Results);
        var builder = new StringBuilder();
        _ = builder.AppendLine("<!DOCTYPE html>");
        _ = builder.AppendLine("<html lang=\"en\"><head>");
        _ = builder.AppendLine("<meta charset=\"utf-8\" />");
        _ = builder.AppendLine("<link rel=\"icon\" type=\"image/png\" href=\"data:image/png;base64,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7FLCePP/nwdn7tvY8+Vn5r+HS5QRYhf6P/vP0/d7cd/79//8Xxo//2LM53v2Y/bfh/P8X+59yeX/unPc/Xv+/H0PvP+D/we/V+M5p91o/n9ZqOnvrp//xb++IQjOXH/gO+7/Y46pS/l/h1sFjWqxf48P9s9z93rOe+2/p9nvo/df8DX76/f+Hn+/f/+D/7vdlw9/fQN/53/O/b/z/+/nO/y/P/r/j/y/m/v3h/J/5+D/Lf67/X/H/Xu+/+f/zvn/7/y/8/8f/P/7+P8P/v/D/7+Q/3/w/x/8/wf//8H/f/D/H/z/B///wf9/8P8f/P8H//9f/P+H/v/D/3/w/x/8/8dQrjHwfy/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f+j/P/z/h/7/w/9/6P8//P+H/v/D/3/o/z/8/4f+/8P/f==\">");
        _ = builder.AppendLine("<title>CRL Health Report — CrlMonitor</title>");
        _ = builder.AppendLine("<style>");
        _ = builder.AppendLine("body{font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,sans-serif;background:#f5f7fb;color:#1f2937;margin:0;padding:0;}");
        _ = builder.AppendLine(".container{max-width:1200px;margin:0 auto;padding:32px;}");
        _ = builder.AppendLine(".card{background:#fff;border-radius:16px;box-shadow:0 10px 30px rgba(15,23,42,.1);padding:32px;margin-bottom:32px;}");
        _ = builder.AppendLine(".summary-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(135px,1fr));gap:16px;}");
        _ = builder.AppendLine(".summary-card{padding:16px;border-radius:12px;background:#f9fafb;border:1px solid #e5e7eb;}");
        _ = builder.AppendLine(".summary-label{font-size:14px;color:#6b7280;text-transform:uppercase;letter-spacing:.05em;}");
        _ = builder.AppendLine(".summary-value{font-size:28px;font-weight:600;color:#111827;margin-top:4px;}");
        _ = builder.AppendLine(".header-divider{border-bottom:1px solid #e5e7eb;margin:12px 0 24px 0;}");
        _ = builder.AppendLine(".report-meta{color:#6b7280;font-size:14px;margin-bottom:20px;}");
        _ = builder.AppendLine(".table-wrapper{overflow-x:auto;}");
        _ = builder.AppendLine("table{width:100%;border-collapse:collapse;margin-top:16px;font-size:14px;}");
        _ = builder.AppendLine("th{background:#111827;color:#f9fafb;text-align:left;padding:12px;border-bottom:2px solid #0f172a;}");
        _ = builder.AppendLine("td{padding:12px;border-bottom:1px solid #e5e7eb;line-height:1.4;}");
        _ = builder.AppendLine("td:last-child,th:last-child{min-width:220px;}");
        _ = builder.AppendLine("tr:nth-child(even){background:#f9fafb;}");
        _ = builder.AppendLine(".status-OK{color:#16a34a;font-weight:600;}");
        _ = builder.AppendLine(".status-WARNING,.status-EXPIRING{color:#f97316;font-weight:600;}");
        _ = builder.AppendLine(".status-EXPIRED,.status-ERROR{color:#dc2626;font-weight:600;}");
        _ = builder.AppendLine(".uri-toggle{color:#2563eb;text-decoration:none;font-size:12px;margin-left:4px;}");
        _ = builder.AppendLine(".uri-toggle:hover{text-decoration:underline;}");
        _ = builder.AppendLine(".uri-full{white-space:nowrap;margin-left:4px;}");
        _ = builder.AppendLine(".issuer{word-break:keep-all;overflow-wrap:normal;}");
        _ = builder.AppendLine(".dt{white-space:nowrap;}");
        _ = builder.AppendLine("</style>");
        _ = builder.AppendLine("<script>");
        _ = builder.AppendLine("function toggleUri(id){var full=document.getElementById(id+'-full');var short=document.getElementById(id+'-short');var link=document.getElementById(id+'-link');if(full.style.display==='none'){full.style.display='inline';short.style.display='none';link.textContent='(hide)';}else{full.style.display='none';short.style.display='inline';link.textContent='(show)';}}");
        _ = builder.AppendLine("</script>");
        _ = builder.AppendLine("</head><body>");
        _ = builder.AppendLine("<div class=\"container\">");
        _ = builder.AppendLine("<div class=\"card\">");
        _ = builder.AppendLine("<h1>CRL Health Report</h1>");
        _ = builder.AppendLine("<div class=\"header-divider\"></div>");
        var licenseInfo = GetLicenseInfo();
        _ = builder.AppendLine(FormattableString.Invariant($"<p class=\"report-meta\">Generated: {TimeFormatter.FormatUtc(run.GeneratedAtUtc)} &middot; {licenseInfo}</p>"));
        _ = builder.AppendLine("<div class=\"summary-grid\">");
        AppendSummaryCard(builder, "CRL Errors", summary.Errors, summary.Errors > 0 ? "#dc2626" : null);
        AppendSummaryCard(builder, "CRLs Expired", summary.Expired, summary.Expired > 0 ? "#dc2626" : null);
        AppendSummaryCard(builder, "CRLs Warning", summary.Warning, null);
        AppendSummaryCard(builder, "CRLs Expiring", summary.Expiring, null);
        AppendSummaryCard(builder, "CRLs OK", summary.Ok, null);
        AppendSummaryCard(builder, "CRLs Checked", summary.Total, null);
        _ = builder.AppendLine("</div></div>");
        _ = builder.AppendLine("<div class=\"card table-wrapper\">");
        _ = builder.AppendLine("<table><thead><tr>");
        _ = builder.AppendLine("<th>URI</th><th>Issuer</th><th>Status</th><th>This Update (UTC)</th><th>Next Update (UTC)</th><th>CRL Size</th><th>Download (ms)</th><th>Signature</th><th>Revocations</th><th>Checked (UTC)</th><th>Previous (UTC)</th><th>Type</th><th>Details</th>");
        _ = builder.AppendLine("</tr></thead><tbody>");

        // Sort by status priority: ERROR, EXPIRED, EXPIRING, WARNING, OK
        var sortedResults = run.Results.OrderBy(r => r.Status switch {
            CrlStatus.Error => 0,
            CrlStatus.Expired => 1,
            CrlStatus.Expiring => 2,
            CrlStatus.Warning => 3,
            CrlStatus.Ok => 4,
            _ => 5
        }).ToList();

        for (var i = 0; i < sortedResults.Count; i++)
        {
            AppendRow(builder, sortedResults[i], i);
        }
        _ = builder.AppendLine("</tbody></table></div></div>");

        if (LicenseBootstrapper.ValidatedLicense?.Type == LicenseType.Trial)
        {
            var requestCode = LicenseBootstrapper.CreateRequestCode();
            _ = builder.AppendLine(FormattableString.Invariant($"<div style=\"text-align:center;color:#374151;font-size:13px;margin-top:24px;\">You are using a trial license. To upgrade, please email <a href=\"mailto:sales@redkestrel.co.uk\">sales@redkestrel.co.uk</a> with your request code: {requestCode}</div>"));
        }

        _ = builder.AppendLine(FormattableString.Invariant($"<footer style=\"text-align:center;color:#6b7280;font-size:12px;margin-top:32px;\">Generated by CRL Monitor {GetVersion()} — © {DateTime.UtcNow:yyyy} Red Kestrel Consulting Limited</footer>"));
        _ = builder.AppendLine("</div></body></html>");
        return builder.ToString();
    }

    private static string GetLicenseInfo()
    {
        var license = LicenseBootstrapper.ValidatedLicense;
        if (license == null)
        {
            return string.Empty;
        }

        if (license.Type == LicenseType.Trial)
        {
            // For trials, only show days remaining (not license expiration)
            // License expiration is defense-in-depth and may not match trial period
            var trialStatus = LicenseBootstrapper.TrialStatus;
            if (trialStatus != null)
            {
                return FormattableString.Invariant($"(License: Trial — {trialStatus.DaysRemaining} days remaining)");
            }
        }
        else
        {
            // For Standard licenses, show expiration date
            var expiryDate = license.Expiration.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return FormattableString.Invariant($"(License: Standard — expires {expiryDate})");
        }

        return string.Empty;
    }

    private static void AppendSummaryCard(StringBuilder builder, string label, int value, string? color)
    {
        var valueStyle = string.IsNullOrWhiteSpace(color) ? "summary-value" : $"summary-value\" style=\"color:{color}";
        _ = builder.AppendLine("<div class=\"summary-card\">");
        _ = builder.AppendLine(FormattableString.Invariant($"<div class=\"summary-label\">{label}</div>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<div class=\"{valueStyle}\">{value}</div>"));
        _ = builder.AppendLine("</div>");
    }

    private static void AppendRow(StringBuilder builder, CrlCheckResult result, int rowIndex)
    {
        var parsed = result.ParsedCrl;
        var statusClass = $"status-{result.Status.ToDisplayString()}";
        var rowClass = result.Status is CrlStatus.Error or CrlStatus.Expired ? " class=\"row-" + result.Status.ToDisplayString() + "\"" : string.Empty;
        _ = builder.AppendLine("<tr" + rowClass + ">");

        // Collapsible URI
        var fullUri = result.Uri.ToString();
        var escapedUri = Escape(fullUri);
        const int maxUriLength = 40;
        if (fullUri.Length > maxUriLength)
        {
            var truncated = Escape(fullUri[0..maxUriLength] + "...");
            var uriId = FormattableString.Invariant($"uri{rowIndex}");
            _ = builder.Append(FormattableString.Invariant($"<td><span id=\"{uriId}-short\" class=\"uri-short\">"));
            _ = builder.Append(truncated);
            _ = builder.Append("</span>&nbsp;");
            _ = builder.Append(FormattableString.Invariant($"<a href=\"#\" class=\"uri-toggle\" id=\"{uriId}-link\" onclick=\"toggleUri('{uriId}'); return false;\">(show)</a>"));
            _ = builder.Append(FormattableString.Invariant($"<span id=\"{uriId}-full\" class=\"uri-full\" style=\"display:none;\">{escapedUri}</span>"));
            _ = builder.AppendLine("</td>");
        }
        else
        {
            _ = builder.AppendLine(FormattableString.Invariant($"<td>{escapedUri}</td>"));
        }
        _ = builder.AppendLine(FormattableString.Invariant($"<td class=\"issuer\">{ProtectHyphens(Escape(parsed?.Issuer ?? string.Empty))}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td class=\"{statusClass}\">{Escape(result.Status.ToDisplayString())}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td class=\"dt\">{FormatDate(parsed?.ThisUpdate)}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td class=\"dt\">{FormatDate(parsed?.NextUpdate)}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{result.ContentLength?.ToString(CultureInfo.InvariantCulture) ?? string.Empty}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{result.DownloadDuration?.TotalMilliseconds.ToString("F0", CultureInfo.InvariantCulture) ?? string.Empty}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{Escape(CsvReportFormatter.NormalizeSignatureStatus(result.SignatureStatus))}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{parsed?.RevokedSerialNumbers?.Count.ToString(CultureInfo.InvariantCulture) ?? string.Empty}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td class=\"dt\">{FormatDate(result.CheckedAtUtc)}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td class=\"dt\">{FormatDate(result.PreviousFetchUtc)}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{Escape(parsed == null ? string.Empty : parsed.IsDelta ? "Delta" : "Full")}</td>"));
        _ = builder.AppendLine(FormattableString.Invariant($"<td>{Escape(result.ErrorInfo ?? string.Empty)}</td>"));
        _ = builder.AppendLine("</tr>");
    }

    private static string FormatDate(DateTime? value)
    {
        var formatted = TimeFormatter.FormatUtc(value);
        if (string.IsNullOrEmpty(formatted))
        {
            return string.Empty;
        }

        // Split date and time onto separate lines (yyyy-MM-dd<br>HH:mm:ssZ)
        var spaceIndex = formatted.IndexOf(' ', StringComparison.Ordinal);
        return spaceIndex > 0
            ? formatted[0..spaceIndex] + "<br>" + formatted[(spaceIndex + 1)..]
            : formatted;
    }

    private static Summary BuildSummary(IReadOnlyList<CrlCheckResult> results)
    {
        return new Summary(
            results.Count,
            results.Count(r => r.Status == CrlStatus.Ok),
            results.Count(r => r.Status == CrlStatus.Warning),
            results.Count(r => r.Status == CrlStatus.Expiring),
            results.Count(r => r.Status == CrlStatus.Expired),
            results.Count(r => r.Status == CrlStatus.Error));
    }

    private static string Escape(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value);
    }

    private static string ProtectHyphens(string value)
    {
        // Replace regular hyphen with non-breaking hyphen (U+2011)
        // Prevents line breaks inside hyphenated names like "GlobalSign nv-sa"
        return value.Replace("-", "&#8209;", StringComparison.Ordinal);
    }

    private static string GetVersion()
    {
        var version = typeof(HtmlReportWriter).Assembly.GetName().Version;
        if (version == null)
        {
            return "v1.0.0";
        }

        var build = version.Build >= 0 ? version.Build : 0;
        return FormattableString.Invariant($"v{version.Major}.{version.Minor}.{build}");
    }

    private readonly record struct Summary(int Total, int Ok, int Warning, int Expiring, int Expired, int Errors);
}
