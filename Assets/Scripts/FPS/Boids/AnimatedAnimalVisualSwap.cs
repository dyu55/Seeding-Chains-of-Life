using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[DisallowMultipleComponent]
public class AnimatedAnimalVisualSwap : MonoBehaviour
{
    public string resourceModelPath = "Animals/FoxAnimated/Fox";
    public float desiredLocalHeight = 2.1f;
    public float yawOffsetDegrees = 0f;
    public bool destroyExistingVisualChildren = true;
    public bool logWarnings = false;

    private FPSBoidAgent _boid;
    private Transform _visualRoot;
    private Animator _animator;
    private PlayableGraph _graph;
    private AnimationClipPlayable _currentPlayable;
    private AnimationClip _idleClip;
    private AnimationClip _moveClip;
    private AnimationClip _fallbackClip;
    private AnimationClip _currentClip;
    private bool _initialized;
    private bool _usingMoveClip;
    private Vector3 _lastWorldPosition;
    private float _smoothedPlanarSpeed;

    public void ApplyNow()
    {
        if (_initialized)
            return;

        _boid = GetComponent<FPSBoidAgent>();
        _initialized = true;

        var modelPrefab = Resources.Load<GameObject>(resourceModelPath);
        if (modelPrefab == null)
        {
            if (logWarnings)
                Debug.LogWarning($"[AnimatedAnimalVisualSwap] Missing model resource at '{resourceModelPath}'.", this);
            return;
        }

        if (destroyExistingVisualChildren)
        {
            var toDestroy = new List<GameObject>();
            for (int i = 0; i < transform.childCount; i++)
                toDestroy.Add(transform.GetChild(i).gameObject);

            for (int i = 0; i < toDestroy.Count; i++)
                Destroy(toDestroy[i]);
        }

        var instance = Instantiate(modelPrefab, transform);
        instance.name = modelPrefab.name;
        _visualRoot = instance.transform;
        _visualRoot.localPosition = Vector3.zero;
        _visualRoot.localRotation = Quaternion.Euler(0f, yawOffsetDegrees, 0f);
        _visualRoot.localScale = Vector3.one;

        NormalizeVisualScaleAndGrounding();
        SetupAnimation();
        _lastWorldPosition = transform.position;
    }

    private void Update()
    {
        if (!_graph.IsValid() || !_currentPlayable.IsValid() || _moveClip == null || _idleClip == null || _boid == null)
            return;

        Vector3 worldDelta = transform.position - _lastWorldPosition;
        _lastWorldPosition = transform.position;

        float boidPlanarSpeed = new Vector3(_boid.velocity.x, 0f, _boid.velocity.z).magnitude;
        float displacementSpeed = Time.deltaTime > 0.0001f
            ? new Vector3(worldDelta.x, 0f, worldDelta.z).magnitude / Time.deltaTime
            : 0f;

        float targetPlanarSpeed = Mathf.Max(boidPlanarSpeed, displacementSpeed);
        _smoothedPlanarSpeed = Mathf.Lerp(_smoothedPlanarSpeed, targetPlanarSpeed, 8f * Time.deltaTime);

        if (_currentClip != null && _currentPlayable.IsValid() && _currentClip.length > 0.01f)
        {
            double time = _currentPlayable.GetTime();
            if (time >= _currentClip.length)
                _currentPlayable.SetTime(time % _currentClip.length);
        }

        float enterMoveThreshold = Mathf.Max(0.06f, _boid.maxSpeed * 0.10f);
        float exitMoveThreshold = Mathf.Max(0.03f, _boid.maxSpeed * 0.05f);
        bool shouldMove = _usingMoveClip
            ? _smoothedPlanarSpeed > exitMoveThreshold
            : _smoothedPlanarSpeed > enterMoveThreshold;
        if (shouldMove == _usingMoveClip)
            return;

        PlayClip(shouldMove ? _moveClip : _idleClip);
        _usingMoveClip = shouldMove;
    }

    private void OnDisable()
    {
        if (_graph.IsValid())
            _graph.Destroy();
    }

    private void NormalizeVisualScaleAndGrounding()
    {
        if (_visualRoot == null)
            return;

        if (!TryGetRenderableBounds(_visualRoot.gameObject, out Bounds bounds))
            return;

        float height = Mathf.Max(0.001f, bounds.size.y);
        float scale = desiredLocalHeight / height;
        _visualRoot.localScale = Vector3.one * scale;

        if (!TryGetRenderableBounds(_visualRoot.gameObject, out bounds))
            return;

        Vector3 pos = _visualRoot.localPosition;
        pos.y -= bounds.min.y - transform.position.y;
        _visualRoot.localPosition = pos;
    }

    private void SetupAnimation()
    {
        var clips = Resources.LoadAll<AnimationClip>(resourceModelPath);
        if (clips == null || clips.Length == 0)
            return;

        _idleClip = FindBestClip(clips, "idle", "stand", "breathe", "look");
        _moveClip = FindBestClip(clips, "walk", "run", "trot", "jog", "locomotion");
        _fallbackClip = clips[0];

        if (_idleClip == null)
            _idleClip = _fallbackClip;
        if (_moveClip == null)
            _moveClip = _idleClip;

        _animator = _visualRoot != null
            ? _visualRoot.GetComponentInChildren<Animator>(true)
            : null;
        if (_animator == null && _visualRoot != null)
            _animator = _visualRoot.gameObject.AddComponent<Animator>();

        if (_animator == null || _idleClip == null)
            return;

        _graph = PlayableGraph.Create($"{name}_AnimalVisualGraph");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);

        var output = AnimationPlayableOutput.Create(_graph, "Animation", _animator);
        _currentPlayable = AnimationClipPlayable.Create(_graph, _idleClip);
        _currentPlayable.SetApplyFootIK(false);
        _currentPlayable.SetApplyPlayableIK(false);
        _currentPlayable.SetTime(0);
        output.SetSourcePlayable(_currentPlayable);
        _graph.Play();
        _currentClip = _idleClip;
        _usingMoveClip = false;
    }

    private void PlayClip(AnimationClip clip)
    {
        if (clip == null || !_graph.IsValid())
            return;

        if (_currentPlayable.IsValid())
            _currentPlayable.Destroy();

        _currentPlayable = AnimationClipPlayable.Create(_graph, clip);
        _currentPlayable.SetApplyFootIK(false);
        _currentPlayable.SetApplyPlayableIK(false);
        _currentPlayable.SetTime(0);
        var output = (AnimationPlayableOutput)_graph.GetOutput(0);
        output.SetSourcePlayable(_currentPlayable);
        _currentClip = clip;
    }

    private static AnimationClip FindBestClip(IEnumerable<AnimationClip> clips, params string[] keywords)
    {
        if (clips == null)
            return null;

        AnimationClip fallback = null;
        foreach (var clip in clips)
        {
            if (clip == null)
                continue;

            if (fallback == null)
                fallback = clip;

            string clipName = clip.name ?? string.Empty;
            for (int i = 0; i < keywords.Length; i++)
            {
                if (clipName.IndexOf(keywords[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return clip;
            }
        }

        return fallback;
    }

    private static bool TryGetRenderableBounds(GameObject go, out Bounds bounds)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bool found = false;
        bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null)
                continue;

            if (!found)
            {
                bounds = r.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return found;
    }
}
