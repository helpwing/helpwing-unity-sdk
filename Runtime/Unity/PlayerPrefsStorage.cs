using System.Threading.Tasks;
using UnityEngine;

namespace Helpwing
{
    /// <summary>The session in PlayerPrefs, saved on every write so an app kill loses nothing.</summary>
    public sealed class PlayerPrefsStorage : IHelpwingStorage
    {
        public Task<string> GetItem(string key)
        {
            return Task.FromResult(PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null);
        }

        public Task SetItem(string key, string value)
        {
            PlayerPrefs.SetString(key, value);
            PlayerPrefs.Save();
            return Task.CompletedTask;
        }

        public Task RemoveItem(string key)
        {
            PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
            return Task.CompletedTask;
        }
    }
}
