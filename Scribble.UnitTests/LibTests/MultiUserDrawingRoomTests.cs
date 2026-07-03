using FluentAssertions;
using Scribble.Services.MultiUserDrawing;
using Scribble.Shared.Lib;

namespace Scribble.UnitTests.LibTests;

public class MultiUserDrawingRoomTests
{
    private static MultiUserDrawingClient Client(string id) =>
        new(id, $"User-{id}");

    // IsHost
    [Fact]
    public void IsHost_EmptyClientList_ReturnsFalse()
    {
        var room = new MultiUserDrawingRoom("room1", "conn-A", "Alice")
        {
            Clients = []
        };

        room.IsHost.Should().BeFalse();
    }

    [Fact]
    public void IsHost_ClientIsFirstInList_ReturnsTrue()
    {
        var room = new MultiUserDrawingRoom("room1", "conn-A", "Alice")
        {
            Clients = [Client("conn-A"), Client("conn-B")]
        };

        room.IsHost.Should().BeTrue();
    }

    [Fact]
    public void IsHost_ClientIsNotFirstInList_ReturnsFalse()
    {
        var room = new MultiUserDrawingRoom("room1", "conn-B", "Bob")
        {
            Clients = [Client("conn-A"), Client("conn-B")]
        };

        room.IsHost.Should().BeFalse();
    }

    [Fact]
    public void IsHost_SingleClientAndItIsMe_ReturnsTrue()
    {
        var room = new MultiUserDrawingRoom("room1", "conn-A", "Alice")
        {
            Clients = [Client("conn-A")]
        };

        room.IsHost.Should().BeTrue();
    }
}
