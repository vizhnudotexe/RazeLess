# RazeLess 🖱️⚡

### A lightweight, privacy-first manager for the Razer DeathAdder Essential (2021)

**RazeLess** is a **portable Windows utility** built specifically for the **Razer DeathAdder Essential 2021 (RZ01-0385)**.

I built this project because I was tired of how unnecessarily heavy modern peripheral software has become.

What should be a simple mouse utility ended up being:

* multiple background services
* high idle RAM usage
* telemetry and data collection
* startup slowdowns
* marketing panels and upsell clutter
* always-running processes for features that should feel instant

For a mouse.

So instead of accepting the bloat, I built my own.

**RazeLess focuses on one thing:**

> giving full control of the DeathAdder Essential with the smallest possible footprint.

No account.
No cloud dependency.
No ads.
No telemetry.
No background waste.

Just fast, direct mouse control.

---

## 💡 Why this project exists

Razer’s official ecosystem offers good hardware, but the software experience felt over-engineered for the wrong reasons.

As someone who cares deeply about:

* performance
* low-latency systems
* clean software architecture
* resource optimization
* user privacy
* hardware-level control

I wanted an application that respected the machine it runs on.

This project started as a personal frustration:

> **why should changing DPI require bloated background software?**

That question turned into:

* USB HID packet capture
* protocol reverse engineering
* Windows device communication
* custom profile persistence
* startup restoration services
* device fingerprinting
* failure recovery systems

What began as a utility became a **systems engineering project around low-level peripheral control**.

---

## 🚀 Features

### 🎯 Performance

* Up to **5 DPI stages**
* DPI range: **200 → 6400**
* Per-stage DPI configuration
* **100 / 500 / 1000 Hz polling**
* Instant profile apply

### 💡 Lighting
> ⚠️ **UI complete — backend hardware implementation in progress**

* Brightness control (**0–100**)
* Disable lighting completely
* Turn off when display sleeps
* Turn off after configurable idle timeout
* Restore on movement

The lighting workflow and UI are already designed, but **device-level HID communication is still being finalized**.

### 🖱️ Customization
> ⚠️ **UI complete — backend remapping logic in progress**

* Remap every supported button except left click
* Disable buttons
* Map keyboard keys
* Mouse-to-mouse remapping
* Media key support
* Browser action support

The full remapping interface is already available, but **button switching and hardware-side apply behavior are still under active development**.

### 📂 Profiles

* Manual profile switching
* Per-task or per-scenario presets
* Last used profile restore
* Per-device profile associations

### ⚙️ Reliability

* HID reconnect detection
* Safe fallback to last stable settings
* Device-specific identification
* Rolling debug logs
* Corruption-safe config writes

---

## 🏗️ Technical Highlights

This project was built as a **real low-level Windows systems project**, not just a UI wrapper.

### Core engineering areas

* **USB HID reverse engineering**
* **Wireshark + USBPcap packet analysis**
* **Feature report transaction design**
* **MVVM-based WPF architecture**
* **Per-device fingerprinting**
* **Portable config persistence**
* **Startup restoration service**
* **Graceful reconnect + failure handling**
* **Low-memory idle architecture**

### Stack

* **C#**
* **WPF (.NET 8)**
* **MVVM**
* **USB HID Feature Reports**
* **JSON / SQLite storage**
* **Windows startup integration**

---

## 🚀 Getting Started

### Prerequisites
* **Windows 10/11**
* **.NET 8.0 Runtime** (to run) or **.NET 8.0 SDK** (to build)

### How to Run
1. **Clone the repository**:
   ```bash
   git clone https://github.com/vizhnudotexe/RazeLess.git
   ```
2. **Build the project**:
   Open a terminal in the project root and run:
   ```powershell
   dotnet build -c Release
   ```
3. **Launch the application**:
   ```powershell
   ./DeathAdderManager/bin/Release/net8.0-windows/DeathAdderManager.exe
   ```
   *Note: Ensure your mouse is plugged in before launching.*

---

## 🎯 Supported Hardware

RazeLess currently supports the following Razer devices:

| Device Model | Product ID (PID) | Notes |
| :--- | :--- | :--- |
| **Razer DeathAdder Essential** | `0x0071` | Legacy Model |
| **Razer DeathAdder Essential (2021)** | `0x0098` | White/2021 Refresh |

*Don't see your device? Feel free to open an issue with your USB VID/PID.*

---

## 🤝 Contributors Welcome

One of the biggest planned upgrades is a **HyperShift-like secondary button layer**, inspired by the official Synapse workflow.

This would enable:
* secondary button actions
* hold-to-shift modifier logic
* alternate productivity bindings
* per-app advanced remaps
* multi-layer gaming shortcuts

Contributors experienced in:
* USB HID
* Windows hooks
* Raw Input
* interception drivers
* low-level remapping
* packet analysis
* reverse engineering Synapse behavior

would be incredibly valuable to help build this feature.

---

## 📌 Project Goals

This project is built around a simple principle:

> **peripheral software should feel invisible**

The software should never be the heaviest thing involved in moving a mouse.

Primary goals:

* tiny memory footprint
* instant startup
* no permanent background services
* fully offline usage
* portable distribution
* clean maintainable architecture
* protocol-level hardware control

---

## 🤝 Why this matters

This project represents something bigger than just replacing one vendor app.

It’s a statement that:

> **good hardware deserves good software**

Users should not need to trade:

* privacy
* performance
* startup speed
* system resources

for basic mouse customization.

RazeLess is my attempt to build the kind of utility I wish shipped with the mouse in the first place.