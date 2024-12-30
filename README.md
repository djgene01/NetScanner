# NetScanner WIP

**NetScanner** is a lightweight tool built with **.NET 9** and **Terminal.GUI** to quickly scan local subnets for active devices and open ports. Tired of hunting for IP addresses on your LAN? This might help!

---

## Key Features
- **Multithreaded Scanning**: Quickly scans an entire /24 subnet (~254 addresses) in about 15 seconds.
- **Open Port Detection**: Specify which ports to check (e.g., 80, 443) to see if they’re open.
- **Future Enhancements**:
  - Clickable links for HTTP/HTTPS (port 80/443).
  - MAC address lookups.
  - More features as inspiration strikes!

---

## Why This Tool?
Finding devices on a local network can be a chore. NetScanner is my personal solution to instantly discover:
- Which IPs are in use.
- Which services might be running on them.

If it helps you, awesome!

---

## Installation & Usage
1. **Requirements**: Ensure you have the latest [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet) installed.
2. **Clone or Download** this repo.
3. **Build and Run**:
   ```bash
   dotnet build
   dotnet run
