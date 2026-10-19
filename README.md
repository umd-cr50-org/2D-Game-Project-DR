# 2D Game Project – Boss Battle

A 2D action platformer made in Unity. Explore the stage, find the boss, and beat it in a fight inspired by Super Smash Bros.: jab combos, air strings, dashes, magic, a charge-up gauge and an ultimate attack.

**Play it in your browser:** https://umd-cr50-org.github.io/2D-Game-Project-DR/ *(available once the web build is published; see "Publishing" below)*

---

## Controls

| Action | Key |
|---|---|
| Move | Arrow keys / A, D |
| Jump / double jump | Space |
| Dash | Double-tap left or right |
| Attack (combo) | Z (press up to 4 times) |
| Magic projectile (uses gauge) | X |
| Charge the magic gauge | Hold C (standing on the ground) |
| Ultimate (full gauge) | V |
| Pause | Esc |

## How to play

1. Press **Start** on the title screen.
2. Explore the stage until you find the boss. Its health bar appears when the battle starts, and a red arrow at the edge of the screen points to it whenever it's off-screen.
3. Wear the boss down with combos and magic. Fill the gauge by dealing damage, taking damage or holding **C**, then press **V** to unleash your ultimate.
4. Grab the green orbs the boss drops to recover health.
5. Win by bringing the boss's health to zero.

## Features

**The player**
- Up to 4 jabs on the ground (one per press) and a separate 3-hit air string.
- A dash you can cancel into attacks, keeping its momentum.
- Smash-style air limits: one air string and one dash per jump. A second string leaves you helpless until you land.
- Slope-aware movement: slanted platforms work like flat ones.
- Magic projectiles paid for from the magic gauge, plus a gauge you can charge by hand.
- An ultimate blast when the gauge is full.
- Health that blinks red on hits, with knockback and hit stun.

**The boss**
- Uses the same moves as the player, controlled by its own AI.
- Reads your position: it jab-combos up close, dashes in at mid range, casts magic when you run away, and uses a shockwave if you hide underneath it.
- Waits 2 seconds after every attack, and stops casting after two spells in a row so it has to come after you.
- Teleports around the stage at 75%, 50% and 25% health, healing a little each time. It can also channel a heal, which any hit interrupts.
- **Phase 2** at half health: faster, with stronger 3-orb magic.
- Its own ultimate, which deals half your max health. Get out of the red circle!

**Presentation**
- HUD with a health bar, magic gauge, and boss health and ultimate bars.
- Main menu, pause menu (with a controls screen), and victory / defeat screens.
- Music that changes as the fight goes on, voice lines, and sound effects.

## Built with

- **Unity 6** (6000.6.4f1), Universal Render Pipeline (2D)
- **UI Toolkit** for all menus and the HUD (`Assets/UI`)
- C# scripts in `Assets/`

## Opening the project

1. Install **Unity Hub** and Unity **6000.6.4f1** (or the closest Unity 6 version).
2. Clone or download this repository.
3. In Unity Hub, choose **Add → Add project from disk** and select the project folder.
4. Open `Assets/Scenes/MainMenu.unity` (title screen) or `Assets/Scenes/SampleScene.unity` (the stage), then press **Play**.

## Main scripts

| Script | What it does |
|---|---|
| `PlayerController` | Movement for any character: walking on slopes, jumps, dash, air limits, hit stun |
| `PlayerAttack` | Jab and air-string combos, per-attack timing and hitboxes |
| `MagicCaster` / `Projectile` | Magic projectiles |
| `SpecialMeter` / `UltimateAbility` | The magic gauge, charging, and the ultimate blast |
| `Health` / `HealthDropper` / `HealthPickup` | Health, damage, healing orbs |
| `BossAI` | The boss's decisions: attacks, dashes, magic, shockwave, teleports, heals, phase 2 |
| `GameUI` / `MainMenu` | The HUD, menus, end screens and the boss indicator |
| `GameAudio` | Music, voice lines and every sound effect |
| `KeyboardCharacterInput` | Keyboard controls (the boss uses `BossAI` in its place) |

## Publishing

- **Windows:** File → Build Profiles → Windows → Build, into a folder outside the project. Zip that whole folder; it runs on any Windows PC via the `.exe`.
- **Web (GitHub Pages):** File → Build Profiles → Web. Under Player Settings → Publishing Settings, set **Compression Format** to **Disabled**. Build into a `docs/` folder at the root of this repository, commit it, and push to `main`. Then go to the repo's **Settings → Pages**, choose **Deploy from a branch**, and select **main** and **/docs**.

## Credits

Made by **Zeio-dev** for class.
