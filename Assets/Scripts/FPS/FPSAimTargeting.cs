using UnityEngine;
using SCoL;
using SCoL.Inventory;
using SCoL.Settlement;
using SCoL.Visualization;
using SCoL.XR;
using SCoL.Interaction;

public enum FPSAimTargetKind
{
    None = 0,
    Pickup = 1,
    Harvestable = 2,
    LegacyPlant = 3,
    CAPlant = 4,
    Animal = 5,
    Grabbable = 6,
    SettlementCenterpiece = 7,
    SettlementStorage = 8,
    SettlementBarrier = 9
}

public struct FPSAimTargetInfo
{
    public FPSAimTargetKind kind;
    public Transform root;
    public RaycastHit hit;
    public SCoLPickup pickup;
    public FPSSeedGrowth legacyPlant;
    public FPSBoidAgent animal;
    public SCoLGrabbable grabbable;
    public SCoLSettlementInteractable settlementInteractable;
    public int cellX;
    public int cellY;

    public bool HasActionableTarget
    {
        get
        {
            return kind == FPSAimTargetKind.Pickup
                || kind == FPSAimTargetKind.Harvestable
                || kind == FPSAimTargetKind.LegacyPlant
                || kind == FPSAimTargetKind.CAPlant
                || kind == FPSAimTargetKind.Animal
                || kind == FPSAimTargetKind.Grabbable
                || kind == FPSAimTargetKind.SettlementCenterpiece
                || kind == FPSAimTargetKind.SettlementStorage
                || kind == FPSAimTargetKind.SettlementBarrier;
        }
    }
}

public static class FPSAimTargeting
{
    const float AnimalAimWorldTolerance = 1.2f;
    const float AnimalAimViewportTolerance = 0.16f;
    const float AnimalAimHeightBias = 0.3f;

    public static bool TryResolve(
        Camera cameraSource,
        float maxDistance,
        LayerMask hitMask,
        SCoLRuntime runtime,
        PlantVoxelRenderer plantRenderer,
        out FPSAimTargetInfo info)
    {
        info = default;
        if (!SCoLInteractionInput.TryGetAimRay(cameraSource, out var ray))
            return false;

        bool hasHit = Physics.Raycast(ray, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore);
        if (hasHit)
        {
            info.hit = hit;
            var col = hit.collider;
            var t = col != null ? col.transform : null;

            if (t != null)
            {
                var settlementInteractable = t.GetComponentInParent<SCoLSettlementInteractable>();
                if (settlementInteractable != null)
                {
                    info.settlementInteractable = settlementInteractable;
                    info.root = settlementInteractable.transform;
                    info.kind = settlementInteractable.kind switch
                    {
                        SCoLSettlementInteractableKind.Centerpiece => FPSAimTargetKind.SettlementCenterpiece,
                        SCoLSettlementInteractableKind.Storage => FPSAimTargetKind.SettlementStorage,
                        SCoLSettlementInteractableKind.Barrier => FPSAimTargetKind.SettlementBarrier,
                        _ => FPSAimTargetKind.None
                    };
                    return true;
                }

                var pickup = t.GetComponentInParent<SCoLPickup>();
                if (pickup != null)
                {
                    info.kind = FPSAimTargetKind.Pickup;
                    info.pickup = pickup;
                    info.root = pickup.transform;
                    return true;
                }

                var legacyPlant = t.GetComponentInParent<FPSSeedGrowth>();
                if (legacyPlant != null)
                {
                    info.kind = FPSAimTargetKind.LegacyPlant;
                    info.legacyPlant = legacyPlant;
                    info.root = legacyPlant.transform;
                    return true;
                }

                var animal = t.GetComponentInParent<FPSBoidAgent>();
                if (animal != null)
                {
                    info.kind = FPSAimTargetKind.Animal;
                    info.animal = animal;
                    info.root = animal.transform;
                    return true;
                }

                var grabbable = t.GetComponentInParent<SCoLGrabbable>();
                if (grabbable != null)
                {
                    info.kind = FPSAimTargetKind.Grabbable;
                    info.grabbable = grabbable;
                    info.root = grabbable.transform;
                    return true;
                }

                for (Transform p = t; p != null; p = p.parent)
                {
                    if (p.gameObject.CompareTag("Harvestable"))
                    {
                        info.kind = FPSAimTargetKind.Harvestable;
                        info.root = p;
                        return true;
                    }
                }
            }
        }

        if (TryResolveAnimalNearAim(cameraSource, maxDistance, hitMask, out var fallbackAnimal, out _))
        {
            info.kind = FPSAimTargetKind.Animal;
            info.animal = fallbackAnimal;
            info.root = fallbackAnimal != null ? fallbackAnimal.transform : null;
            return true;
        }

        if (hasHit && runtime != null && runtime.Grid != null && runtime.TryWorldToCell(hit.point, out int x, out int y))
        {
            var cell = runtime.Grid.Get(x, y);
            if (cell != null && cell.HasPlant)
            {
                info.kind = FPSAimTargetKind.CAPlant;
                info.cellX = x;
                info.cellY = y;

                if (plantRenderer != null && plantRenderer.TryGetActivePlantGameObject(x, y, out var plantGO))
                    info.root = plantGO != null ? plantGO.transform : null;
                else
                    info.root = null;

                return true;
            }
        }

        info.kind = FPSAimTargetKind.None;
        info.root = null;
        return hasHit;
    }

    public static bool TryResolveAnimalNearAim(
        Camera cameraSource,
        float maxDistance,
        LayerMask hitMask,
        out FPSBoidAgent animal,
        out Vector3 targetPoint)
    {
        animal = null;
        targetPoint = default;

        if (cameraSource == null || !SCoLInteractionInput.TryGetAimRay(cameraSource, out var ray))
            return false;

        if (Physics.Raycast(ray, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
        {
            animal = hit.collider != null ? hit.collider.GetComponentInParent<FPSBoidAgent>() : null;
            if (animal != null)
            {
                targetPoint = GetAnimalAimPoint(animal);
                return true;
            }
        }

        float bestScore = float.PositiveInfinity;
        var activeAgents = FPSBoidAgent.ActiveAgentsView;
        if (activeAgents == null)
            return false;

        for (int i = 0; i < activeAgents.Count; i++)
        {
            var candidate = activeAgents[i];
            if (candidate == null || !candidate.isActiveAndEnabled)
                continue;

            Vector3 aimPoint = GetAnimalAimPoint(candidate);
            Vector3 toCandidate = aimPoint - ray.origin;
            float alongRay = Vector3.Dot(ray.direction, toCandidate);
            if (alongRay < 0.05f || alongRay > maxDistance + AnimalAimWorldTolerance)
                continue;

            Vector3 nearestPoint = ray.origin + ray.direction * alongRay;
            float worldOffset = Vector3.Distance(nearestPoint, aimPoint);

            Vector3 viewport = cameraSource.WorldToViewportPoint(aimPoint);
            if (viewport.z <= 0f)
                continue;

            float viewportOffset = Vector2.Distance(
                new Vector2(viewport.x, viewport.y),
                new Vector2(0.5f, 0.5f));

            if (worldOffset > AnimalAimWorldTolerance && viewportOffset > AnimalAimViewportTolerance)
                continue;

            float score = worldOffset * 0.9f + viewportOffset * 7.5f + alongRay * 0.02f;
            if (score >= bestScore)
                continue;

            bestScore = score;
            animal = candidate;
            targetPoint = aimPoint;
        }

        return animal != null;
    }

    static Vector3 GetAnimalAimPoint(FPSBoidAgent animal)
    {
        if (animal == null)
            return default;

        var col = animal.GetComponent<Collider>();
        if (col != null)
            return col.bounds.center;

        return animal.transform.position + Vector3.up * AnimalAimHeightBias;
    }
}
