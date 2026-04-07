using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SCoL.Visualization
{
    public sealed class SCoLPollinationBeeSwarm : MonoBehaviour
    {
        readonly List<BeeInstance> _bees = new();
        GameObject _beePrefab;
        AnimationClip _beeClip;
        float _duration;
        float _radius;
        float _height;
        float _speed;
        float _bobAmplitude;
        float _bobSpeed;
        float _spawnTime;
        float _beeScale = 1f;
        ParticleSystem _fireflyParticles;
        AudioSource _beeAudioSource;

        sealed class BeeInstance
        {
            public Transform Root;
            public GameObject Visual;
            public float Angle;
            public float AngularSpeed;
            public float Radius;
            public float HeightOffset;
            public float BobOffset;
            public PlayableGraph Graph;
        }

        public void Initialize(
            GameObject beePrefab,
            AnimationClip beeClip,
            int beeCount,
            float radius,
            float height,
            float duration,
            float speed,
            float bobAmplitude,
            float bobSpeed,
            float beeScale,
            int fireflyCount,
            Vector2 fireflySizeRange,
            Vector2 fireflySpeedRange,
            float fireflyRadius,
            float fireflyHeight,
            AudioClip beeBuzzClip,
            float buzzVolume,
            float buzzMinDistance,
            float buzzMaxDistance,
            Color beeGlowColor,
            float beeGlowIntensity,
            float beeGlowRange)
        {
            _beePrefab = beePrefab;
            _beeClip = beeClip;
            _duration = Mathf.Max(0.1f, duration);
            _radius = Mathf.Max(0.1f, radius);
            _height = Mathf.Max(0f, height);
            _speed = Mathf.Max(0.1f, speed);
            _bobAmplitude = Mathf.Max(0f, bobAmplitude);
            _bobSpeed = Mathf.Max(0.1f, bobSpeed);
            _beeScale = Mathf.Max(0.01f, beeScale);
            _spawnTime = Time.time;

            SpawnBees(Mathf.Max(1, beeCount));
            EnsureBeeAudio(beeBuzzClip, buzzVolume, buzzMinDistance, buzzMaxDistance);
            EnsureFireflies(
                Mathf.Max(0, fireflyCount),
                new Vector2(Mathf.Max(0.005f, fireflySizeRange.x), Mathf.Max(fireflySizeRange.x, fireflySizeRange.y)),
                new Vector2(Mathf.Max(0f, fireflySpeedRange.x), Mathf.Max(fireflySpeedRange.x, fireflySpeedRange.y)),
                Mathf.Max(0.1f, fireflyRadius),
                Mathf.Max(0f, fireflyHeight));
            ApplyBeeGlow(beeGlowColor, beeGlowIntensity, beeGlowRange);
        }

        void SpawnBees(int count)
        {
            if (_beePrefab == null)
                return;

            for (int i = 0; i < count; i++)
            {
                var root = new GameObject($"Bee_{i + 1}");
                root.transform.SetParent(transform, worldPositionStays: false);

                float angle = (360f / count) * i + Random.Range(-15f, 15f);
                float radius = _radius * Random.Range(0.65f, 1f);
                float heightOffset = _height * Random.Range(0.3f, 1f);

                root.transform.localPosition = Quaternion.Euler(0f, angle, 0f) * (Vector3.forward * radius) + Vector3.up * heightOffset;

                var visual = Instantiate(_beePrefab, root.transform);
                visual.name = "BeeVisual";
                visual.transform.localPosition = Vector3.zero;
                visual.transform.localRotation = Quaternion.identity;
                visual.transform.localScale = Vector3.one * _beeScale;

                var instance = new BeeInstance
                {
                    Root = root.transform,
                    Visual = visual,
                    Angle = angle,
                    AngularSpeed = _speed * Random.Range(0.75f, 1.25f),
                    Radius = radius,
                    HeightOffset = heightOffset,
                    BobOffset = Random.Range(0f, Mathf.PI * 2f)
                };

                TryPlayClip(visual, instance);
                _bees.Add(instance);
            }
        }

        void EnsureBeeAudio(AudioClip clip, float volume, float minDistance, float maxDistance)
        {
            if (clip == null)
                return;

            _beeAudioSource = gameObject.AddComponent<AudioSource>();
            _beeAudioSource.clip = clip;
            _beeAudioSource.loop = true;
            _beeAudioSource.playOnAwake = false;
            _beeAudioSource.spatialBlend = 1f;
            _beeAudioSource.volume = Mathf.Clamp01(volume);
            _beeAudioSource.minDistance = Mathf.Max(0.1f, minDistance);
            _beeAudioSource.maxDistance = Mathf.Max(_beeAudioSource.minDistance + 0.1f, maxDistance);
            _beeAudioSource.rolloffMode = AudioRolloffMode.Linear;
            _beeAudioSource.Play();
        }

        void ApplyBeeGlow(Color glowColor, float glowIntensity, float glowRange)
        {
            for (int i = 0; i < _bees.Count; i++)
            {
                var bee = _bees[i];
                if (bee.Root == null)
                    continue;

                var halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
                halo.name = "BeeGlowHalo";
                halo.transform.SetParent(bee.Root, worldPositionStays: false);
                halo.transform.localPosition = Vector3.zero;
                halo.transform.localRotation = Quaternion.identity;
                halo.transform.localScale = Vector3.one * Mathf.Max(0.08f, _beeScale * 0.75f);
                var haloCollider = halo.GetComponent<Collider>();
                if (haloCollider != null)
                    Destroy(haloCollider);

                var haloRenderer = halo.GetComponent<Renderer>();
                if (haloRenderer != null)
                {
                    if (haloRenderer.sharedMaterial == null)
                    {
                        var shader = Shader.Find("Universal Render Pipeline/Unlit");
                        if (shader == null)
                            shader = Shader.Find("Unlit/Color");
                        if (shader != null)
                            haloRenderer.sharedMaterial = new Material(shader) { name = "BeeGlowHaloMat" };
                    }

                    var haloMat = haloRenderer.sharedMaterial;
                    if (haloMat != null)
                    {
                        Color haloColor = glowColor;
                        haloColor.a = 0.22f;
                        if (haloMat.HasProperty("_BaseColor"))
                            haloMat.SetColor("_BaseColor", haloColor);
                        if (haloMat.HasProperty("_Color"))
                            haloMat.SetColor("_Color", haloColor);
                    }

                    haloRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    haloRenderer.receiveShadows = false;
                }

                var lightGo = new GameObject("BeeGlow");
                lightGo.transform.SetParent(bee.Root, worldPositionStays: false);
                lightGo.transform.localPosition = Vector3.zero;
                var glowLight = lightGo.AddComponent<Light>();
                glowLight.type = LightType.Point;
                glowLight.range = Mathf.Max(0.01f, glowRange);
                glowLight.intensity = Mathf.Max(0f, glowIntensity * 0.6f);
                glowLight.color = glowColor;
                glowLight.shadows = LightShadows.None;
            }
        }

        void EnsureFireflies(int count, Vector2 sizeRange, Vector2 speedRange, float radius, float height)
        {
            if (count <= 0)
                return;

            var go = new GameObject("PollinationFireflies");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.up * height;
            _fireflyParticles = go.AddComponent<ParticleSystem>();

            var main = _fireflyParticles.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedRange.x, speedRange.y);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
            main.maxParticles = count;
            main.playOnAwake = false;
            main.startColor = new Color(1f, 0.78f, 0.24f, 0.95f);

            var emission = _fireflyParticles.emission;
            emission.rateOverTime = Mathf.Max(4f, count * 1.1f);

            var shape = _fireflyParticles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;

            var noise = _fireflyParticles.noise;
            noise.enabled = true;
            noise.strength = 0.25f;
            noise.frequency = 0.45f;
            noise.scrollSpeed = 0.2f;

            var col = _fireflyParticles.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(CreateFireflyGradient());

            var renderer = _fireflyParticles.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = GetSphereMesh();
            renderer.sharedMaterial = CreatePollinationParticleMaterial();

            _fireflyParticles.Play();
        }

        static Material CreatePollinationParticleMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
                shader = Shader.Find("Particles/Standard Unlit");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                return null;

            var mat = new Material(shader) { name = "PollinationFireflyMat" };
            Color warm = new Color(1f, 0.78f, 0.24f, 0.85f);
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", warm);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", warm);
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(1f, 0.55f, 0.08f, 1f) * 1.8f);
            }
            return mat;
        }

        static Mesh GetSphereMesh()
        {
#if UNITY_EDITOR
            return Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
#else
            var temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var mesh = temp.GetComponent<MeshFilter>() != null ? temp.GetComponent<MeshFilter>().sharedMesh : null;
            Destroy(temp);
            return mesh;
#endif
        }

        static Gradient CreateFireflyGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.82f, 0.3f), 0f),
                    new GradientColorKey(new Color(1f, 0.7f, 0.18f), 0.45f),
                    new GradientColorKey(new Color(0.98f, 0.9f, 0.42f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.2f),
                    new GradientAlphaKey(0.3f, 0.55f),
                    new GradientAlphaKey(1f, 0.8f),
                    new GradientAlphaKey(0f, 1f),
                });
            return gradient;
        }

        void TryPlayClip(GameObject visual, BeeInstance instance)
        {
            if (visual == null || _beeClip == null)
                return;

            var animator = visual.GetComponentInChildren<Animator>();
            if (animator == null)
                animator = visual.AddComponent<Animator>();

            var graph = PlayableGraph.Create($"BeeClip_{visual.GetInstanceID()}");
            graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(graph, "BeeOutput", animator);
            var playable = AnimationClipPlayable.Create(graph, _beeClip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            output.SetSourcePlayable(playable);
            graph.Play();
            instance.Graph = graph;
        }

        void Update()
        {
            if (_bees.Count == 0)
            {
                if (Time.time - _spawnTime >= _duration)
                    Destroy(gameObject);
                return;
            }

            float elapsed = Time.time - _spawnTime;
            if (elapsed >= _duration)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 center = transform.position;
            for (int i = 0; i < _bees.Count; i++)
            {
                var bee = _bees[i];
                if (bee.Root == null)
                    continue;

                bee.Angle += bee.AngularSpeed * Time.deltaTime;
                float radians = bee.Angle * Mathf.Deg2Rad;
                float bob = Mathf.Sin(elapsed * _bobSpeed + bee.BobOffset) * _bobAmplitude;
                Vector3 offset = new Vector3(Mathf.Cos(radians), 0f, Mathf.Sin(radians)) * bee.Radius;
                Vector3 worldPos = center + offset + Vector3.up * (bee.HeightOffset + bob);
                bee.Root.position = worldPos;

                Vector3 tangent = new Vector3(-Mathf.Sin(radians), 0f, Mathf.Cos(radians));
                if (tangent.sqrMagnitude > 0.0001f)
                    bee.Root.rotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);
            }
        }

        void OnDestroy()
        {
            for (int i = 0; i < _bees.Count; i++)
            {
                if (_bees[i].Graph.IsValid())
                    _bees[i].Graph.Destroy();
            }

            _bees.Clear();
        }
    }
}
