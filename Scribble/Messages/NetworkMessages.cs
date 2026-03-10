using CommunityToolkit.Mvvm.Messaging.Messages;

namespace Scribble.Messages;

// A special request message so MultiUserDrawing service can ask Canvas if it has unsaved work
public class HasEventsRequestMessage : RequestMessage<bool>
{
}