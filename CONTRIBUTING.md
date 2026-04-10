# Contributing to RazeLess 🖱️

Thank you for your interest in RazeLess! We welcome contributions that help make this tool more reliable, efficient, and support more devices.

## 🛡️ Core Principles
*   **Zero Bloat**: Keep the memory footprint as small as possible.
*   **Privacy First**: No telemetry, no background services, no logins.
*   **Performance**: Low-latency communication and efficient code.

## 🛠️ How Can I Help?

### 1. Adding Support for New Devices
If you have a Razer mouse that isn't supported yet:
1.  Identify your device's **Vendor ID (VID)** and **Product ID (PID)**.
2.  Open an issue with these details.
3.  If you want to code it yourself, look at `HidDeviceFactory` in `Infrastructure/Hid/HidTransport.cs`.

### 2. Reporting Bugs
When reporting a bug, please include:
*   Your mouse model.
*   Windows version.
*   The `deathadder_debug.log` if applicable (found in the application directory).

### 3. Submitting Code
*   **Fork the repo**: Create a separate branch for your changes.
*   **Coding Style**: Please follow standard C# / .NET conventions.
*   **Clean PRs**: Ensure your code builds and doesn't include unnecessary files or logs.

## ⚖️ License
By contributing, you agree that your contributions will be licensed under the **GPL-3.0 License**.
