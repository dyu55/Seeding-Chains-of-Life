using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using SCoL.Voxels;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SCoL.Settlement
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SCoLCampsiteSceneAnchor : MonoBehaviour
    {
        public string resourcePath = "Campsite/tentV2";
        public string editorAssetPath = "Assets/Models/Modeling/_Incoming/tent updated/tent updated.obj";
        public Vector3 visualEuler = Vector3.zero;
        [Min(0.5f)] public float targetFootprint = 6.2f;
        [Min(0.5f)] public float targetHeight = 3.8f;
        [Min(0f)] public float groundOffset = 0.05f;
        [Min(0.5f)] public float dryLandSearchStep = 2f;
        [Min(0)] public int dryLandSearchRings = 8;
        [Min(0f)] public float minShoreClearance = 6f;
        [Min(0.5f)] public float flatnessSampleRadius = 6f;
        [Min(0f)] public float maxFlatHeightDelta = 0.9f;

        [SerializeField] GameObject _visualInstance;

        void Awake()
        {
            EnsureVisual();
        }

        void OnEnable()
        {
            EnsureVisual();
        }

        void Start()
        {
            EnsureVisual();
        }

        void OnValidate()
        {
        }

        public Vector3 GetAnchorPosition(VoxelWorld voxelWorld)
        {
            Vector3 world = transform.position;
            if (voxelWorld == null)
                return world;

            if (TryFindNearbyDryGround(voxelWorld, world, out var grounded))
                return grounded;

            return world;
        }

        public GameObject GetVisualRoot()
        {
            EnsureVisual();
            return _visualInstance;
        }

        public void EnsureVisualNow()
        {
            EnsureVisual();
        }

        void EnsureVisual()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && PrefabUtility.IsPartOfPrefabAsset(gameObject))
                return;
#endif

            if (_visualInstance == null)
            {
                var existing = transform.Find("CampsiteVisual");
                if (existing != null)
                    _visualInstance = existing.gameObject;
            }

            if (_visualInstance != null && _visualInstance.transform.parent != transform)
                _visualInstance = null;

            var prefab = LoadVisualPrefab();
            if (_visualInstance != null && ShouldRebuildFromPreferredPrefab(prefab))
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(_visualInstance);
                else
                    Destroy(_visualInstance);
#else
                Destroy(_visualInstance);
#endif
                _visualInstance = null;
            }
            if (_visualInstance == null && prefab != null)
            {
#if UNITY_EDITOR
                _visualInstance = Application.isPlaying
                    ? Instantiate(prefab, transform)
                    : PrefabUtility.InstantiatePrefab(prefab, transform) as GameObject;
#else
                _visualInstance = Instantiate(prefab, transform);
#endif
            }

            if (_visualInstance == null)
                _visualInstance = BuildObjFallbackVisual();

            if (_visualInstance == null)
                return;

            _visualInstance.name = "CampsiteVisual";
            _visualInstance.transform.SetParent(transform, false);
            _visualInstance.transform.localPosition = Vector3.zero;
            _visualInstance.transform.localRotation = Quaternion.Euler(visualEuler);
            NormalizeVisual();
        }

        GameObject LoadVisualPrefab()
        {
            var prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab != null)
                return prefab;

#if UNITY_EDITOR
            if (!string.IsNullOrWhiteSpace(editorAssetPath))
            {
                var editorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(editorAssetPath);
                if (editorPrefab != null)
                    return editorPrefab;
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Resources/Campsite/tentV2.obj");
#else
            return null;
#endif
        }

        GameObject BuildObjFallbackVisual()
        {
            string objPath = Path.Combine(Application.dataPath, "Resources/Campsite/tentV2.obj");
            if (!File.Exists(objPath))
                return null;

            if (!TryBuildObjMesh(objPath, out Mesh mesh))
                return null;

            var visualRoot = new GameObject("CampsiteVisual");
            visualRoot.transform.SetParent(transform, false);

            var filter = visualRoot.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = visualRoot.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = CreateFallbackMaterial();
            return visualRoot;
        }

        Material CreateFallbackMaterial()
        {
            Texture2D texture = Resources.Load<Texture2D>("Campsite/texturefile2");
#if UNITY_EDITOR
            if (texture == null)
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Campsite/texturefile2.png");
#endif

            var shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard")
                ?? Shader.Find("Legacy Shaders/Diffuse");
            var material = new Material(shader);
            material.name = "CampsiteFallbackMaterial";
            if (texture != null)
            {
                if (material.HasProperty("_BaseMap"))
                    material.SetTexture("_BaseMap", texture);
                if (material.HasProperty("_MainTex"))
                    material.SetTexture("_MainTex", texture);
            }
            return material;
        }

        bool ShouldRebuildFromPreferredPrefab(GameObject preferredPrefab)
        {
            if (_visualInstance == null || preferredPrefab == null)
                return false;

            return _visualInstance.transform.childCount == 0 &&
                   _visualInstance.GetComponent<MeshFilter>() != null &&
                   _visualInstance.GetComponent<MeshRenderer>() != null;
        }

        bool TryFindNearbyDryGround(VoxelWorld voxelWorld, Vector3 around, out Vector3 grounded)
        {
            grounded = around;
            if (voxelWorld == null)
                return false;

            Vector3 best = default;
            float bestScore = float.NegativeInfinity;

            float step = Mathf.Max(0.5f, dryLandSearchStep);
            int rings = Mathf.Max(1, dryLandSearchRings);
            for (int ring = 0; ring <= rings; ring++)
            {
                int samples = ring == 0 ? 1 : Mathf.Max(8, ring * 8);
                float radius = ring * step;
                for (int i = 0; i < samples; i++)
                {
                    float angle = samples == 1 ? 0f : i / (float)samples * Mathf.PI * 2f;
                    Vector3 probe = around + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    if (!TryProjectDryPoint(voxelWorld, probe, out var candidate))
                        continue;

                    float shoreDistance = EstimateShoreDistance(voxelWorld, candidate);
                    float flatnessDelta = EstimateFlatnessDelta(voxelWorld, candidate);
                    if (flatnessDelta > Mathf.Max(0.01f, maxFlatHeightDelta))
                        continue;

                    float score = shoreDistance - radius * 0.2f - flatnessDelta * 5f;
                    if (score <= bestScore)
                        continue;

                    bestScore = score;
                    best = candidate;

                    if (shoreDistance >= Mathf.Max(0f, minShoreClearance))
                    {
                        grounded = candidate;
                        return true;
                    }
                }
            }

            if (bestScore > float.NegativeInfinity)
            {
                grounded = best;
                return true;
            }

            return false;
        }

        bool TryProjectDryPoint(VoxelWorld voxelWorld, Vector3 probe, out Vector3 grounded)
        {
            grounded = probe;
            if (!voxelWorld.TryGetTerrainSurfaceYAtWorld(probe + Vector3.up * 4f, out float surfaceY, includeWaterSurface: false))
                return false;

            grounded = new Vector3(probe.x, surfaceY + groundOffset, probe.z);
            return !voxelWorld.IsWaterColumnAtWorld(grounded);
        }

        float EstimateShoreDistance(VoxelWorld voxelWorld, Vector3 center)
        {
            float target = Mathf.Max(0f, minShoreClearance);
            if (target <= 0.01f)
                return 999f;

            float step = Mathf.Max(1f, dryLandSearchStep);
            for (float radius = step; radius <= target; radius += step)
            {
                int samples = Mathf.Max(8, Mathf.CeilToInt(radius * 6f));
                for (int i = 0; i < samples; i++)
                {
                    float angle = i / (float)samples * Mathf.PI * 2f;
                    Vector3 probe = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                    if (voxelWorld.IsWaterColumnAtWorld(probe))
                        return radius - step;
                }
            }

            return target;
        }

        float EstimateFlatnessDelta(VoxelWorld voxelWorld, Vector3 center)
        {
            if (voxelWorld == null)
                return 999f;

            float radius = Mathf.Max(0.5f, flatnessSampleRadius);
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            int samples = 8;
            for (int i = 0; i < samples; i++)
            {
                float angle = i / (float)samples * Mathf.PI * 2f;
                Vector3 probe = center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (!voxelWorld.TryGetTerrainSurfaceYAtWorld(probe + Vector3.up * 4f, out float surfaceY, includeWaterSurface: false))
                    return 999f;

                minY = Mathf.Min(minY, surfaceY);
                maxY = Mathf.Max(maxY, surfaceY);
            }

            return maxY - minY;
        }

        static bool TryBuildObjMesh(string objPath, out Mesh mesh)
        {
            mesh = null;

            var positions = new List<Vector3>(1024);
            var uvs = new List<Vector2>(1024);
            var finalVertices = new List<Vector3>(2048);
            var finalUvs = new List<Vector2>(2048);
            var triangles = new List<int>(4096);
            var vertexMap = new Dictionary<VertexKey, int>(2048);

            foreach (string rawLine in File.ReadLines(objPath))
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line[0] == '#')
                    continue;

                if (line.StartsWith("v "))
                {
                    if (TryParseVector3(line, out var position))
                        positions.Add(position);
                    continue;
                }

                if (line.StartsWith("vt "))
                {
                    if (TryParseVector2(line, out var uv))
                        uvs.Add(new Vector2(uv.x, 1f - uv.y));
                    continue;
                }

                if (!line.StartsWith("f "))
                    continue;

                var faceIndices = ParseFace(line);
                if (faceIndices.Count < 3)
                    continue;

                for (int i = 1; i < faceIndices.Count - 1; i++)
                {
                    AddFaceVertex(faceIndices[0], positions, uvs, finalVertices, finalUvs, triangles, vertexMap);
                    AddFaceVertex(faceIndices[i], positions, uvs, finalVertices, finalUvs, triangles, vertexMap);
                    AddFaceVertex(faceIndices[i + 1], positions, uvs, finalVertices, finalUvs, triangles, vertexMap);
                }
            }

            if (finalVertices.Count == 0 || triangles.Count == 0)
                return false;

            mesh = new Mesh
            {
                name = "CampsiteTentMesh"
            };
            mesh.SetVertices(finalVertices);
            mesh.SetUVs(0, finalUvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateNormals();
            return true;
        }

        static void AddFaceVertex(
            VertexKey key,
            List<Vector3> positions,
            List<Vector2> uvs,
            List<Vector3> finalVertices,
            List<Vector2> finalUvs,
            List<int> triangles,
            Dictionary<VertexKey, int> vertexMap)
        {
            if (!vertexMap.TryGetValue(key, out int index))
            {
                index = finalVertices.Count;
                vertexMap.Add(key, index);
                finalVertices.Add(positions[key.vertexIndex]);
                finalUvs.Add(key.uvIndex >= 0 && key.uvIndex < uvs.Count ? uvs[key.uvIndex] : Vector2.zero);
            }

            triangles.Add(index);
        }

        static bool TryParseVector3(string line, out Vector3 value)
        {
            value = default;
            string[] parts = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 4)
                return false;

            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y) ||
                !float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
                return false;

            value = new Vector3(x, y, z);
            return true;
        }

        static bool TryParseVector2(string line, out Vector2 value)
        {
            value = default;
            string[] parts = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
                return false;

            if (!float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) ||
                !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
                return false;

            value = new Vector2(x, y);
            return true;
        }

        static List<VertexKey> ParseFace(string line)
        {
            var face = new List<VertexKey>(4);
            string[] parts = line.Split((char[])null, System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 1; i < parts.Length; i++)
            {
                string[] indices = parts[i].Split('/');
                if (indices.Length == 0)
                    continue;

                if (!int.TryParse(indices[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int vertexIndex))
                    continue;

                int uvIndex = -1;
                if (indices.Length > 1 && !string.IsNullOrEmpty(indices[1]))
                    int.TryParse(indices[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out uvIndex);

                face.Add(new VertexKey(vertexIndex - 1, uvIndex - 1));
            }

            return face;
        }

        readonly struct VertexKey
        {
            public readonly int vertexIndex;
            public readonly int uvIndex;

            public VertexKey(int vertexIndex, int uvIndex)
            {
                this.vertexIndex = vertexIndex;
                this.uvIndex = uvIndex;
            }
        }

        void NormalizeVisual()
        {
            if (_visualInstance == null || !TryGetHierarchyBounds(_visualInstance, out var bounds))
                return;

            float footprint = Mathf.Max(0.01f, Mathf.Max(bounds.size.x, bounds.size.z));
            float height = Mathf.Max(0.01f, bounds.size.y);
            float uniformScale = Mathf.Min(
                Mathf.Max(0.01f, targetFootprint) / footprint,
                Mathf.Max(0.01f, targetHeight) / height);
            _visualInstance.transform.localScale = Vector3.one * uniformScale;

            if (!TryGetHierarchyBounds(_visualInstance, out bounds))
                return;

            Vector3 currentBottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            Vector3 desiredBottomCenter = transform.position;
            _visualInstance.transform.position += desiredBottomCenter - currentBottomCenter;
        }

        static bool TryGetHierarchyBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            if (go == null)
                return false;

            var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            bool hasBounds = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = renderer.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            return hasBounds;
        }
    }
}
