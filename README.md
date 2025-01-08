# NetScanner 0.02 Alpha

**NetScanner** is a lightweight tool built with **.NET 9** and **Terminal.GUI** to quickly scan local subnets for active devices, open ports, and even trace routes. Tired of hunting for IP addresses on your LAN? This might help!
![NetScanner](https://github.com/user-attachments/assets/64c8b453-eca7-497c-adf5-79f168afe899)

---

## Key Features
- **Multithreaded Scanning**: Quickly scans an entire /24 subnet (~254 addresses) in about 15 seconds.
- **Traceroute Function**: Easily trace the path packets take from your machine to a target host.
- **Open Port Detection**: Specify which ports to check (e.g., 80, 443) to see if they’re open. Clickable URL
- **CSV Export**: Save scan results for later use in a csv file.
- **MAC address lookups**: Find a Mac on your network with ease

- **Future Enhancements**:
  - More features as inspiration strikes!

---

## Why This Tool?
Finding devices on a local network can be a chore. NetScanner is my personal solution to instantly discover:
- Which IPs are in use.
- Which services might be running on them.
- How network traffic travels across multiple hops (via Traceroute).
![Screenshot from 2025-01-08 22-03-44](https://github.com/user-attachments/assets/2ffd9b90-8c7c-4200-8475-04d41cabb5b3)

If it helps you, awesome!

---

## Installation & Usage
1. **Requirements**: Ensure you have the latest [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet) installed.
2. **Clone or Download** this repo.
3. **Build and Run**:
   ```bash
   dotnet build
   dotnet run
