# Contributing to RazeLess 🖱️

Thank you for your interest in RazeLess! We welcome contributions that help make this tool more reliable, efficient, and support more devices.

## 🛡️ Core Principles
* **Zero Bloat**: Keep the memory footprint as small as possible.
* **Performance First**: Low-latency communication and efficient code paths.
* **Privacy First**: No telemetry, no background services, no logins.

These principles should guide every contribution.

---

## 🛠️ How Can I Help?

### 1. Adding Support for New Devices
If you have a Razer mouse that isn't supported yet:
1. Identify your device's **Vendor ID (VID)** and **Product ID (PID)**.
2. Open an issue with these details.
3. If you want to code it yourself, start with:
   `Infrastructure/Hid/HidTransport.cs`

Support for additional DeathAdder variants and other Razer mice is highly welcome.

---

### 2. Button Customization
The button customization UI is already implemented, but the backend hardware-side remapping logic is still being improved.

Help is welcome for:
* low-level button remapping
* key-to-mouse mappings
* media key actions
* browser action support
* persistent remap restore

---

### 3. Lighting Control
The lighting interface is already designed, but HID communication for brightness and restore logic is still under active development.

Help is welcome for:
* brightness packet handling
* idle timeout behavior
* display sleep restore
* reconnect lighting state sync

---

### 4. HyperShift-like Secondary Layer
A major planned feature is a **HyperShift-inspired secondary button layer**, similar to official Synapse.

This feature may include:
* secondary button actions
* hold-to-shift modifier logic
* alternate productivity mappings
* multi-layer gaming shortcuts

Contributors with experience in low-level input systems, Raw Input, Windows hooks, or HID packet analysis can make a huge impact here.

---

### 5. Reporting Bugs
When reporting a bug, please include:
* your mouse model
* Windows version
* the feature you were using
* `deathadder_debug.log` if applicable (found in the application directory)

---

### 6. Submitting Code
* **Fork the repo**
* Create a separate branch for your changes
* Follow standard **C# / .NET conventions**
* Keep pull requests focused and clean
* Ensure your code builds
* Avoid unnecessary files, binaries, or logs

---

## ⚖️ License
By contributing, you agree that your contributions will be licensed under the **GPL-3.0 License**.