using System.IO;
using UnityEngine;

namespace LightsOut
{
    /// <summary>Development-only progress storage reset. Other preferences, including volume, are preserved.</summary>
    public static class LightsOutDevelopmentSave
    {
        public static bool ClearProgress(string directory, string careerKey, string permanentKey, out string error)
        {
            error = null;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            try
            {
                File.Delete(Path.Combine(directory, "lightsout-rent-save.json"));
                File.Delete(Path.Combine(directory, "lightsout-permanent-save.json"));
                PlayerPrefs.DeleteKey(careerKey);
                PlayerPrefs.DeleteKey(permanentKey);
                PlayerPrefs.Save();
                return true;
            }
            catch (IOException exception) { error = exception.Message; }
            catch (System.UnauthorizedAccessException exception) { error = exception.Message; }
            return false;
#else
            return false;
#endif
        }
    }
}
