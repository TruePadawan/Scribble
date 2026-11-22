using Microsoft.AspNetCore.SignalR;
using Scribble.Shared.Lib;

namespace Scribble.Server.Hubs;

/// <summary>Owns temporary upload and snapshot buffers for exactly one SignalR connection.</summary>
internal sealed class RoomTransferState
{
    private MemoryStream? _upload;
    private string? _key;
    private Dictionary<string, byte[]> _images = [];
    private readonly Dictionary<string, byte[]> _parts = [];

    /// <summary>Checks the offset, quota and completed image checksum before making an upload available.</summary>
    internal void Upload(string key, int offset, byte[] data, bool last)
    {
        var metadata = key is "seed" or "edit";
        if (data.Length is 0 or > RoomProtocol.ChunkBytes ||
            (!metadata && (key.Length != 71 || !key.StartsWith("sha256:", StringComparison.Ordinal))))
        {
            throw new HubException("Invalid upload chunk.");
        }

        if (offset == 0)
        {
            _key = key;
            _upload = new MemoryStream();
        }

        if (_upload == null || key != _key || offset != _upload.Length)
        {
            throw new HubException("Invalid upload offset.");
        }

        // A retried upload replaces the staged copy of the same file.
        var staged = metadata
            ? 0
            : _images.Values.Sum(value => (long)value.Length) - (_images.GetValueOrDefault(key)?.Length ?? 0);
        if (staged + offset + data.Length > (metadata ? RoomProtocol.SnapshotBytes : RoomProtocol.AssetBytes))
        {
            throw new HubException(metadata ? "Room history exceeds 8 MiB." : "Room images exceed 32 MiB.");
        }

        _upload.Write(data);
        if (!last)
        {
            return;
        }

        var bytes = _upload.ToArray();
        _upload.Dispose();
        _upload = null;
        if (metadata)
        {
            _parts[key] = bytes;
        }
        else if (RoomAssets.Key(bytes) == key)
        {
            _images[key] = bytes;
        }
        else
        {
            throw new HubException("Image checksum mismatch.");
        }
    }

    /// <summary>Consumes staged images together with the seed or edit that claims them.</summary>
    internal Dictionary<string, byte[]> TakeImages()
    {
        var images = _images;
        _images = [];
        return images;
    }

    /// <summary>Takes a completed metadata upload once.</summary>
    internal byte[]? Take(string key) => _parts.Remove(key, out var bytes) ? bytes : null;

    /// <summary>Pins an immutable cut while newer edits continue to arrive.</summary>
    internal void BeginSnapshot(byte[] board, byte[] history, byte[] manifest)
    {
        _parts["board"] = board;
        _parts["history"] = history;
        _parts["manifest"] = manifest;
    }

    /// <summary>Returns a bounded slice, releasing finished snapshot sections from this connection.</summary>
    internal byte[] Read(string key, int offset, RoomRegistry rooms, string connection)
    {
        byte[]? bytes;
        var snapshot = key is "board" or "history" or "manifest";
        if (snapshot)
        {
            bytes = rooms.IsMember(connection) ? _parts.GetValueOrDefault(key) : null;
        }
        else
        {
            bytes = rooms.Asset(connection, key);
        }

        if (bytes == null || offset < 0 || offset >= bytes.Length)
        {
            throw new HubException("Room download is unavailable.");
        }

        var chunk = bytes.AsSpan(offset, Math.Min(RoomProtocol.ChunkBytes, bytes.Length - offset)).ToArray();
        if (snapshot && offset + chunk.Length == bytes.Length)
        {
            _parts.Remove(key);
        }

        return chunk;
    }
}