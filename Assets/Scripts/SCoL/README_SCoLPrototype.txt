SCoL Prototype (MVP)

1) Create a config:
   Assets -> Create -> SCoL -> Config

2) In a scene, create an empty GameObject and add SCoLBootstrap.
   Assign the config asset.

3) Press Play.

Interaction (temporary): call these methods on SCoLRuntime from XR interactables / UnityEvents:
- PlaceSeedAt(worldPos)
- AddWaterAt(worldPos, amount)
- IgniteAt(worldPos, fuel)
- StompAt(worldPos, damage)
