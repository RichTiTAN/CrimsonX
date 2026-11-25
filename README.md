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

##  Security and trust

The default connection path is not a tunnel you control. CrimsonX scrapes **publicly published proxy configurations** from remote sources, tests them, and load-balances your traffic across whichever ones currently answer.

- Those servers belong to third parties. CrimsonX does not run them, and has **no control over how they are configured, who operates them, what they log, or whether they are safe**.
- A scraped node can be misconfigured, malicious, or run by someone with an interest in what passes through it. It carries your traffic, and it is the only thing standing between your connection and the destination: it sees your address, the destinations you reach, your DNS queries and any traffic that is not TLS, and it can log all of it.
- Treat the default path as fine for casual browsing, not as a privacy guarantee.

If what you do needs to be trustworthy, use infrastructure you control:

- **Exit node** — hand CrimsonX your own server as the exit. Traffic still travels over the scraped nodes, but it leaves through your server under the protocol you configured — choose an encrypted one (VLESS+REALITY/TLS, Trojan, Hysteria2, WireGuard) — so the public hop only carries an encrypted stream to it. That hop can still see that you connect and how much data moves, but not the content.
- **Custom config** — paste your own VLESS/VMess/Trojan/Shadowsocks, WireGuard, OpenVPN, IKEv2 or AmneziaWG configuration, either from a provider you trust or from a server you run. A custom config never touches the scraped pool.
- **Your own server as the first hop, with split routing or WARP**, if that fits how you work.

> **Please do not use the default connection for sensitive work.** Without an exit node or a custom config, the operator of the node you land on learns your address, the sites and services you reach, and anything sent without TLS — and can log or act on it. Banking, work accounts and private messages belong on a server you run yourself: use it as the exit node, or import it as a custom config.

##  Screenshots

<img width="694" height="552" alt="image" src="https://github.com/user-attachments/assets/44f37838-2a44-4aec-a0ea-4f48e6c47334" />


##  Installation

1. Go to the [Releases](https://github.com/RichTiTAN/CrimsonX/releases) page.
2. Download the latest `CrimsonX.zip`.
3. Extract the folder to your preferred location.
4. Run `CrimsonX.exe`.

*Note: CrimsonX requires Windows 10 or newer.*

> **On Linux?** CrimsonX also runs natively on Linux — see the [CrimsonX-Linux repository](https://github.com/RichTiTAN/CrimsonX-Linux).

###  Source availability

The source for CrimsonX 3.3.4 and later is published in this repository, so you can read exactly what the app does and build it yourself. It is not open source: it is licensed under the [PolyForm Strict License 1.0.0](LICENSE), which permits personal and other noncommercial use, and does not permit distributing the software or publishing modified versions.

| | |
| --- | --- |
| Read the code | yes |
| Build and run it unmodified, for personal or other noncommercial use | yes |
| Publish a fork or a patched build | no |
| Use it commercially | ask first: CrimsonVPN.Support@proton.me |
| Official release builds | governed by the [CrimsonX EULA](EULA.txt), which also covers business use |

Versions up to and including 3.3.3 were released under the **GNU General Public License (v3)**. That grant is not revoked, and their source stays available in the matching release tags and releases.

###  Building from source

You need the .NET 10 SDK. Private worker URLs and a built-in encryption key live in a **gitignored** file (`Services/AppSecrets.cs`), so create it from the template before building:

```
copy Services\AppSecrets.cs.example Services\AppSecrets.cs
```

Then open `Services/AppSecrets.cs`, fill in your own worker URLs plus a freshly generated 32-byte cache key and 16-byte IV, and build:

```
dotnet build CrimsonX.slnx
```

The proxy engines (`Data\xray`, `Data\sing_box`, `Data\awg`, `Data\aether`, `Data\ipsec`) are not part of the repository: the release package ships prebuilt binaries. Without them the app starts, but it cannot tunnel. The engine build scripts and the test suite are not part of this repository.

At runtime the stores in `Data\cache\*.bin` are encrypted with a 32-byte key that is generated on first use, wrapped with Windows DPAPI for the current Windows user, and kept in `Data\cache\data.key`. The key in `AppSecrets.cs` is only used to read stores written by older builds and as the fallback when DPAPI is unavailable, so an upgrade keeps your settings and saved credentials.

##  Configuration & Usage

### 1. Connection Modes
- **VPN Mode:** Uses sing-box to create a virtual network interface (TUN), forcing all system traffic through the proxy.
- **Proxy Mode:** Sets the Windows System Proxy settings to route standard web traffic.
- **Clear Proxy Mode:** Disables the system proxy settings, allowing direct connection to the internet. while exposing the proxy to apps that support manual proxy configuration.

### 2. Custom Configs
If you don't want to rely on the automated scrapers, you can inject up to 2 of your own configs directly in the Settings tab: VLESS/VMESS/Shadowsocks/Socks share links, xray/sing-box outbound JSON, or full **OpenVPN** (`.ovpn`, with inline `<ca>`/`<tls-crypt>`/`<tls-auth>` blocks), **WireGuard** configs (`.conf`, `wireguard://` share links, sing-box endpoint JSON or xray `wireguard` outbound JSON) and **IKEv2** profiles (profile JSON, `ikev2://` share links, `.mobileconfig` files, or the **IKEV2** button under the custom configs).

Just paste them in (**Ctrl+V**), press the **+** button to import a file, or pick one of your saved configs.

Every entry in **SAVED CONFIGS** can be copied back out - the copy icon on its row, or a click on the row itself - and you get the config exactly as you imported it, `.ovpn` comments and `.conf` keys included. A remembered OpenVPN username and password are never part of that text: they are kept separately in the app's encrypted credential store and only attached to the endpoint when the tunnel starts.
The same configs can also be used per app in the **Apps & Games** tab (custom proxy per rule). 

The **CUSTOM CONFIGS** section also carries a **PER-CONFIG ADAPTER** switch. With it on, each of the two boxes gets its own adapter picker and each config leaves through the adapter it was given.

The panel's third switch, **OBFUSCATION**, sends a burst of decoy datagrams before every OpenVPN / WireGuard handshake so the tunnel never opens with a recognisable first packet (UDP only, TCP configs are left alone; the server drops the decoys). Like its two neighbours it is staged and waits for **SUBMIT**.

**AmneziaWG** configs (`.conf` or a sing-box `awg` endpoint JSON, with `Jc`/`Jmin`/`Jmax`, `S1`-`S4`, `H1`-`H4`, `I1`-`I5`, `Hpk`, `Cpa`, the AWG 3 timings and `RandomTrailers`/`DisableCookies`) run on a second engine, `Data\awg\amnezia-box.exe` - Amnezia's sing-box fork - while every other config keeps using the bundled sing-box. They work in the custom config boxes and in the Apps & Games rules, and they are refused as an exit node. A `DNS =` line in an AmneziaWG config is honoured inside that tunnel (names it resolves go to the config's resolver, through the tunnel); plain WireGuard and OpenVPN configs still leave DNS to CrimsonX.

### 3. Load Balance Policies
Head to the **Settings** tab to adjust how CrimsonX distributes connections. If you're downloading large files, **Least Ping** or **Round Robin** is recommended.  

### 4. Seamless Swap
The app constantly checks if your current configs are working and if they are not it will seamlessly replace them with working configs without disconnecting you from the internet.

### 5. Apps & Games Profile Routing
The **Apps & Games** tab provides pre-configured routing profiles for popular games (Valorant, CS2, League of Legends, etc.) and applications (Discord, Telegram). You can seamlessly route TCP and UDP traffic independently, bind them to specific network adapters, or force regional matchmaking without affecting your global system VPN.  

### 6. WARP
If the need arises you can enable WARP from the main page of the app and route all the app's configs through Cloudflare WARP for better connectivity.

### TROUBLESHOOTING:
- Check the [Documentation file](https://github.com/RichTiTAN/CrimsonX/blob/main/Documentation.md) for detailed instructions.
- Share-link and JSON formats for MASQUE, WireGuard, IKEv2 and OpenVPN are in the [Transports file](https://github.com/RichTiTAN/CrimsonX/blob/main/Transports.md).

##  License

CrimsonX 3.3.4 and later are source available. The source code is licensed under the [PolyForm Strict License 1.0.0](LICENSE); the builds published on the Releases page are licensed under the [CrimsonX End User License Agreement](EULA.txt), and by installing or using those builds you agree to that EULA. Commercial use, redistribution or any other licensing: **CrimsonVPN.Support@proton.me**.

Versions up to and including 3.3.3 were released under the **GNU General Public License (v3)**. Their rights are not revoked, and their corresponding source remains available in the matching release tags and releases.

Third-party components remain under their own licenses: see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) and the `Licenses` folder.

---
<div align="center">
  <i>Developed with ❤️ by RichTiTAN</i>
</div>

# Credits and Donations  
Creator: [@itsTiTANVPN](https://t.me/itsTitanVPN)  
Contact: CrimsonVPN.Support@proton.me

__Credits:__  
xray: https://github.com/xtls/xray-core  
Sing_Box: https://github.com/SagerNet/sing-box  
Avalonia: https://github.com/avaloniaui  
Aether: https://github.com/CluvexStudio/Aether  
go-ipsec: https://github.com/n0madic/go-ipsec  
awg: https://github.com/amnezia-vpn/amnezia-box

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
