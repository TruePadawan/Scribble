using CommunityToolkit.Mvvm.ComponentModel;
using Scribble.Shared.Lib;

namespace Scribble.Services.MultiUserDrawing;

/// <summary>
/// Client-side wrapper around <see cref="Message"/> that adds display context
/// </summary>
public partial class ChatMessage(Message message, bool isOwnMessage = false) : ObservableObject
{
    public string Id => message.Id;
    public string DisplayName => message.DisplayName;
    public string Content => message.Content;
    public bool IsOwnMessage { get; } = isOwnMessage;
    [ObservableProperty] private bool _isSent = true;
}