using System;

namespace Helpwing.UI
{
    /// <summary>Every string the chat draws itself. No translations ship with the package; set these in your language.</summary>
    [Serializable]
    public sealed class SupportLabels
    {
        public string placeholder = "Write a message…";
        public string emailPlaceholder = "Your email address";
        public string send = "Send";
        public string sending = "Sending…";
        public string failed = "Not sent.";
        public string retry = "Tap to try again";
        public string offline = "No connection. Your messages will be sent when it comes back.";
        public string online = "We are online";
        public string away = "We are away right now";
        /// <summary><c>{0}</c> is the agent's name.</summary>
        public string typing = "{0} is typing…";
        public string branding = "Powered by Helpwing";
        public string loading = "Loading…";
        public string unavailable = "Support chat is not available right now.";
        public string launcher = "Chat";
        public string close = "Close";

        public string Typing(string name)
        {
            try
            {
                return string.Format(typing, name);
            }
            catch (FormatException)
            {
                return typing;
            }
        }
    }
}
