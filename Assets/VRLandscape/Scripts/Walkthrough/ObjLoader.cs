using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace VRLandscape
{
    // Runtime Wavefront OBJ loader (+ MTL materials and textures), for SketchUp / Rhino / Revit exports.
    // Geometry is merged into one mesh per material, which keeps big landscape models fast on phones.
    // Up axis: "y" or "z" from park.json, otherwise guessed (landscapes are flat, so the thinnest axis is up).
    public static class ObjLoader
    {
        class Material
        {
            public string name;
            public Color color = new Color(0.8f, 0.8f, 0.8f, 1f);
            public string texture;
        }

        class Corner { public int v, t, n; }

        class Data
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<Vector2> vt = new List<Vector2>();
            public readonly List<Vector3> vn = new List<Vector3>();
            public readonly Dictionary<string, List<Corner[]>> facesByMaterial = new Dictionary<string, List<Corner[]>>();
            public readonly List<string> mtlLibs = new List<string>();
        }

        // Mesh arrays built off the main thread, turned into Unity meshes on it.
        class MeshArrays
        {
            public string material;
            public Vector3[] positions;
            public Vector3[] normals;
            public Vector2[] uvs;
            public int[] triangles;
        }

        public static async Task<GameObject> Load(string path, Transform parent, string upAxis)
        {
            string text = await FileLoader.ReadText(path);
            if (text == null) throw new Exception("Could not read " + Path.GetFileName(path));
            string dir = path.Substring(0, Math.Max(0, path.Replace('\\', '/').LastIndexOf('/')));

            var data = await Task.Run(() => Parse(text));
            if (data.v.Count == 0) throw new Exception("The OBJ file has no geometry.");

            bool zUp = upAxis == "z" || (upAxis != "y" && GuessZUp(data.v));
            var arrays = await Task.Run(() => Build(data, zUp));

            var materials = new Dictionary<string, Material>();
            foreach (var lib in data.mtlLibs)
            {
                string mtl = await FileLoader.ReadText(FileLoader.Combine(dir, lib), quiet: true);
                if (mtl != null) ParseMtl(mtl, materials);
            }

            var root = new GameObject(Path.GetFileNameWithoutExtension(path));
            root.transform.SetParent(parent, false);
            var unityMaterials = new Dictionary<string, UnityEngine.Material>();
            foreach (var a in arrays)
            {
                if (!unityMaterials.TryGetValue(a.material, out var mat))
                {
                    materials.TryGetValue(a.material, out var def);
                    mat = await CreateMaterial(def, dir);
                    unityMaterials[a.material] = mat;
                }

                var mesh = new Mesh { name = a.material };
                if (a.positions.Length > 65535) mesh.indexFormat = IndexFormat.UInt32;
                mesh.vertices = a.positions;
                mesh.uv = a.uvs;
                mesh.triangles = a.triangles;
                if (a.normals != null) mesh.normals = a.normals;
                else mesh.RecalculateNormals();
                mesh.RecalculateBounds();

                var go = new GameObject(string.IsNullOrEmpty(a.material) ? "Mesh" : a.material);
                go.transform.SetParent(root.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            }
            return root;
        }

        // ------------------------------------------------------------ parsing

        static Data Parse(string text)
        {
            var d = new Data();
            string currentMaterial = "";
            var faces = GetFaces(d, currentMaterial);
            var corners = new List<Corner>(8);

            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length < 2 || line[0] == '#') continue;
                    var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    switch (parts[0])
                    {
                        case "v":
                            if (parts.Length >= 4) d.v.Add(new Vector3(F(parts[1]), F(parts[2]), F(parts[3])));
                            break;
                        case "vt":
                            if (parts.Length >= 3) d.vt.Add(new Vector2(F(parts[1]), F(parts[2])));
                            break;
                        case "vn":
                            if (parts.Length >= 4) d.vn.Add(new Vector3(F(parts[1]), F(parts[2]), F(parts[3])));
                            break;
                        case "f":
                            corners.Clear();
                            for (int i = 1; i < parts.Length; i++) corners.Add(ParseCorner(parts[i], d));
                            if (corners.Count >= 3) faces.Add(corners.ToArray());
                            break;
                        case "usemtl":
                            currentMaterial = parts.Length > 1 ? line.Substring(6).Trim() : "";
                            faces = GetFaces(d, currentMaterial);
                            break;
                        case "mtllib":
                            if (parts.Length > 1) d.mtlLibs.Add(line.Substring(6).Trim());
                            break;
                    }
                }
            }
            return d;
        }

        static List<Corner[]> GetFaces(Data d, string material)
        {
            if (!d.facesByMaterial.TryGetValue(material, out var list))
                d.facesByMaterial[material] = list = new List<Corner[]>();
            return list;
        }

        // "v", "v/t", "v//n" or "v/t/n"; indices are 1-based, negative ones count back from the end.
        static Corner ParseCorner(string token, Data d)
        {
            var c = new Corner { v = -1, t = -1, n = -1 };
            var p = token.Split('/');
            c.v = Index(p[0], d.v.Count);
            if (p.Length > 1 && p[1].Length > 0) c.t = Index(p[1], d.vt.Count);
            if (p.Length > 2 && p[2].Length > 0) c.n = Index(p[2], d.vn.Count);
            return c;
        }

        static int Index(string s, int count)
        {
            int i = int.Parse(s, CultureInfo.InvariantCulture);
            return i < 0 ? count + i : i - 1;
        }

        static float F(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        static bool GuessZUp(List<Vector3> v)
        {
            Vector3 min = v[0], max = v[0];
            foreach (var p in v) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            var size = max - min;
            return size.z < size.y; // the thinner of Y and Z is "up" for a flat site
        }

        // OBJ is right-handed; Unity is left-handed. Both conversions mirror one axis, so winding is reversed.
        static Vector3 Convert(Vector3 p, bool zUp) => zUp ? new Vector3(p.x, p.z, p.y) : new Vector3(-p.x, p.y, p.z);

        static List<MeshArrays> Build(Data d, bool zUp)
        {
            var result = new List<MeshArrays>();
            foreach (var kv in d.facesByMaterial)
            {
                if (kv.Value.Count == 0) continue;
                var positions = new List<Vector3>();
                var normals = new List<Vector3>();
                var uvs = new List<Vector2>();
                var triangles = new List<int>();
                var lookup = new Dictionary<(int, int, int), int>();
                bool hasNormals = true;

                int Vertex(Corner c)
                {
                    var key = (c.v, c.t, c.n);
                    if (lookup.TryGetValue(key, out int index)) return index;
                    index = positions.Count;
                    positions.Add(Convert(d.v[c.v], zUp));
                    uvs.Add(c.t >= 0 && c.t < d.vt.Count ? d.vt[c.t] : Vector2.zero);
                    if (c.n >= 0 && c.n < d.vn.Count) normals.Add(Convert(d.vn[c.n], zUp));
                    else { normals.Add(Vector3.up); hasNormals = false; }
                    lookup[key] = index;
                    return index;
                }

                foreach (var face in kv.Value)
                {
                    bool valid = true;
                    foreach (var c in face) if (c.v < 0 || c.v >= d.v.Count) valid = false;
                    if (!valid) continue;
                    int first = Vertex(face[0]);
                    for (int i = 1; i < face.Length - 1; i++)
                    {
                        // Fan triangulation, winding reversed for Unity.
                        triangles.Add(first);
                        triangles.Add(Vertex(face[i + 1]));
                        triangles.Add(Vertex(face[i]));
                    }
                }
                if (triangles.Count == 0) continue;
                result.Add(new MeshArrays
                {
                    material = kv.Key,
                    positions = positions.ToArray(),
                    normals = hasNormals ? normals.ToArray() : null,
                    uvs = uvs.ToArray(),
                    triangles = triangles.ToArray()
                });
            }
            return result;
        }

        static void ParseMtl(string text, Dictionary<string, Material> into)
        {
            Material current = null;
            using (var reader = new StringReader(text))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Length < 2 || line[0] == '#') continue;
                    var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    switch (parts[0])
                    {
                        case "newmtl":
                            current = new Material { name = line.Substring(6).Trim() };
                            into[current.name] = current;
                            break;
                        case "Kd":
                            if (current != null && parts.Length >= 4)
                                current.color = new Color(F(parts[1]), F(parts[2]), F(parts[3]), current.color.a);
                            break;
                        case "d":
                            if (current != null && parts.Length >= 2) current.color.a = Mathf.Clamp01(F(parts[1]));
                            break;
                        case "Tr":
                            if (current != null && parts.Length >= 2) current.color.a = 1f - Mathf.Clamp01(F(parts[1]));
                            break;
                        case "map_Kd":
                            // Options like "-s 1 1 1" may come first; the file name is the last token.
                            if (current != null && parts.Length >= 2) current.texture = parts[parts.Length - 1];
                            break;
                    }
                }
            }
        }

        static async Task<UnityEngine.Material> CreateMaterial(Material def, string dir)
        {
            var color = def != null ? def.color : new Color(0.8f, 0.8f, 0.8f, 1f);
            var mat = Visuals.NewLit(color);
            if (def != null && !string.IsNullOrEmpty(def.texture))
            {
                var tex = await FileLoader.ReadTexture(FileLoader.Combine(dir, def.texture.Replace('\\', '/')));
                if (tex != null)
                {
                    tex.wrapMode = TextureWrapMode.Repeat;
                    mat.mainTexture = tex;
                }
            }
            if (color.a < 0.99f) Visuals.MakeTransparent(mat);
            return mat;
        }
    }
}
