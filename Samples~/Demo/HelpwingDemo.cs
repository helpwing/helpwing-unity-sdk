using Helpwing;
using Helpwing.UI;
using UnityEngine;

/// <summary>
/// Drop on an empty GameObject in any scene: sets up the chat from code and names a signed-in player.
/// Replace the project key with your own (Settings → Chat widget in the dashboard).
/// </summary>
public sealed class HelpwingDemo : MonoBehaviour
{
    [SerializeField] private string apiUrl = "https://api.helpwing.app";
    [SerializeField] private string projectKey = "pk_live_...";
    [Tooltip("Pretend a player is signed in. The hash must come from your server.")]
    [SerializeField] private string playerId = "";
    [SerializeField] private string playerHash = "";

    private HelpwingClient client;

    private void Start()
    {
        client = HelpwingClient.Instance != null ? HelpwingClient.Instance : HelpwingClient.Create(apiUrl, projectKey);
        if (!string.IsNullOrEmpty(playerId))
        {
            _ = client.Identify(new Identity { Id = playerId, Name = "Player " + playerId, UserHash = playerHash });
        }

        // Labels, fonts and colours are inspector fields; add SupportUI in the editor to set them.
        gameObject.AddComponent<SupportUI>();
        client.StateChanged += state => Debug.Log($"[Helpwing demo] {state.Status}, {state.Messages.Count} messages, {state.UnreadCount} unread");
    }

    private void OnGUI()
    {
        // A "Help" row in a settings menu would call Open() the same way.
        if (client != null && GUI.Button(new Rect(16, 16, 160, 40), $"Support ({client.State.UnreadCount})")) client.Open();
    }
}
