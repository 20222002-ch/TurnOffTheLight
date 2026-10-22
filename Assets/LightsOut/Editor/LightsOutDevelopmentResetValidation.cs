using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace LightsOut.Editor
{
    /// <summary>Exercises development reset storage using disposable keys and files, never the player's saves.</summary>
    public static class LightsOutDevelopmentResetValidation
    {
        [Serializable] sealed class Report { public int passed, failed; public List<Result> checks = new List<Result>(); }
        [Serializable] sealed class Result { public string name, details; public bool passed; }
        [UnityEditor.MenuItem("Lights Out/Run development reset storage checks")]
        static void RunMenu() { Debug.Log(Run()); }
        public static string Run()
        {
            var report = new Report();
            Check(report, "reset removes both progress stores while preserving unrelated data", SelectiveClear);
            Check(report, "reset remains safe when progress was already cleared", RepeatedClear);
            Check(report, "unwritable storage reports failure without clearing preference keys", FailurePreservesKeys);
            return JsonUtility.ToJson(report, true);
        }
        static void Check(Report report, string name, Func<string> action)
        {
            try { report.checks.Add(new Result { name = name, passed = true, details = action() }); report.passed++; }
            catch (Exception exception) { report.checks.Add(new Result { name = name, passed = false, details = exception.Message }); report.failed++; }
        }
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static string Exercise(Func<string, string, string, string, string> action)
        {
            string suffix = Guid.NewGuid().ToString("N"), directory = Path.Combine(Application.temporaryCachePath, "LightsOutReset-" + suffix);
            string career = "LightsOut.QA.Career." + suffix, permanent = "LightsOut.QA.Permanent." + suffix, unrelated = "LightsOut.QA.Unrelated." + suffix;
            Directory.CreateDirectory(directory);
            try { return action(directory, career, permanent, unrelated); }
            finally
            {
                PlayerPrefs.DeleteKey(career); PlayerPrefs.DeleteKey(permanent); PlayerPrefs.DeleteKey(unrelated); PlayerPrefs.Save();
                foreach (string filename in new[] { "lightsout-rent-save.json", "lightsout-permanent-save.json", "unrelated.txt", "blocked" }) File.Delete(Path.Combine(directory, filename));
                Directory.Delete(directory);
            }
        }
        static string SelectiveClear()
        {
            return Exercise((directory, career, permanent, unrelated) =>
            {
                PlayerPrefs.SetString(career, "career fixture"); PlayerPrefs.SetString(permanent, "permanent fixture"); PlayerPrefs.SetInt(unrelated, 73);
                File.WriteAllText(Path.Combine(directory, "lightsout-rent-save.json"), "career fixture");
                File.WriteAllText(Path.Combine(directory, "lightsout-permanent-save.json"), "permanent fixture");
                File.WriteAllText(Path.Combine(directory, "unrelated.txt"), "keep me");
                Require(LightsOutDevelopmentSave.ClearProgress(directory, career, permanent, out string error), error ?? "Reset failed");
                Require(!PlayerPrefs.HasKey(career) && !PlayerPrefs.HasKey(permanent), "Progress preferences remain");
                Require(!File.Exists(Path.Combine(directory, "lightsout-rent-save.json")) && !File.Exists(Path.Combine(directory, "lightsout-permanent-save.json")), "Progress files remain");
                Require(PlayerPrefs.GetInt(unrelated) == 73 && File.ReadAllText(Path.Combine(directory, "unrelated.txt")) == "keep me", "Unrelated preferences or files were changed");
                return "Two isolated progress keys and both JSON files cleared; unrelated preference/file preserved";
            });
        }
        static string RepeatedClear()
        {
            return Exercise((directory, career, permanent, unrelated) =>
            {
                Require(LightsOutDevelopmentSave.ClearProgress(directory, career, permanent, out string first), first ?? "First reset failed");
                Require(LightsOutDevelopmentSave.ClearProgress(directory, career, permanent, out string second), second ?? "Repeated reset failed");
                return "Missing save files and keys can be reset twice without an exception";
            });
        }
        static string FailurePreservesKeys()
        {
            return Exercise((directory, career, permanent, unrelated) =>
            {
                string blocked = Path.Combine(directory, "blocked"); File.WriteAllText(blocked, "not a directory");
                PlayerPrefs.SetString(career, "career fixture"); PlayerPrefs.SetString(permanent, "permanent fixture");
                Require(!LightsOutDevelopmentSave.ClearProgress(blocked, career, permanent, out string error) && !string.IsNullOrEmpty(error), "Invalid storage was reported as successful");
                Require(PlayerPrefs.GetString(career) == "career fixture" && PlayerPrefs.GetString(permanent) == "permanent fixture", "Storage failure erased preference data");
                return "File used as a directory returns an error and preserves both progress keys";
            });
        }
    }
}
