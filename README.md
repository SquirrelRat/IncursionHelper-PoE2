# IncursionHelper-PoE2

A dedicated helper plugin for **ExileCore2** to enhance your Incursion Temple experience in Path of Exile 2. It assists with both the **Temple Planning** phase and the active **Incursion Altar** encounters.

---

## Features

### 1. Temple Planning
When using the Temple Console to build your temple (before activation), the plugin helps you choose the best room cards. It reads the cards and displays the corresponding Room Name or Reward directly on the UI.

<img width="808" alt="Altar Progress Lines" src="https://github.com/user-attachments/assets/ebc58e9b-0af5-4da5-b9ee-1d3b8f4b5945" />

---

### 2. Incursion Altar Progress
During the Incursion encounter, the plugin visualizes the Altar activation progress:
*   **Connection Lines:** Red and Green lines connect pedestals to show the activation sequence and progress.
*   **Progress Counter:** Displays a clear text counter (e.g., "Incursion: 2/6") near the Altar to track how many pedestals have been activated.
*   **Pedestal Status:** Highlights individual pedestals to show if they are **Queued** (Next) or **Activated**.

<p float="left">
  <img src="https://github.com/user-attachments/assets/555f2314-4ea3-4450-80fd-937110e2d709" width="45%" />
  <img src="https://github.com/user-attachments/assets/e0d7ae46-058a-427e-976d-684cedb8225e" width="45%" />
</p>

---

### 3. Reward & Item Highlights
*   **Reward Benches:** Identifies and labels in-world benches (like the Corruption Altar or Gemcutter) so you don't miss them.
*   **Medallions:** Highlights specific ground items like Medallions to ensure they aren't lost in the clutter.

<img width="213" alt="Item Highlight" src="https://github.com/user-attachments/assets/16fb6627-40d6-4b49-9447-e6768c1540f1" />

---

## Installation

1.  Download the plugin source.
2.  Place the `IncursionHelper-PoE2` folder into your `ExileCore2/Plugins/Source/` directory.
3.  Start (or restart) **ExileCore2**.
4.  Enable the plugin in the settings menu (**F12**).

## Configuration

Open the ExileCore2 menu (Default: **F12**) and navigate to **IncursionHelper**.

*   **Visual Settings:** Toggle Circles, Connections, Numbers, and Rewards.
*   **Scaling:** Adjust the size of text, circles, and line thickness to match your resolution.
*   **Colors:** Fully customizable colors for every state (Activated, Queued, Rewards, etc.).
