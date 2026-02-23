using UnityEngine;
using SCoL;
using SCoL.Inventory;
using SCoL.Visualization;
using SCoL.XR;

public enum FPSAimTargetKind
{
    None = 0,
    Pickup = 1,
    Harvestable = 2,
    LegacyPlant = 3,
    CAPlant = 4,
    Animal = 5,
    Grabbable = 6
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
                || kind == FPSAimTargetKind.Grabbable;
        }
    }
}

public static class FPSAimTargeting
{
    public static bool TryResolve(
        Camera cameraSource,
        float maxDistance,
        LayerMask hitMask,
        SCoLRuntime runtime,
        PlantVoxelRenderer plantRenderer,
        out FPSAimTargetInfo info)
    {
        info = default;
        if (cameraSource == null)
            return false;

        var ray = cameraSource.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
            return false;

        info.hit = hit;
        var col = hit.collider;
        var t = col != null ? col.transform : null;

        if (t != null)
        {
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

        if (runtime != null && runtime.Grid != null && runtime.TryWorldToCell(hit.point, out int x, out int y))
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
        return true;
    }
}
