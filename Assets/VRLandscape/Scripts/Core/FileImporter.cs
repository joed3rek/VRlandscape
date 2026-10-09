using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine;

namespace VRLandscape
{
    // "Load file..." in the park menu. Copies the picked file into the device Parks folder as a new park:
    //  .glb / .gltf      -> 3D model park (Walk in 3D + AR)
    //  .obj              -> 3D model park; its .mtl and textures are copied too when they can be found
    //                       (the Android picker hands over only one file, so zip OBJ exports to keep materials)
    //  .jpg / .png       -> single 360 panorama
    //  .zip              -> a park folder with park.json, or a set of models/panoramas (auto-linked tour)
    public static class FileImporter
    {
        static readonly string[] ModelExt = { ".glb", ".gltf", ".obj" };
        static readonly string[] ImageExt = { ".jpg", ".jpeg", ".png" };

        public static void Pick(Action<string> onPicked)
        {
#if UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanel("Load model, panorama or park zip", "", "glb,gltf,obj,jpg,jpeg,png,zip");
            onPicked(string.IsNullOrEmpty(path) ? null : path);
#else
            // Many Android file managers don't know a MIME type for .glb, so allow everything and check the extension.
            NativeFilePicker.PickFile(path => onPicked(path), new[] { "*/*" });
#endif
        }

        // Returns the new park's name.
        public static async Task<string> Import(string sourcePath)
        {
            string ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            string baseName = Path.GetFileNameWithoutExtension(sourcePath);
            string folder = UniqueFolder(baseName);

            try
            {
                if (ModelExt.Contains(ext))
                {
                    if (ext == ".obj")
                    {
                        bool withMaterials = await Task.Run(() => CopyObjWithMaterials(sourcePath, folder));
                        WritePark(folder, new ParkDef
                        {
                            name = Pretty(baseName),
                            description = withMaterials ? "Imported OBJ model" : "Imported OBJ (no materials: load a .zip with the .mtl to keep them)",
                            model = Path.GetFileName(sourcePath)
                        });
                    }
                    else
                    {
                        await Task.Run(() =>
                        {
                            Directory.CreateDirectory(folder);
                            File.Copy(sourcePath, Path.Combine(folder, "model" + ext));
                        });
                        WritePark(folder, new ParkDef { name = Pretty(baseName), description = "Imported model", model = "model" + ext });
                    }
                }
                else if (ImageExt.Contains(ext))
                {
                    await Task.Run(() =>
                    {
                        Directory.CreateDirectory(folder);
                        File.Copy(sourcePath, Path.Combine(folder, "panorama" + ext));
                    });
                    WritePark(folder, new ParkDef
                    {
                        name = Pretty(baseName),
                        description = "Imported 360 panorama",
                        panoramas = new[] { new PanoramaDef { id = "view", name = Pretty(baseName), image = "panorama" + ext, hotspots = new HotspotDef[0] } }
                    });
                }
                else if (ext == ".zip")
                {
                    await ImportZip(sourcePath, folder, baseName);
                }
                else
                {
                    throw new Exception("Unsupported file type " + ext + ". Use .glb, .gltf, .jpg, .png or .zip.");
                }
            }
            catch
            {
                try { if (Directory.Exists(folder)) Directory.Delete(folder, true); } catch { /* best effort */ }
                throw;
            }
            return Path.GetFileName(folder);
        }

        static async Task ImportZip(string zipPath, string folder, string baseName)
        {
            string temp = Path.Combine(Application.temporaryCachePath, "import_" + Guid.NewGuid().ToString("N"));
            try
            {
                await Task.Run(() => ZipFile.ExtractToDirectory(zipPath, temp));

                // A ready-made park folder: park.json at the top level or one folder down.
                string parkJson = Directory.GetFiles(temp, "park.json", SearchOption.AllDirectories)
                    .OrderBy(p => p.Length).FirstOrDefault();
                if (parkJson != null)
                {
                    string parkDir = Path.GetDirectoryName(parkJson);
                    await Task.Run(() => CopyDirectory(parkDir, folder));
                    return;
                }

                // Otherwise build a park from whatever is inside.
                var files = Directory.GetFiles(temp, "*", SearchOption.AllDirectories)
                    .Where(f => !Path.GetFileName(f).StartsWith(".") && !f.Contains("__MACOSX"))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
                string modelFile = files.FirstOrDefault(f => Path.GetExtension(f).ToLowerInvariant() == ".glb")
                                   ?? files.FirstOrDefault(f => Path.GetExtension(f).ToLowerInvariant() == ".gltf")
                                   ?? files.FirstOrDefault(f => Path.GetExtension(f).ToLowerInvariant() == ".obj");
                // Images used as OBJ textures are materials, not panoramas.
                var textures = modelFile != null && modelFile.EndsWith(".obj", StringComparison.OrdinalIgnoreCase)
                    ? ObjTextureNames(modelFile) : new HashSet<string>();
                var images = files.Where(f => ImageExt.Contains(Path.GetExtension(f).ToLowerInvariant()) &&
                                              !textures.Contains(Path.GetFileName(f).ToLowerInvariant())).ToList();
                if (modelFile == null && images.Count == 0)
                    throw new Exception("The zip has no park.json, .glb/.obj model or panorama images.");

                await Task.Run(() => CopyDirectory(temp, folder));
                string Rel(string f) => f.Substring(temp.Length).TrimStart('/', '\\').Replace('\\', '/');

                // Panoramas in file-name order, linked as a simple tour: "Next" ahead, "Back" behind.
                var panoramas = new List<PanoramaDef>();
                for (int i = 0; i < images.Count; i++)
                {
                    var hotspots = new List<HotspotDef>();
                    if (i < images.Count - 1) hotspots.Add(new HotspotDef { target = "p" + (i + 1), label = "Next", yaw = 0, pitch = -12 });
                    if (i > 0) hotspots.Add(new HotspotDef { target = "p" + (i - 1), label = "Back", yaw = 180, pitch = -12 });
                    panoramas.Add(new PanoramaDef
                    {
                        id = "p" + i,
                        name = Pretty(Path.GetFileNameWithoutExtension(images[i])),
                        image = Rel(images[i]),
                        hotspots = hotspots.ToArray()
                    });
                }

                WritePark(folder, new ParkDef
                {
                    name = Pretty(baseName),
                    description = (modelFile != null ? "3D model" : "") + (modelFile != null && images.Count > 0 ? " + " : "") +
                                  (images.Count > 0 ? images.Count + " panoramas" : ""),
                    model = modelFile != null ? Rel(modelFile) : null,
                    panoramas = panoramas.ToArray()
                });
            }
            finally
            {
                try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch { /* best effort */ }
            }
        }

        // Copies an .obj plus the .mtl files it references and their textures (when they sit next to it).
        static bool CopyObjWithMaterials(string objPath, string folder)
        {
            Directory.CreateDirectory(folder);
            string srcDir = Path.GetDirectoryName(objPath);
            File.Copy(objPath, Path.Combine(folder, Path.GetFileName(objPath)));
            bool any = false;
            foreach (var mtl in ReferencedFiles(objPath, "mtllib"))
            {
                string mtlPath = Path.Combine(srcDir, mtl);
                if (!File.Exists(mtlPath)) continue;
                CopyRelative(srcDir, mtlPath, folder);
                any = true;
                foreach (var tex in ReferencedFiles(mtlPath, "map_Kd", lastToken: true))
                {
                    string texPath = Path.Combine(Path.GetDirectoryName(mtlPath), tex);
                    if (File.Exists(texPath)) CopyRelative(srcDir, texPath, folder);
                }
            }
            return any;
        }

        static HashSet<string> ObjTextureNames(string objPath)
        {
            var names = new HashSet<string>();
            foreach (var mtl in ReferencedFiles(objPath, "mtllib"))
            {
                string mtlPath = Path.Combine(Path.GetDirectoryName(objPath), mtl);
                if (!File.Exists(mtlPath)) continue;
                foreach (var tex in ReferencedFiles(mtlPath, "map_Kd", lastToken: true))
                    names.Add(Path.GetFileName(tex).ToLowerInvariant());
            }
            return names;
        }

        static IEnumerable<string> ReferencedFiles(string file, string keyword, bool lastToken = false)
        {
            foreach (var raw in File.ReadLines(file))
            {
                var line = raw.Trim();
                if (!line.StartsWith(keyword + " ")) continue;
                string value = lastToken
                    ? line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Last()
                    : line.Substring(keyword.Length).Trim();
                if (value.Length > 0) yield return value.Replace('\\', '/');
            }
        }

        static void CopyRelative(string srcRoot, string file, string destRoot)
        {
            string rel = file.Substring(srcRoot.Length).TrimStart('/', '\\');
            string dest = Path.Combine(destRoot, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            if (!File.Exists(dest)) File.Copy(file, dest);
        }

        static void WritePark(string folder, ParkDef park)
        {
            Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "park.json"), JsonUtility.ToJson(park, true));
        }

        static string UniqueFolder(string baseName)
        {
            string safe = new string(baseName.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == ' ' ? c : '_').ToArray()).Trim();
            if (safe.Length == 0) safe = "Imported";
            string folder = Path.Combine(ParkCatalog.DeviceParksFolder, safe);
            for (int i = 2; Directory.Exists(folder); i++) folder = Path.Combine(ParkCatalog.DeviceParksFolder, $"{safe} ({i})");
            return folder;
        }

        static string Pretty(string fileName) => fileName.Replace('_', ' ').Replace('-', ' ').Trim();

        static void CopyDirectory(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (var dir in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, dir.Substring(from.Length).TrimStart('/', '\\')));
            foreach (var file in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(to, file.Substring(from.Length).TrimStart('/', '\\')), true);
        }
    }
}
