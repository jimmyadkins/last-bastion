# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Last Bastion is a **Unity 6 (2024 LTS, version 6000.4.0f1)** tower defense game. Players place turrets and walls during a building phase, then survive waves of "Swarmer" enemies attacking an HQ.

## Build & Run

Open the project in Unity Hub using Unity 6000.4.0f1. No CLI build commands — use the Unity Editor directly.

**Scenes** (in `Assets/Scenes/`):
- `Game.unity` — main game scene (start here)
- `MainMenu.unity` — main menu
- `UI.unity` — UI overlay scene

## Architecture

### Singleton Managers

All major systems are singletons wired through Unity's Inspector (not `FindObjectOfType`). Key managers:

| Manager | Responsibility |
|---|---|
| `GameManager` | Game state machine, wave lifecycle |
| `GridManager` | Building grid — tracks cell occupancy |
| `BuildingManager` | Placement, preview, and selling |
| `SwarmerManager` | Enemy AI and spatial grid |
| `TurretManager` | Turret registry and update loop |
| `PlayerMoney` | Economy |
| `LevelManager` | Level data loading and progression |
| `PrefabManager` | Central prefab registry |

### Event System (Switchboard)

`Switchboard.cs` is a static event hub. UI and systems subscribe here rather than holding direct references. Key events: `OnWaveStart`, `OnWaveEnd`, `OnWin`, `OnLose`, `OnHQHealthChanged`, `OnWaveTimeChanged`, volume events.

### Game State Machine

`GameManager` drives a `GameState` enum:
- `Building` — player places turrets/walls; purchasing UI is active
- `InWave` — enemies spawned; turrets fire; wave timer counts down
- `LevelOver` — wave complete; transitions to next wave or end screen

### Grid Systems

Two overlapping grids (constants in `Defines.cs`):
- **Building grid**: 2-unit cells (`BuildingGridCellSize`) — tracks where buildings are placed
- **Enemy grid**: 6-unit cells (`EnemyGridCellSize`) — spatial hash for O(1) neighbor queries in swarm AI

`IGridElement` and `IBuilding` are the key interfaces for anything that occupies grid cells.

### Building System

`BuildableObject` is the base class for all placeable buildings. Buildings validate placement against `GridManager`, support preview material swapping (valid/invalid), and refund 50% on sell. Walls have special drag-to-build multi-cell placement.

Building types: Cannon, Gatling, Artillery, Railgun, Wall, HQ (non-sellable, 2×2), Obstacle, InvincibleWall.

### Enemy AI (Swarmer)

`SwarmerManager` runs a flocking update: target weight (toward HQ) + alignment + separation + obstacle-avoidance whisker raycasts. `SwarmerController` handles per-enemy health, HQ collision damage, and death VFX.

Enemy systems live in `Assets/_Game/Temporary/` — these are flagged for refactoring but are the active implementation.

### Constants

All shared constants (layer masks, grid sizes, turret aim tolerance, damage values) are in `Assets/_Game/Defines.cs`. Check here before hardcoding magic numbers.

## Key File Locations

- Game constants: `Assets/_Game/Defines.cs`
- Game state / wave logic: `Assets/_Game/Behavior/GameManager.cs`
- Events hub: `Assets/_Game/Behavior/Switchboard.cs`
- Level data (ScriptableObject): `Assets/_Game/Behavior/Grid/LevelData.cs`
- Building placement: `Assets/_Game/Behavior/Buildings/BuildingManager.cs`
- Enemy swarm AI: `Assets/_Game/Temporary/SwarmerManager.cs`
- Turret targeting: `Assets/_Game/Behavior/Turrets/TurretController.cs`
