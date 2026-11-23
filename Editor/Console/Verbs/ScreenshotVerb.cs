#if UNITY_EDITOR
using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UtilityTools.AIConsole
{
    /// <summary>
    /// <c>screenshot game|scene [file.png] [width height]</c>: saves what the Game or Scene view camera sees.
    /// In Play Mode, <c>game</c> without a size captures the whole Game view including UI; otherwise the camera is
    /// rendered on its own (no UI overlays).
    /// </summary>
    internal sealed class ScreenshotVerb : IAgentVerb
    {
        internal const string ShotsFolder = "shots";
        private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(10);
        private const int FallbackWidth = 1280, FallbackHeight = 720, MaxSide = 4096;

        public string Name => "screenshot";
        public string Usage => "screenshot game|scene [file.png] [width height]";
        public bool ReadOnly => true;

        internal readonly struct Request
        {
            public readonly string View;
            public readonly string File;
            public readonly int Width;
            public readonly int Height;

            public Request(string view, string file, int width, int height)
            {
                View = view;
                File = file;
                Width = width;
                Height = height;
            }

            public bool HasSize => Width > 0 && Height > 0;
        }

        /// <summary>Parses the arguments; <c>null</c> with an error when they don't fit the usage.</summary>
        internal static Request? Parse(string arg, out string error)
        {
            error = null;
            string[] words = (arg ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0 || words[0] != "game" && words[0] != "scene")
            {
                error = "usage: screenshot game|scene [file.png] [width height]";
                return null;
            }

            int i = 1;
            string file = null;
            if (i < words.Length && words[i].EndsWith(".png", StringComparison.OrdinalIgnoreCase)) file = words[i++];

            int width = 0, height = 0;
            if (i < words.Length)
            {
                if (i + 2 != words.Length || !TryPositive(words[i], out width) || !TryPositive(words[i + 1], out height) ||
                    width > MaxSide || height > MaxSide)
                {
                    error = $"size must be two numbers from 1 to {MaxSide}, e.g. `screenshot game 1080 1920`";
                    return null;
                }
            }

            return new Request(words[0], file, width, height);
        }

        public AgentVerbResult Start(AgentVerbCall call)
        {
            var parsed = Parse(call.Arg, out string error);
            if (parsed == null) return AgentVerbResult.Failure(error);
            var request = parsed.Value;

            string relative = request.File ?? DefaultFile(request.View);
            string full = Path.GetFullPath(relative);
            Directory.CreateDirectory(Path.GetDirectoryName(full));

            if (request.View == "game" && EditorApplication.isPlaying && !request.HasSize)
            {
                if (File.Exists(full)) File.Delete(full);
                ScreenCapture.CaptureScreenshot(full);
                call.Data = relative;
                return AgentVerbResult.Running; // written at the end of a frame
            }

            var camera = request.View == "game" ? Camera.main : SceneView.lastActiveSceneView != null ? SceneView.lastActiveSceneView.camera : null;
            if (camera == null)
                return AgentVerbResult.Failure(request.View == "game" ? "no camera tagged MainCamera" : "no Scene view is open");

            int width = request.HasSize ? request.Width : camera.pixelWidth > 16 ? camera.pixelWidth : FallbackWidth;
            int height = request.HasSize ? request.Height : camera.pixelHeight > 16 ? camera.pixelHeight : FallbackHeight;
            RenderToFile(camera, full, width, height);
            return AgentVerbResult.Success($"saved {relative} ({width}×{height}, camera render, no UI)");
        }

        public AgentVerbResult Check(AgentVerbCall call)
        {
            string full = Path.GetFullPath(call.Data);
            var info = new FileInfo(full);
            if (info.Exists && info.Length > 0 && DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromMilliseconds(200))
                return AgentVerbResult.Success($"saved {call.Data} (Game view with UI)");
            if (!EditorApplication.isPlaying) return AgentVerbResult.Failure("Play Mode ended before the capture was written");
            if (call.Elapsed > CaptureTimeout) return AgentVerbResult.Failure("the capture was not written — is the Game view open?");
            return AgentVerbResult.Running;
        }

        private static string DefaultFile(string view)
        {
            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
            return AIBridge.Folder.Replace('\\', '/').TrimEnd('/') + "/" + ShotsFolder + "/" + stamp + "_" + view + ".png";
        }

        private static void RenderToFile(Camera camera, string path, int width, int height)
        {
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = camera.targetTexture;
            var previousActive = RenderTexture.active;
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static bool TryPositive(string s, out int value) =>
            int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value > 0;
    }
}
#endif
