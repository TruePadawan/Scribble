namespace Scribble.Shared.Lib;

public record MultiUserDrawingClient(string ConnectionId, string Name)
{
    public string ParticipantId { get; init; } = ConnectionId;
}