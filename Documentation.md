# CrimsonX - Comprehensive User Manual

Welcome to the **CrimsonX** User Manual! This document provides an in-depth explanation of every feature, tab, and setting available in the application.

---

## 1. Home Tab
The Home tab is the main dashboard of CrimsonX, giving you quick access to connection controls and real-time monitoring.

### 🔴 The Connect Button
The pulsating core of the application. Clicking this button initiates the dynamic connection pipeline:
1. **Config Scraping & Testing**: CrimsonX silently pulls fresh configurations from remote worker nodes or reads your cached configs.
2. **Speed & Latency Checks**: It tests these configurations in the background to ensure they are working and ranks them by speed.
3. **Engine Startup**: Once the best nodes are selected, the Xray-core engine is started. If you are in VPN Mode, sing-box is also initialized to capture all system traffic via a TUN interface.

### ⚡ Quick Settings Panel
Located on the Home screen, this panel provides fast access to frequently toggled options without having to dig into the Settings tab. The available quick toggles are:
- **DIRECT UDP**
- **XRAY EXIT-NODE**
- **BIND ADAPTER**
- **DOH**
- **SYSTEM DNS**
- **AD BLOCKER**
- **LAN CONNECTIONS**
- **LAUNCH ON START-UP**
- **AUTO-CONNECT**
- **START MINIMIZED**
- **MINIMIZE TO TRAY**
- **EXCLUDE LOCATIONS**
- **CUSTOM CONFIGS**
- **DISABLE BACKGROUND CHECK**
- **DISABLE 4H REFRESH**

### 🔄 Operating Modes
Click the text beneath the Connect button to cycle through the operating modes:
- **VPN Mode**: Captures 100% of your computer's internet traffic using a virtual network adapter (TUN). Best for gaming or apps that don't respect proxy settings.
- **Proxy Mode**: Modifies the Windows System Proxy settings. Only apps that respect the system proxy (like web browsers) will be routed through CrimsonX.
- **Clear Proxy Mode**: Disables the Windows System Proxy settings, allowing your system to have a direct connection to the internet, while still leaving the proxy port open in the background for applications where you manually configure the proxy settings.

---

## 2. Stats & Logs Overlays
Accessible via the small navigational icons on the main dashboard.

### 📊 Stats View
Provides real-time telemetry of your active connection:
- **Speeds**: Live Upload and Download speed graphs and text readouts.
- **Data Usage**: Total amount of bandwidth consumed during the current session.
- **Latency (Ping)**: Real-time latency to the remote server.
- **Network Info**: Displays the country of the server you are connected to, your **Local Port**, and your **LAN IP** (if LAN sharing is active). *(Note: Your public IP is intentionally not displayed here).*

### 📝 Logs View
A terminal-like window that outputs raw logs directly from the underlying Xray engine. Useful for debugging connection issues or verifying that specific routing rules are being triggered.

---

## 3. Split Tunneling Tab
Split Tunneling gives you granular control over what traffic is routed through the proxy and what traffic uses your normal, direct internet connection.

### Routing Modes
- **DISABLED**: Split tunneling is turned off. All traffic is routed according to your main operating mode.
- **EXCLUSIVE**: Only *bypass* the proxy for the apps, domains, IPs, and ports listed. Everything else goes through the proxy.
- **INCLUSIVE**: *Only* route the apps, domains, IPs, and ports listed through the proxy. Everything else uses your direct internet.

### Split Categories
- **APPLICATIONS**: Click "ADD" to browse for and select an executable file (e.g., `chrome.exe`). The selected apps will follow your chosen Exclusive or Inclusive routing rule.
- **DOMAINS, IPs & PORTS**: Enter specific domains (e.g., `example.com`), IP addresses, or ports you wish to route or bypass.
- **BLOCKED DOMAINS, IPs & PORTS**: Enter domains, IPs, or ports that you want to completely block from accessing the internet.

### Direct UDP
- **DIRECT UDP Toggle**: Forces all UDP traffic (like Discord voice or competitive games) to bypass the proxy and use your direct internet connection, ensuring minimal latency while keeping TCP traffic proxied.

---

## 4. Settings Tab
The heart of CrimsonX's customization, broken down into specific sections:

### START-UP
- **LAUNCH ON START-UP**: Launches CrimsonX automatically when you log into Windows.
- **AUTO-CONNECT**: Automatically initiates the connection sequence as soon as the app starts.
- **START MINIMIZED**: Opens the app silently in the background rather than popping up the main window.
- **MINIMIZE TO TRAY**: When clicking the close `X` button or minimizing the app, it will hide in the system tray (near the clock) rather than closing completely.

### CONNECTION
- **EXCLUDE LOCATIONS**: Select specific continents (e.g., Asia, Europe). Configs located in these continents will be completely excluded from speed testing and connections.
- **CUSTOM CONFIGS**: Enter up to two of your own private VLESS/VMESS/Shadowsocks strings. 
  - **ALLOW CONNECTING WITH ONE CONFIG**: If checked, the app will successfully connect even if only one of your two custom configs is working.
- **CUSTOM XRAY EXIT-NODE**: Import a `.json` file or paste a share link (e.g., `vless://`, `vmess://`) containing a custom Xray configuration to act as an exit node.
- **BIND ADAPTER**: Forces all proxy traffic to exclusively exit through the specific network adapter you select from the dropdown.
- **DNS SETTINGS**: 
  - **UPSTREAM DOH URL**: Resolve DNS through encrypted DNS-over-HTTPS (DoH) instead of plaintext queries, reducing DNS leaks and censorship.
  - **SYSTEM DNS**: Force your system's primary and secondary DNS to specific IPv4 addresses while connected.
- **ALLOW LAN CONNECTIONS**: Opens the proxy port to your local network, allowing your phone, console, or smart TV to connect to your PC's IP and share the VPN.
  - **AUTHENTICATION**: Requires devices on the network to supply a Username and Password to use your LAN proxy.
- **AD AND TRACKER BLOCKER**: Drops requests to known ad and tracker domains before they leave your PC. Xray routes matching domains to a blackhole outbound.
- **LOAD-BALANCE**: Controls how Xray distributes connections across your proxy nodes:
  - `ROUND ROBIN`: Cycles through nodes sequentially.
  - `LEAST LOAD`: Sends traffic to the node with the fewest active connections.
  - `LEAST PING`: Prioritizes the node with the fastest response time.
  - `RANDOM`: Statistically balances traffic by picking a random node.

### SYSTEM
- **DISABLE BACKGROUND CHECK**: Stops the app from continuously testing new configs in the background while you are connected.
- **DISABLE 4H REFRESH**: Prevents the app from fetching entirely new config lists from the cloud every 4 hours.
- **LANGUAGE**: Switch the entire interface between English and Persian (`فارسی`). Features full Right-To-Left (RTL) layout mirroring.
- **DEBUG MODE**: Enables detailed error logging. Useful for reading logs in `error.log` when something fails to connect.
- **DESKTOP SHORTCUT**: One-click button to create a CrimsonX shortcut on your desktop.
- **START MENU SHORTCUT**: One-click button to create a CrimsonX shortcut in your Start menu.

---

## 5. Themes Tab
Allows you to personalize the visual aesthetic of the application. 
- Choose between dynamic gradient themes: Crimson, Blue, Purple, Green, Pink, and Yellow. 
- The selected theme instantly updates the pulsing connect button, navigation borders, toggle switches, and background accents.

---

## 6. About Tab
- Displays the currently installed version of CrimsonX.
- Automatically checks for updates and handles the OTA (Over-The-Air) download and extraction of new versions from GitHub.
- Provides quick links to the project's GitHub repository and community Telegram channels.
