<div align="center">
  <img src="Assets/CrimsonX.ico" width="128" height="128" alt="CrimsonX Logo">
  
  # CrimsonX

  **A GUI VPN client that fetches, tests and load-balances multiple xray configs suited for your network.**  
  *Automated config scraping, intelligent load-balancing, and seamless tunneling for Windows.*
</div>

---

##  Overview

**CrimsonX** is an advanced proxy/vpn client for Windows built with C# and Avalonia UI. It takes advantage of the powerful **Xray-core** and **sing-box** engines under the hood, wrapping them in a beautiful, highly animated, and user-friendly interface.

Unlike standard clients, CrimsonX features a **dynamic pipeline** that constantly pulls, tests, and caches the fastest configurations in the background. It automatically load-balances traffic across multiple nodes to ensure uninterrupted, high-speed connectivity.

##  Key Features

-  **Seamless Proxy and VPN integration:** Uses `Xray-core` for proxying and load-balancing, and `sing-box` for seamless system-wide VPN Mode (TUN).
-  **Seamless Swapping mechanism:** Actively checking and swapping dead configs for healthy ones.
-  **Intelligent Load Balancing:** Distribute traffic using multiple policies:
      - Round Robin: Evenly distributes connections across all active nodes.
      - Least Ping: Routes traffic through the node with the lowest latency.
      - Least Load: Dynamically selects the node with the fewest active connections.
      - Random: Picks a node at random for statistical distribution.
-  **Dynamic Config Pipeline:** Automatically scrapes, background-tests, and caches working proxy configurations from remote workers, prioritizing cached configs on the next startup.
-  **Advanced Split Tunneling:** Fine-tune your routing rules:
      - Exclude specific continents (Geo-IP based routing).
      - Enable Ad-Blocker to filter malicious and tracking requests.
      - Bypass proxy for specific apps or IPs (Direct UDP support).
-  **Apps & Games Routing:** A dedicated hub to manage TCP/UDP routing, adapter binding, and region matching for specific applications and games.
-  **LAN Sharing:** Share your VPN connection over the local network, with optional Username/Password authentication.
-  **DNS Control:** Built-in support for secure DNS-over-HTTPS (DoH) and customizable System DNS fallbacks. Name resolution follows the traffic path - proxied apps resolve through the node, direct/bypassed apps resolve directly, so a dead node cannot take down DNS for the traffic that was never proxied.

##  Screenshots

<img width="696" height="554" alt="Screenshot 2026-10-04 155541" src="https://github.com/user-attachments/assets/7b093fdb-5d37-4ee0-a75c-efadf7aa86b9" />


##  Installation

1. Go to the [Releases](https://github.com/RichTiTAN/CrimsonX/releases) page.
2. Download the latest `CrimsonX.zip`.
3. Extract the folder to your preferred location.
4. Run `CrimsonX.exe`.

*Note: CrimsonX requires Windows 10 or newer.*

###  Building from source
Private worker URLs and the local cache-encryption key live in a **gitignored** file (`Services/AppSecrets.cs`) so they never end up in the public repository. After cloning, create it from the template before building:

```
copy Services\AppSecrets.cs.example Services\AppSecrets.cs
```

Then open `Services/AppSecrets.cs` and fill in your own worker URLs plus a freshly generated 32-byte cache key and 16-byte IV.

##  Configuration & Usage

### 1. Connection Modes
- **VPN Mode:** Uses sing-box to create a virtual network interface (TUN), forcing all system traffic through the proxy.
- **Proxy Mode:** Sets the Windows System Proxy settings to route standard web traffic.
- **Clear Proxy Mode:** Disables the system proxy settings, allowing direct connection to the internet. while exposing the proxy to apps that support manual proxy configuration.

### 2. Custom Configs
If you don't want to rely on the automated scrapers, you can inject up to 2 of your own configs directly in the Settings tab: VLESS/VMESS/Shadowsocks/Socks share links, xray/sing-box outbound JSON, or full **OpenVPN** (`.ovpn`, with inline `<ca>`/`<tls-crypt>`/`<tls-auth>` blocks) and **WireGuard** configs (`.conf`, `wireguard://` share links, sing-box endpoint JSON or xray `wireguard` outbound JSON).

Just paste them in (**Ctrl+V**), press the **+** button to import a file, or pick one of your saved configs: the box keeps the config behind the scenes and only shows its title, so a multi-line `.ovpn` can no longer be clipped by the single-line box.

OpenVPN and WireGuard configs are not proxied by xray directly: CrimsonX runs a second, headless sing-box instance that owns the `openvpn-client` / `wireguard` endpoint and exposes a local socks inbound on a free loopback port. xray then dials that socks inbound as one of its `proxy-node` outbounds, so the config takes part in the normal balancer, speed test, split tunnel and watchdog logic. The tunnel port is picked automatically on every connect (never hardcoded, never exposed to the LAN) and is written to the log as `[Tunnel] openvpn <server> → socks 127.0.0.1:<port>`.

The same configs can also be used per app in the **Apps & Games** tab (custom proxy per rule). Those run in their own sing-box tunnel process, kept separate from the Custom Configs tunnels, and only apply in VPN Mode — and when the same config + adapter is already running as a custom config, the rule reuses that engine instead of starting a second process.

### 3. Load Balance Policies
Head to the **Settings** tab to adjust how CrimsonX distributes connections. If you're downloading large files, **Least Ping** or **Round Robin** is recommended.  

### 4. Seamless Swap
The app constantly checks if your current configs are working and if they are not it will seamlessly replace them with working configs without disconnecting you from the internet.

### 5. Apps & Games Profile Routing
The **Apps & Games** tab provides pre-configured routing profiles for popular games (Valorant, CS2, League of Legends, etc.) and applications (Discord, Telegram). You can seamlessly route TCP and UDP traffic independently, bind them to specific network adapters, or force regional matchmaking without affecting your global system VPN.

### TROUBLESHOOTING:
- Check the [Documentation file](https://github.com/RichTiTAN/CrimsonX/blob/main/Documentation.md) for detailed instructions.

##  License

This program is free software: you can redistribute it and/or modify it under the terms of the **GNU General Public License (v3)** as published by the Free Software Foundation.

See the [LICENSE](LICENSE) file for more details.

---
<div align="center">
  <i>Developed with ❤️ by RichTiTAN</i>
</div>

# Credits and Donations  
Creator: [@itsTiTANVPN](https://t.me/itsTitanVPN)  

__Credits:__  
xray: https://github.com/xtls/xray-core  
Sing_Box: https://github.com/SagerNet/sing-box  
Avalonia: https://github.com/avaloniaui

__Donations:__ 
- If you want to support the project or me you can do so by sending your desired amount to one of these wallet addresses:

USDT (BEP20)  
`0xFc1d71C22DC2604f6C13Ca540ed842535cbE6d75`

USDT (TRC20)  
`TNMaNGDMG7BzbjkXeiguFWzDHZ4hCUU9R8`

BITCOIN  
`bc1quzdzuhrfse520r0wkqgkvsl7nv354r8sj5u9f9`

TON  
`UQCB3bvk4nXMiokNWprb7CwPjurU8WOUfVSgAVXMMke0CxdW`
