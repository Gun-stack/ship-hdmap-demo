# Third-party assets

| Asset | Where it is used | Source | License |
| --- | --- | --- | --- |
| Car Kit 3.1 — `hatchback-sports.obj`, `colormap.png` | The passenger car (driven and parked). `Editor/CarBake.cs` bakes the OBJ into `Assets/ShipHdMap/Resources/Vehicle/Car.asset` | Kenney, https://kenney.nl/assets/car-kit | CC0 1.0 — [`Vehicles/Kenney-Car-Kit-License.txt`](Vehicles/Kenney-Car-Kit-License.txt) |
| JetBrains Mono NL Regular 2.304 | The in-scene HUD (`Assets/ShipHdMap/Resources/Fonts/`) | JetBrains, https://github.com/JetBrains/JetBrainsMono | SIL Open Font License 1.1 — [`Fonts/JetBrainsMono-OFL.txt`](Fonts/JetBrainsMono-OFL.txt) |

The source OBJ sits here, outside `Assets/`, so Unity never imports it as a model of its own; only the baked mesh ships.
