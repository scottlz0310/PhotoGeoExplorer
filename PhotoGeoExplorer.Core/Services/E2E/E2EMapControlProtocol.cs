using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PhotoGeoExplorer.Services.MapRendering;

namespace PhotoGeoExplorer.Services.E2E;

/// <summary>
/// 名前付きパイプ上でやり取りする E2E 地図操作コマンド（1 行 1 JSON）の解析と実行。
/// </summary>
internal sealed class E2EMapControlProtocol
{
    internal const int MaxRequestLineLength = 4096;

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly IMapViewController _controller;
    private readonly Action<string> _logInfo;
    private readonly Action<string> _logError;

    internal E2EMapControlProtocol(
        IMapViewController controller,
        Action<string>? logInfo = null,
        Action<string>? logError = null)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
        _logInfo = logInfo ?? (_ => { });
        _logError = logError ?? (_ => { });
    }

    internal async Task<string> HandleLineAsync(string? line, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return SerializeError(null, "Request line is empty.");
        }

        if (line.Length > MaxRequestLineLength)
        {
            _logError($"[E2E][Control] Rejected oversized request ({line.Length} chars).");
            return SerializeError(null, $"Request exceeds maximum length of {MaxRequestLineLength} characters.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException ex)
        {
            _logError($"[E2E][Control] Rejected invalid JSON request: {ex.Message}");
            return SerializeError(null, $"Invalid JSON: {ex.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                _logError("[E2E][Control] Rejected non-object JSON request.");
                return SerializeError(null, "Request must be a JSON object.");
            }

            if (!TryGetPropertyCaseInsensitive(document.RootElement, "command", out var commandElement)
                || commandElement.ValueKind != JsonValueKind.String)
            {
                _logError("[E2E][Control] Rejected request without a string 'command' field.");
                return SerializeError(null, "Missing or invalid 'command' field.");
            }

            var command = commandElement.GetString()?.Trim();
            if (string.IsNullOrEmpty(command))
            {
                _logError("[E2E][Control] Rejected request with empty 'command' field.");
                return SerializeError(null, "Missing or invalid 'command' field.");
            }

            if (string.Equals(command, "status", StringComparison.OrdinalIgnoreCase))
            {
                var status = _controller.CurrentStatus;
                _logInfo($"[E2E][Control] command=status hasValidViewport={status.HasValidViewport} isBusy={status.IsBusy} renderedSeq={status.RenderedSeq}");
                return SerializeSuccess("status", status);
            }

            if (string.Equals(command, "view", StringComparison.OrdinalIgnoreCase))
            {
                return await HandleViewCommandAsync(document.RootElement, cancellationToken).ConfigureAwait(false);
            }

            _logError($"[E2E][Control] Rejected unknown command: '{command}'.");
            return SerializeError(command, $"Unknown command '{command}'. Supported commands: status, view.");
        }
    }

    private async Task<string> HandleViewCommandAsync(JsonElement root, CancellationToken cancellationToken)
    {
        if (!TryGetFiniteDouble(root, "latitude", out var latitude, out var latError))
        {
            _logError($"[E2E][Control] command=view rejected: {latError}");
            return SerializeError("view", latError);
        }

        if (!TryGetFiniteDouble(root, "longitude", out var longitude, out var lonError))
        {
            _logError($"[E2E][Control] command=view rejected: {lonError}");
            return SerializeError("view", lonError);
        }

        if (!TryGetFiniteDouble(root, "zoom", out var zoom, out var zoomError))
        {
            _logError($"[E2E][Control] command=view rejected: {zoomError}");
            return SerializeError("view", zoomError);
        }

        if (!MapViewTarget.TryCreate(latitude, longitude, zoom, out var target, out var validationError))
        {
            _logError($"[E2E][Control] command=view rejected: {validationError}");
            return SerializeError("view", validationError);
        }

        var result = await _controller.SetViewAsync(target, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded)
        {
            var error = result.Error ?? "Failed to update map view.";
            _logError($"[E2E][Control] command=view ({target.Latitude}, {target.Longitude}, z={target.Zoom}) failed: {error}");
            return SerializeError("view", error);
        }

        _logInfo($"[E2E][Control] command=view ({target.Latitude}, {target.Longitude}, z={target.Zoom}) succeeded. isBusy={result.Status.IsBusy} renderedSeq={result.Status.RenderedSeq}");
        return SerializeSuccess("view", result.Status);
    }

    private static bool TryGetFiniteDouble(JsonElement root, string name, out double value, out string error)
    {
        value = 0;
        if (!TryGetPropertyCaseInsensitive(root, name, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out value)
            || !double.IsFinite(value))
        {
            error = $"Missing or invalid numeric field '{name}'.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    private static bool TryGetPropertyCaseInsensitive(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string SerializeSuccess(string command, MapViewStatus status)
        => JsonSerializer.Serialize(new ResponsePayload(true, command, status, null), ResponseJsonOptions);

    private static string SerializeError(string? command, string error)
        => JsonSerializer.Serialize(new ResponsePayload(false, command, null, error), ResponseJsonOptions);

    private sealed record ResponsePayload(
        bool Ok,
        string? Command,
        MapViewStatus? Status,
        string? Error);
}
