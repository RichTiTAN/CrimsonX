<div align="center">
  <img src="Assets/CrimsonX.png" width="128" height="128" alt="CrimsonX Logo">
  
  # CrimsonX

  **A GUI VPN client that fetches, tests and load-balances multiple xray configs suited for your network.**  
  *Automated config scraping, intelligent load-balancing, and seamless tunneling for Windows.*
</div>

---

## 🌟 Overview

**CrimsonX** is an advanced proxy/vpn client for Windows built with C# and Avalonia UI. It takes advantage of the powerful **Xray-core** and **sing-box** engines under the hood, wrapping them in a beautiful, highly animated, and user-friendly interface.

Unlike standard clients, CrimsonX features a **dynamic pipeline** that constantly pulls, tests, and caches the fastest configurations in the background. It automatically load-balances traffic across multiple nodes to ensure uninterrupted, high-speed connectivity.

## ✨ Key Features

- 🚀 **Seamless Proxy and VPN integration:** Uses `Xray-core` for proxying and load-balancing, and `sing-box` for seamless system-wide VPN Mode (TUN).
- ⚖️ **Intelligent Load Balancing:** Distribute traffic using multiple policies:
  - **Round Robin:** Evenly distributes connections across all active nodes.
  - **Least Ping:** Routes traffic through the node with the lowest latency.
  - **Least Load:** Dynamically selects the node with the fewest active connections.
  - **Random:** Picks a node at random for statistical distribution.
- 🔄 **Dynamic Config Pipeline:** Automatically scrapes, background-tests, and caches working proxy configurations from remote workers, prioritizing cached configs on the next startup.
- 🛡️ **Advanced Split Tunneling:** Fine-tune your routing rules:
  - Exclude specific continents (Geo-IP based routing).
  - Enable Ad-Blocker to filter malicious and tracking requests.
  - Bypass proxy for specific apps or IPs (Direct UDP support).
- 🌍 **Multi-Language & Theming:** Full RTL support for Persian (`فارسی`), and 5 beautiful gradient themes (Crimson, Blue, Purple, Green, Pink, Yellow).
- 📡 **LAN Sharing:** Share your VPN connection over the local network, with optional Username/Password authentication.
- 🔒 **DNS Control:** Built-in support for secure DNS-over-HTTPS (DoH) and customizable System DNS fallbacks.

## 📸 Screenshots

*(Add screenshots of your application here)*

## 📥 Installation

1. Go to the [Releases](https://github.com/RichTiTAN/CrimsonX/releases) page.
2. Download the latest `CrimsonX.zip`.
3. Extract the folder to your preferred location.
4. Run `CrimsonX.exe`.

*Note: CrimsonX requires Windows 10 or newer.*

## ⚙️ Configuration & Usage

### 1. Connection Modes
- **VPN Mode:** Uses sing-box to create a virtual network interface (TUN), forcing all system traffic through the proxy.
- **Proxy Mode:** Sets the Windows System Proxy settings to route standard web traffic.
- **Clear Proxy Mode:** Disables the system proxy settings, allowing direct connection to the internet. while exposing the proxy to apps that support manual proxy configuration.

### 2. Custom Configs
If you don't want to rely on the automated scrapers, you can inject up to 2 of your own VLESS/VMESS/Shadowsocks configs directly in the Settings tab.

### 3. Load Balance Policies
Head to the **Settings** tab to adjust how CrimsonX distributes connections. If you're downloading large files, **Least Load** or **Round Robin** is recommended. For gaming or VoIP, **Least Ping** ensures the lowest latency.

### TROUBLESHOOTING:
- Check the [Documentation file](./DOCUMENTATION.md) for detailed instructions.

## 📜 License

This program is free software: you can redistribute it and/or modify it under the terms of the **GNU General Public License (v3)** as published by the Free Software Foundation.

See the [LICENSE](LICENSE) file for more details.

---
<div align="center">
  <i>Developed with ❤️ by RichTiTAN</i>
</div>

# Credits and Donations  
Creator: [@itsTiTANVPN](https://t.me/itsTitanVPN)  

__Credits:__  
HAProxy: https://github.com/xjoker/HAProxyForWindows  
xray: https://github.com/xtls/xray-core  
Tor: https://www.torproject.org/  
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
