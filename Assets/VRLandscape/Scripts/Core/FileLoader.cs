using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace VRLandscape
{
    // Reads files from StreamingAssets (inside the APK on Android) or from regular folders.
    public static class FileLoader
    {
        public static string ToUri(string path)
        {
            if (path.Contains("://")) return path;
            return new Uri(Path.GetFullPath(path)).AbsoluteUri;
        }

        public static string Combine(string baseDir, string relative)
        {
            if (string.IsNullOrEmpty(baseDir) || relative.Contains("://") || Path.IsPathRooted(relative)) return relative;
            return baseDir.TrimEnd('/', '\\') + "/" + relative.Replace('\\', '/');
        }

        public static async Task<byte[]> ReadBytes(string path, bool quiet = false)
        {
            using var req = UnityWebRequest.Get(ToUri(path));
            var op = req.SendWebRequest();
            while (!op.isDone) await Task.Yield();
            if (req.result != UnityWebRequest.Result.Success)
            {
                if (!quiet) Debug.LogWarning($"[VRLandscape] Could not read {path}: {req.error}");
                return null;
            }
            return req.downloadHandler.data;
        }

        public static async Task<string> ReadText(string path, bool quiet = false)
        {
            var bytes = await ReadBytes(path, quiet);
            return bytes == null ? null : System.Text.Encoding.UTF8.GetString(bytes);
        }

        // Panoramas wider than this are scaled down on the GPU to save memory (a 10k image is ~150 MB).
        static int MaxPanoramaWidth => Mathf.Min(SystemInfo.maxTextureSize, Application.isMobilePlatform ? 6144 : 8192);

        public static async Task<Texture> ReadTexture(string path)
        {
            var bytes = await ReadBytes(path);
            if (bytes == null) return null;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!tex.LoadImage(bytes, true) || tex.width <= 2)
            {
                UnityEngine.Object.Destroy(tex);
                Debug.LogWarning($"[VRLandscape] Not a valid image (or larger than this device's {SystemInfo.maxTextureSize}px limit): {path}");
                return null;
            }
            tex.wrapModeU = TextureWrapMode.Repeat;
            tex.wrapModeV = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            if (tex.width <= MaxPanoramaWidth) return tex;

            int w = MaxPanoramaWidth;
            int h = Mathf.Max(1, Mathf.RoundToInt(tex.height * (w / (float)tex.width)));
            var rt = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32)
            {
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                name = System.IO.Path.GetFileName(path)
            };
            Graphics.Blit(tex, rt);
            UnityEngine.Object.Destroy(tex);
            return rt;
        }
    }
}
