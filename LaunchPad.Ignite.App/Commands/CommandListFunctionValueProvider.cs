using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using LaunchPad.Ignite.Core.Execution;
using LaunchPad.Ignite.Core.Lists;
using LaunchPad.Ignite.Core.Schema;

namespace LaunchPad.Ignite.App.Commands;

public sealed class CommandListFunctionValueProvider : ICommandListFunctionValueProvider
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromSeconds(20);
    private readonly IReadOnlyDictionary<string, CommandFunctionDefinition> _functions;
    private readonly IReadOnlyDictionary<string, IExtensionListFunctionExecutor> _executors;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, byte> _inFlight = new(StringComparer.Ordinal);

    public CommandListFunctionValueProvider(
        IEnumerable<CommandFunctionDefinition> functions,
        IReadOnlyDictionary<string, IExtensionListFunctionExecutor> executors)
    {
        _functions = functions
            .Where(function => function.ReturnType == CommandFunctionReturnType.List)
            .ToDictionary(function => function.QualifiedName, StringComparer.OrdinalIgnoreCase);
        _executors = executors;
    }

    public event EventHandler? ValuesChanged;

    public IReadOnlyList<CommandListItem> GetOrStart(string functionName, IReadOnlyList<string> arguments)
    {
        if (!_functions.TryGetValue(functionName, out var function)
            || !_executors.TryGetValue(function.QualifiedName, out var executor))
        {
            return [];
        }

        var cacheKey = JsonSerializer.Serialize(new CacheKey(function.QualifiedName, arguments));
        var now = DateTimeOffset.UtcNow;
        var hasCached = _cache.TryGetValue(cacheKey, out var cached);
        if (hasCached && cached!.ExpiresAt > now)
        {
            return cached.Items;
        }

        if (_inFlight.TryAdd(cacheKey, 0))
        {
            _ = RefreshAsync(cacheKey, function, executor, arguments, hasCached ? cached : null);
        }

        return hasCached ? cached!.Items : [];
    }

    private async Task RefreshAsync(
        string cacheKey,
        CommandFunctionDefinition function,
        IExtensionListFunctionExecutor executor,
        IReadOnlyList<string> arguments,
        CacheEntry? previous)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(function.TimeoutSeconds));
            var items = await executor.ExecuteListAsync(function, arguments, timeout.Token);
            var uniqueItems = items
                .Where(item => !string.IsNullOrWhiteSpace(item.Value))
                .DistinctBy(item => item.Value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            _cache[cacheKey] = new CacheEntry(uniqueItems, DateTimeOffset.UtcNow + CacheDuration);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _cache[cacheKey] = new CacheEntry(
                previous?.Items ?? [],
                DateTimeOffset.UtcNow + FailureCacheDuration);
        }
        finally
        {
            _inFlight.TryRemove(cacheKey, out _);
            ValuesChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private sealed record CacheKey(string FunctionName, IReadOnlyList<string> Arguments);

    private sealed record CacheEntry(IReadOnlyList<CommandListItem> Items, DateTimeOffset ExpiresAt);
}
