namespace AetherGlot.Helpers;

/// <summary>
/// Sends a chat message to an FFXIV chat channel on behalf of the local player.
/// The concrete implementation uses FFXIVClientStructs; tests use a mock.
/// </summary>
public interface IChatSender
{
    /// <summary>
    /// Queues <paramref name="text"/> for delivery to <paramref name="channel"/>.
    /// Must be called from or marshalled to the game's framework thread.
    /// </summary>
    void SendMessage(XivChatType channel, string text);
}
