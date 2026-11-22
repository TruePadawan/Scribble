using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR.Client;
using Scribble.Shared.Lib;
using Scribble.Shared.Lib.CanvasElements;
using Scribble.Shared.Utils;
using SkiaSharp;

namespace Scribble.Services.MultiUserDrawing;

/// <summary>Transfers one connection's frozen snapshot and caches its distinct image sources.</summary>
internal sealed class RoomTransfer(HubConnection connection)
{
    private readonly ConcurrentDictionary<string, string> _images = new();
    private readonly Dictionary<string, string> _sourceKeys = [];
    private readonly SemaphoreSlim _download = new(1);

    /// <summary>Uploads each new image before sending events that reference it.</summary>
    internal async Task<List<Event>> EncodeAsync(IEnumerable<Event> events, bool seed)
    {
        var uploads = new Dictionary<string, byte[]>();
        long stagedBytes = 0;
        var edits = events.Select(edit => RoomAssets.Map(edit, source =>
        {
            if (_sourceKeys.TryGetValue(source, out var existing))
            {
                return existing;
            }

            if (source.Length > 4 * ((RoomProtocol.AssetBytes + 2) / 3))
            {
                throw new InvalidOperationException("Room images exceed 32 MiB.");
            }

            var bytes = Convert.FromBase64String(source);
            var key = RoomAssets.Key(bytes);
            if (uploads.TryAdd(key, bytes))
            {
                stagedBytes += bytes.Length;
                if (stagedBytes > RoomProtocol.AssetBytes)
                {
                    throw new InvalidOperationException("Room images exceed 32 MiB.");
                }
            }

            _sourceKeys[source] = key;
            _images[key] = source;
            return key;
        })).ToList();
        foreach (var (key, bytes) in uploads)
        {
            if (seed || !await connection.InvokeAsync<bool>("HasImage", key))
            {
                await UploadAsync(key, bytes);
            }
        }

        return edits;
    }

    /// <summary>Sends a file in sequential chunks that fit the SignalR envelope.</summary>
    internal async Task UploadAsync(string key, byte[] bytes)
    {
        for (var offset = 0; offset < bytes.Length; offset += RoomProtocol.ChunkBytes)
        {
            var length = Math.Min(RoomProtocol.ChunkBytes, bytes.Length - offset);
            await connection.InvokeAsync("UploadChunk", key, offset, bytes.AsSpan(offset, length).ToArray(),
                offset + length == bytes.Length);
        }
    }

    /// <summary>Collects bounded slices and reports progress after each response.</summary>
    private async Task<byte[]> ReadAsync(string key, int size, Func<int, Task>? progress)
    {
        var bytes = new byte[size];
        for (var offset = 0; offset < size;)
        {
            var chunk = await connection.InvokeAsync<byte[]>("ReadChunk", key, offset);
            if (chunk.Length != Math.Min(RoomProtocol.ChunkBytes, size - offset))
            {
                throw new InvalidOperationException("Incomplete room download.");
            }

            chunk.CopyTo(bytes, offset);
            offset += chunk.Length;
            if (progress != null)
            {
                await progress(chunk.Length);
            }
        }

        return bytes;
    }

    /// <summary>Shares a single checksum-verified image between live edits and the joining board.</summary>
    private async Task FetchAsync(string key, int? size = null, Func<int, Task>? progress = null)
    {
        await _download.WaitAsync();
        try
        {
            if (_images.ContainsKey(key))
            {
                if (size.HasValue && progress != null)
                {
                    await progress(size.Value);
                }

                return;
            }

            var length = size ?? await connection.InvokeAsync<int>("ImageSize", key);
            var bytes = await ReadAsync(key, length, progress);
            if (RoomAssets.Key(bytes) != key)
            {
                throw new InvalidOperationException("Room image checksum mismatch.");
            }

            _images[key] = Convert.ToBase64String(bytes);
        }
        finally
        {
            _download.Release();
        }
    }

    /// <summary>Reads a large live edit in bounded slices.</summary>
    internal async Task<Event> ReadEventAsync(Guid id, int bytes) =>
        JsonSerializer.Deserialize<Event>(await ReadAsync("event:" + id, bytes, null))!;

    /// <summary>Sends regular edits directly and stages larger metadata before publication.</summary>
    internal async Task<long?> SendAsync(string roomId, Event edit)
    {
        var wire = (await EncodeAsync([edit], false))[0];
        var bytes = JsonSerializer.SerializeToUtf8Bytes(wire);
        if (bytes.Length <= RoomProtocol.ChunkBytes)
        {
            return await connection.InvokeAsync<long?>("SendEvent", roomId, wire);
        }

        await UploadAsync("edit", bytes);
        return await connection.InvokeAsync<long?>("SendUploadedEvent", roomId);
    }

    /// <summary>Resolves every image referenced by a newly received live edit.</summary>
    internal async Task<Event> DecodeAsync(Event edit)
    {
        foreach (var key in RoomAssets.Sources(edit).Distinct())
        {
            await FetchAsync(key);
        }

        return RoomAssets.Map(edit, key => _images[key]);
    }

    /// <summary>Stages visible placements first, then resolves the remaining undo history.</summary>
    internal async Task<Queue<Event>> DownloadAsync(RoomAdmission admission, SKRect? viewport, Func<int, Task> progress,
        Func<List<CanvasElement>, Task> preview)
    {
        if (!admission.Seeded)
        {
            // Another host may have created the room while our seed was uploading.
            _sourceKeys.Clear();
        }

        var assets = JsonSerializer.Deserialize<Dictionary<string, int>>(
            await ReadAsync("manifest", admission.ManifestBytes, progress))!;
        var board = JsonSerializer.Deserialize<List<CanvasElement>>(await ReadAsync("board", admission.BoardBytes,
            progress))!;
        await preview(board);
        // Keep resolved placements (and their decoded pixels) when another asset arrives.
        var placements = board.Select((element, index) => (element, index))
            .Where(item => item.element is CanvasImage)
            .ToLookup(item => ((CanvasImage)item.element).ImageBase64String);
        var ordered = placements.OrderByDescending(group => viewport is { } rect &&
                                                            group.Any(item =>
                                                                CanvasGeometryUtilities
                                                                    .GetImageWorldBounds((CanvasImage)item.element)
                                                                    .IntersectsWith(rect)));
        foreach (var group in ordered)
        {
            await FetchAsync(group.Key, assets[group.Key], progress);
            board = [.. board];
            foreach (var (element, index) in group)
            {
                board[index] = RoomAssets.WithSource((CanvasImage)element, _images[group.Key]);
            }

            await preview(board);
        }

        foreach (var key in assets.Keys.Where(key => !placements.Contains(key)))
        {
            await FetchAsync(key, assets[key], progress);
        }

        var history =
            JsonSerializer.Deserialize<List<Event>>(await ReadAsync("history", admission.HistoryBytes, progress))!;
        return new Queue<Event>(history.Select(edit => RoomAssets.Map(edit, key => _images[key])));
    }
}