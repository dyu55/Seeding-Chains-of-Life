SCoL XR Controls (Quest/OpenXR)

Add the component:
- SCoL.XR.SCoLXRInteractor

Suggested setup:
1) In RecoveredScene, select 'XR Origin (VR)'
2) Add Component -> SCoLXRInteractor
3) Set Tracking Origin = XR Origin (VR) (self)
4) Hit Layers: include ground plane / world (default Everything is fine)

Controls:
- Left controller Primary Button: next tool
- Left controller Secondary Button: previous tool
- Right controller Trigger: apply tool at raycast hit

Tools:
- Seed: PlaceSeedAt
- Water: AddWaterAt (waterAmount)
- Fire: IgniteAt (fireFuel)
