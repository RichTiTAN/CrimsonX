# CrimsonX - Comprehensive User Manual

Welcome to the **CrimsonX** User Manual! This document provides an in-depth explanation of every feature, tab, and setting available in the application.

---

## 1. Home Tab
The Home tab is the main dashboard of CrimsonX, giving you quick access to connection controls and real-time monitoring.

### 🔴 The Connect Button
The pulsating core of the application. Clicking this button initiates the dynamic connection pipeline:
1. **Config Scraping & Testing**: CrimsonX silently pulls fresh configurations from remote worker nodes or reads your cached configs.
2. **Speed, Stability & Latency Checks**: It tests these configurations in the background to make sure they are working, then speed-tests every survivor for **7 seconds**, sampling the transfer every 250 ms. That way a node that starts fast and then chokes is caught instead of simply averaged away: configs that held their throughput for the whole test are ranked first (fastest of them first), and only when none of them did does the ranking fall back to plain fastest-first. Stalls, jitter and mid-test collapses are written to the app log.
3. **Engine Startup**: Once the best nodes are selected, the Xray-core engine is started. If you are in VPN Mode, sing-box is also initialized to capture all system traffic via a TUN interface.

While that pipeline runs, the connect box's border breathes: a soft orange ring (`#DD6B20`) fades in and out over 1.6 seconds - in the Home box and in the APPS & GAMES strip alike - and it disappears the moment the session is up (the border then reads green) or the attempt ends. The Home button draws no progress bar behind its label: the **STATUS** readout counts the percentage up instead, and the strip does no animation work at all while you are sitting on another tab.

The **Default / Custom** choice is Quick Settings' own first slot now: a **CUSTOM CONFIGS** switch, which is the very control the Settings tab has, so it saves and validates in exactly the same way. Default runs on the configs CrimsonX picks for you; Custom runs on the two custom configs you keep in the Settings tab. Turning it on while both of those boxes are still empty does not stick: the switch bounces straight back off, the Settings tab opens its custom-configs section for a paste, and the panel's **CUSTOM CONFIGS** view is one click away. Emptying both boxes and submitting goes the other way: the switch turns off and the boxes stay empty. That slot used to hold **DIRECT UDP**, and every existing profile is carried over to **CUSTOM CONFIGS** once — the first time this build opens it — while a slot you pick by hand afterwards is kept.

### ⚡ Quick Settings Panel
Located on the Home screen, this panel provides fast access to frequently toggled options without having to dig into the Settings tab. The available quick toggles are (the first slot starts on **CUSTOM CONFIGS**, the second on **AUTO-CONNECT**):
- **DIRECT UDP**
- **EXIT-NODE**
- **BIND ADAPTER**
- **DOH**
- **SYSTEM DNS**
- **AD BLOCKER**
- **LAN CONNECTIONS**
- **LAUNCH ON START-UP**
- **AUTO-CONNECT**
- **START MINIMIZED**
- **MINIMIZE TO TRAY**
- **CUSTOM CONFIGS**
- **DISABLE BACKGROUND CHECK**
- **DISABLE SEAMLESS SWAP**

The panel has two views. **QUICK SETTINGS** is the list above; clicking the **CUSTOM CONFIGS** label beside it slides the panel across to the same custom-config body the Settings tab shows - both config boxes with their own **+** import, **PING** and **SAVE** buttons, and **SUBMIT** with an **ALLOW SINGLE CONFIG** pill on the right (click it to light it, click it again to clear it). Every one of those buttons goes through the Settings tab's own handler, so the two always show the same values and doing it here is exactly the same as doing it there. The first slot's switch is the Settings tab's own **CUSTOM CONFIGS** control, so flipping either one flips both.

### 🔄 Operating Modes
Pick them in **Settings → CONNECTION**, in the **LEGACY CONNECTION MODES** row (they used to sit under the Connect button; whichever mode you were already on is the one highlighted):
- **VPN Mode**: Captures 100% of your computer's internet traffic using a virtual network adapter (TUN). Best for gaming or apps that don't respect proxy settings.
- **Proxy Mode**: Modifies the Windows System Proxy settings. Only apps that respect the system proxy (like web browsers) will be routed through CrimsonX.
- **Clear Proxy Mode**: Disables the Windows System Proxy settings, allowing your system to have a direct connection to the internet, while still leaving the proxy port open in the background for applications where you manually configure the proxy settings.

---

## 2. Stats Overlay
The readouts sit at the top of the main dashboard, above the connect button, and the row of connection details sits above Quick Settings, exactly as wide as that panel - the stats box takes that whole line. While nothing is connected the two sparklines fold away, so that readout box is narrower, and the box grows back into them the moment a session comes up.

### 📊 Stats View
Provides real-time telemetry of your active connection:
- **Status**: Reads *Offline* while nothing is up, counts the connect percentage up while a connection is coming up, and reads *Connected* once the session has settled. It lives in the readout box at the top of the Home tab, above the connect box and in the same style as the other boxes.
- **Session**: How long the current session has been up, in that same box right beside the status. While nothing is connected it reads *00:00:00*.
- **Latency (Ping)**: Real-time latency to the remote server, in that box beside the session. Click the tile to trace again.
- **Data Usage**: Total amount of bandwidth consumed during the current session, in that box as well. It reads whole numbers (*999 MB*, *999 KB*) until the session passes 1 GB, and takes two decimals past that (*1.25 GB*).
- **Speeds**: Live Upload and Download readouts, each with its own sparkline in the cell right beside it - a cell with no padding of its own, so the graph fills it and the fade at either end starts at the highlight's edge. Each plots about the last 21 seconds, one point every 1.5s, and the two scroll together; each scales to its own peak, so a quiet upload is not flattened by a busy download.
- **Location / IP address**: The country the exit node sits in, and the public address the internet sees - the exit's, not your own. The address reads *"Tracing..."* while a trace runs and *"Timeout"* if it never answered. Click the location to trace again, click the address to copy it. Both open the row above Quick Settings, the two ports sit after them, and that row is as wide as the panel below it.
- **Ports**: Your **Local Port**, and your **LAN IP** (if LAN sharing is active), on that same row. Click either one to copy it.

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
- You can also choose which adapter should handle your Direct UDP traffic.

---

## 4. Settings Tab
The heart of CrimsonX's customization, broken down into specific sections:

### START-UP
- **LAUNCH ON START-UP**: Launches CrimsonX automatically when you log into Windows.
- **AUTO-CONNECT**: Automatically initiates the connection sequence as soon as the app starts.
- **START MINIMIZED**: Opens the app silently in the background rather than popping up the main window.
- **MINIMIZE TO TRAY**: When clicking the close `X` button or minimizing the app, it will hide in the system tray (near the clock) rather than closing completely.

### CONNECTION
- **UDP SCANNER**: Hunts for configs whose **UDP** path actually works (best for gaming, voice and other real-time traffic). It is a rail tile - the radar icon between the blocker and Apps & Games - and clicking it slides the settings page out and the scanner in from the right, with that tile left highlighted while the scanner is up. There is no back button any more: pick any other rail tile to leave it (the Settings tile returns to the settings content), and a running scan simply keeps going in the background. It fetches candidates exactly like the normal connect flow (working cache first, then the remote workers) and qualifies them with the UDP probe only - a TCP/HTTP check never decides the outcome, and nothing found here is written into the working cache. Everything chosen on this page (amount, concurrency, discard limit, test adapter) is remembered between sessions.
  - **AMOUNT OF CONFIGS**: How many working configs to collect (5, 10 or 15 - default 10).
  - **CONCURRENT TESTING**: How many configs are probed at the same time (5 or 10).
  - **DISCARD HIGHER THAN**: Drop results whose real ping is slower than the chosen limit (100 ms to 900 ms in 100 ms steps, or **No limit**; defaults to 500 ms).
  - **TEST ADAPTER**: Choose which network adapter the tests leave through (**Default** = whatever Windows picks). It applies to the scan and the stability test only, and leaves the app-wide BIND ADAPTER setting untouched.
  - **START TESTING / STOP**: Runs the scan against the cache and the worker sources. Each config gets three UDP probes in a row and is listed as its **country name** with its **real ping** - measured through the node with the same HTTP targets the app pings when connected (the country code, continent, server address and UDP latency live in the row tooltip). Configs alternate between the two columns as they are found, and the list is sorted by ping once the goal is reached. The results box holds **5 configs per column** and scrolls beyond that. The scan status is shown under the graph.
  - **STABILITY GRAPH**: Plots every probe of the current stability run live, in the same style as the home-tab network graph (smooth spline with an area fill; lost probes break the curve and show a red tick). The block highlights while you hover it, and a live probe/loss counter sits above it.
  - **STABILITY**: Runs a 10 second test on one config (a UDP probe every 500ms); the success rate and average ping appear next to the **STABILITY** caption above the graph, while the status line simply shows `STABILITY TESTING...`. The tunnel is warmed up first, so the connection handshake never inflates the first sample.
  - **COPY ICON**: Copies the config as a **share link** (`vless://`, `vmess://`, `trojan://`, `ss://`) named **`CrimsonX-<country code>`** (e.g. `CrimsonX-US`) so it stands out in the other client's list; if a link cannot be built, the outbound JSON is copied instead.
  - **SAVE ICON**: Stores that same share link in the settings tab's **SAVED CONFIGS** pool (it also shows up in the **CUSTOM PROXY** dropdowns, named `CrimsonX-<country code>`); the toast names the entry, and a pool already holding 30 configs says so instead.
- **+ (SAVED CONFIGS)**: The plus button beside the paste box imports a config file (`.ovpn`, `.conf`, `.json`, `.txt`) into the pool, titles and all, exactly like the **+** buttons next to the custom config boxes; the text **SAVE** button next to it stores whatever the box currently holds.
- **LOCATIONS**: Pick the continents you want to connect to; configs from every other continent are ignored. This one is not a quick-settings slot - it has its own globe tile in the sidebar, directly under Home, and clicking it opens a small overlay listing **All** plus one entry per continent. Choosing **All** switches the filter off (connect anywhere), choosing continents restricts it to them, and if you leave every continent unchecked **All** is selected for you. The overlay fades and slides into place, closes with its X button or a click anywhere else, and its tile stays lit the way a page tile does while it is up.
- **CUSTOM CONFIGS**: Enter up to two of your own private configs.
  - **Keep the config behind the scenes**: Paste the config straight into the box with **Ctrl+V** (or **Shift+Insert**) - whatever its length or number of lines - press the **+** button to import a `.ovpn` / `.conf` / `.json` file, or pick an entry from the dropdown of your saved configs. Pasting submits the config to the app straight away without adding it to the pool; the box then shows only that config's **title** (for example `openvpn · 217.138.216.98:1194`). Renaming an entry in the pool renames it in the boxes too, and deleting it clears the box that used it.
  - **SAVE / SUBMIT**: Each slot has its own **SAVE**, which stores just that box's config in **SAVED CONFIGS**; **SUBMIT** applies both boxes without adding anything to the pool. The old **CLEAR** button is gone - to empty a slot, clear its box.
  - **Share links & JSON**: `vless://`, `vmess://`, `trojan://`, `ss://`, `hysteria2://`, `tuic://`, `socks://`, `http://`, xray outbound JSON or sing-box outbound JSON.
  - **OpenVPN**: paste a complete `.ovpn` file, including the inline `<ca>`, `<cert>`, `<key>`, `<tls-auth>` or `<tls-crypt>` blocks (the app joins those multi-line blocks into the single-line form sing-box expects). `cipher`/`data-ciphers`, `auth`, `tun-mtu`, `mssfix`, `reneg-sec`, `comp-lzo`, `redirect-gateway`, `remote-random`, several `remote` entries and `key-direction` are translated; `dev tap`, `http-proxy` and `socks-proxy` are not supported. If the config asks for `auth-user-pass`, CrimsonX prompts for the username and password on connect (with a *Remember for this config* option).
  - **WireGuard**: paste a `.conf` file, a `wireguard://` share link, a sing-box `wireguard` endpoint JSON, or an xray `wireguard` outbound JSON. The `DNS=` line of a `.conf` is not applied to the system (CrimsonX keeps its own DNS routing).
  - **TUNNEL PORT**: OpenVPN and WireGuard configs run on a dedicated headless sing-box instance that exposes a local socks inbound, and xray dials it as a normal outbound. The port is chosen dynamically on every connect (a free loopback port, kept for the session and logged as `[Tunnel] wireguard <server> → socks 127.0.0.1:<port>`), so it can never collide with other software and is never exposed to the LAN.
  - **PING**: Tests the config end to end, including the OpenVPN/WireGuard tunnel it starts for the test.
  - **ALLOW CONNECTING WITH ONE CONFIG**: If checked, the app will successfully connect even if only one of your two custom configs is working.
- **CUSTOM EXIT-NODE**: Pick a config from your saved configs with the arrow, paste one, or import a file with **+**; **SAVE** stores it in **SAVED CONFIGS** and **PING** tests it. Any of these can be the exit node:
  - **xray configs** (share link or outbound JSON) and **sing-box configs** (converted to xray for the protocols xray speaks - vless, vmess, trojan, shadowsocks, socks, http with ws/grpc/http/httpupgrade/xhttp, hysteria2) are chained inside xray, so the traffic leaves through the entry node and websites see the exit server's address.
  - **OpenVPN (`.ovpn`) and WireGuard (`.conf`, `wireguard://`, xray or sing-box JSON)** are chained inside sing-box instead: the endpoint dials through the very same socks connection that links sing-box to xray, so the entry nodes hide the tunnel's outer hop while the tunnel server stays the address websites see. This needs **VPN Mode** - in Proxy Mode the app says so instead of silently ignoring it.
  - OpenVPN configs that require `auth-user-pass` ask for the credentials (with an option to remember them) as soon as the config is saved.
  - While a tunnel exit node is chained, the proxy inbound of the sing-box instance takes over the shared **LAN PORT** on **10920** (the page and the Home screen show the port), so clients on your LAN take the same chain; the country and ping shown at Home are resolved through the exit as well. If the tunnel does not come up within ~25 seconds the app connects without it and tells you, rather than leaving you black-holed.
  - The chain is verified the moment you connect - a request leaves through the tunnel and comes back (`[ExitNode] established after … ms` in the log). While it is active, the geo trace, the shared port and the country at Home all follow the exit node; the geo line names the exit's location (`[Geo] traced through 127.0.0.1:10920 -> <country> - that is the exit node's location`). If the chain cannot be established the app drops the exit node, says so, and puts the shared port back on xray (10919).
- **BIND ADAPTER**: Forces all proxy traffic to exclusively exit through the specific network adapter you select from the dropdown.
- **DNS SETTINGS**: 
  - **UPSTREAM DOH URL**: Resolve DNS through encrypted DNS-over-HTTPS (DoH) instead of plaintext queries, reducing DNS leaks and censorship. Used for proxied traffic.
  - **DNS ROUTING**: Name resolution always follows the traffic path. Apps and flows that are proxied resolve through the DoH upstream above (through the node, so the answers come from the uncensored exit), while everything that leaves the machine directly - apps set to *Direct* in Apps & Games, whatever bypasses in **Exclusive** mode, and everything outside the list in **Inclusive** mode - resolves through plain direct DNS. A dead or throttled node can therefore never black-hole name resolution for traffic that was going direct anyway.
  - **SYSTEM DNS**: Force your system's primary and secondary DNS to specific IPv4 addresses while connected.
- **ALLOW LAN CONNECTIONS**: Opens the proxy port to your local network, allowing your phone, console, or smart TV to connect to your PC's IP and share the VPN.
  - **AUTHENTICATION**: Requires devices on the network to supply a Username and Password to use your LAN proxy.
- **AD AND TRACKER BLOCKER**: Drops requests to known ad and tracker domains before they leave your PC. Xray routes matching domains to a blackhole outbound. Its toggle lives in the sidebar - the shield sitting between Themes and Apps & Games: click it to switch the blocker on (the shield turns green) and again to switch it off. Settings no longer has a row for it; it can still be pinned as a Home quick-settings slot, and every entry point drives the same setting and the saved config.
- **LOAD-BALANCE**: Controls how Xray distributes connections across your proxy nodes:
  - `ROUND ROBIN`: Cycles through nodes sequentially.
  - `LEAST LOAD`: Sends traffic to the node with the fewest active connections.
  - `LEAST PING`: Prioritizes the node with the fastest response time.
  - `RANDOM`: Statistically balances traffic by picking a random node.

### SYSTEM
- **DISABLE BACKGROUND CHECK**: Stops the app from continuously testing new configs in the background while you are connected.
- **DISABLE SEAMLESS SWAP**: Prevents the app from fetching entirely new config lists from the cloud every 1 hour and swapping them seamlessly.
- **LANGUAGE**: Switch the entire interface between English and Persian (`فارسی`). Features full Right-To-Left (RTL) layout mirroring.
- **DEBUG MODE**: Enables detailed error logging. Useful for reading logs in `error.log` when something fails to connect.
- **DESKTOP SHORTCUT**: One-click button to create a CrimsonX shortcut on your desktop.
- **START MENU SHORTCUT**: One-click button to create a CrimsonX shortcut in your Start menu.

---


---

## 5. Apps & Games Tab
The **Apps & Games** tab is a powerful hub that provides granular, per-app routing profiles for popular games and applications.

### Granular App Routing
Instead of relying on global proxy settings, you can configure precise rules for specific applications (like Discord, Telegram, or Steam) and competitive games (like CS2, League of Legends, Valorant).
- **TCP Routing**: Choose whether the app's TCP traffic should be routed through the VPN (`PROXY`) or bypass it (`DIRECT`).
- **UDP Routing**: Choose whether the app's UDP traffic (like voice chat or game data) should go through the VPN or your direct connection.

### Network Adapter Binding
For advanced setups, you can bind specific applications to specific network adapters. 
- **TCP Adapter / UDP Adapter**: Select which physical or virtual network interface an application should exclusively use for its traffic.

### Custom Proxy
Any rule can leave through its own private config instead of the shared pool: set a protocol's routing to **CUSTOM PROXY** and paste (or pick from your saved configs) a share link, an xray outbound JSON, a WireGuard config (`.conf`, `wireguard://`, sing-box / xray JSON) or a full OpenVPN config (`.ovpn` with inline `<ca>` / `<tls-crypt>`). Paste with **Ctrl+V** as usual: a long multi-line config is kept behind the scenes and the box only shows its title, never a clipped fragment.
- OpenVPN and WireGuard entries are served by a dedicated, headless sing-box instance that exposes a local socks inbound, and the rule's traffic is routed to it. It runs only while that config is actually referenced, and it lives in a separate process from the Custom Configs tunnels, so a failing OpenVPN server can never break the rest of the session.
- When the very same config **and** adapter are already running as a custom config, the rule reuses that engine instead of starting a second one, so the usual "one config in both boxes" setup needs no extra process. The engine layout is logged on every connect as `[Tunnel] engines: custom(...) rules(...)`, and a reuse shows up as `[AppRules] N app-rule tunnel(s) reuse the custom-config engine.`
- One engine serves any number of rules: a config used by two rules, or the same config bound to two different adapters, is still one process (one socks inbound per config + adapter).
- If a tunnel cannot be started (or an OpenVPN config needs a username/password you have not supplied yet) that single rule quietly falls back to the normal proxy/direct routing instead of breaking the config.
- Per-app rules, and therefore per-app custom proxies, only apply in **VPN Mode**; the rule engine is stopped when you switch to Proxy Mode.
- **APPLY CHANGES is a reconnect**: changing a rule while the session is up puts the app back into the connecting phase - the overlay strip reads CONNECTING while the ring and the progress move - until the OpenVPN / WireGuard exit node is re-established and the chain is verified again. The button cannot be clicked twice (it leaves the screen the moment you click it), and the connect button keeps its usual meaning throughout: clicking it disconnects, which also cancels the reapply cleanly, so nothing is left running behind. A failed reapply keeps the changes pending so the button returns and you can retry; an interrupted one falls back to the entry nodes exactly like a failed exit node.
- **PING** measures the rule's config with a real proxy instance: anything xray can dial (xray configs, sing-box configs, share links, hysteria2 and WireGuard - converted first when needed) is measured by a temporary xray instance, OpenVPN keeps its TCP handshake probe, and only the sing-box-only protocols (tuic, anytls) fall back to the sing-box prober.

### One intake for every config box
Every box that takes a config - this one, the two custom configs, the **SAVED CONFIGS** pool and the **CUSTOM EXIT-NODE** - accepts exactly the same shapes: OpenVPN / WireGuard (`.ovpn`, `.conf`, `wireguard://`, xray or sing-box JSON), xray JSON or share links, sing-box JSON or share links, and hysteria2. The app converts what each pane needs (sing-box here, xray for the custom configs and the exit node) and normalises the document on the way, so a bare outbound, a wrapped config with extra `inbounds`/`routing` blocks, or a link all behave the same and reach the engine in the shape it expects. Anything that cannot work in a pane is refused up front with a short message naming that box (`CONFIG NOT SUPPORTED IN EXIT NODE`, `... IN CUSTOM CONFIG`, `... IN APPS & GAMES`) - for example tuic or anytls in an xray box - instead of being accepted and failing at connect. OpenVPN and WireGuard are always dialed by sing-box, and a config that xray rejects is caught when you save it (the box shows xray's own message).
- **Every paste is checked and named**: the moment you paste (or import, or pick from the pool) a config into any of those boxes it is validated for that box and the text box immediately shows the config's **name** instead of the raw text - a share link shows its own `#name` (e.g. `MyLink`) or `protocol · server:port` when it has none, and OpenVPN / WireGuard show `openvpn · host:port` / `wireguard · host:port`. The check is the box's own engine accepting the document (offline, a fraction of a second): it does not dial the server - that is what **PING** is for.
- **Three buttons, three jobs**: **+** imports a config file into the box, **SAVE** stores the box's config in **SAVED CONFIGS**, and **SUBMIT** (in the custom configs row) applies it without storing. Every box follows this, including the ones in APPS & GAMES; removing a config is done on the **SAVED CONFIGS** rows.
- **HINT** shows the three-step walkthrough for this box.
- **Final masks (`fm=`) travel through untouched, exactly as v2rayN / v2rayNG do it**: whatever `fm=` JSON a share link (or an xray config) carries is written into `streamSettings.finalmask` - a sibling of `tlsSettings` - byte for byte, for `tls`, `reality` and `xtls` alike, so a provider's mask survives pasting, saving, export and the exit-node chain (it used to be read only on reality links, and the payload was then translated into a field set that xray no longer needs). Whether a mask works is settled by testing, never by guessing: every config goes through `xray -test` when you add it and again before the tunnel starts, and when xray refuses a mask the same config is used without it while a toast and the log show xray's own words. That guard also covers the configs that never pass a box - fetched pool entries and configs restored from disk are tested the same way before a session comes up. sing-box has no mask concept at all (only `tls.fragment` plus a fallback delay, and it rejects unknown fields outright), so on the sing-box side a TCP fragmenting mask becomes plain TLS fragmentation and the app says so.

### Regional Matchmaking
For supported games, you can force matchmaking to a specific region.
- **Matchmaking Region**: Restricts the proxy's server selection to a specific geographic region (e.g., Europe, Asia) to ensure you always connect to game servers with the best latency.

### Operating Modes
- **REGULAR**: The rules defined in the Apps & Games tab apply on top of your current global operating mode.
- **INCLUSIVE**: The VPN will *only* route the apps explicitly enabled and configured as `PROXY` in the Apps & Games tab. All other system traffic will bypass the VPN.

## 6. Themes Tab
Allows you to personalize the visual aesthetic of the application. 
- Choose between dynamic gradient themes: Crimson, Blue, Purple, Green, Pink, and Yellow. 
- The selected theme instantly updates the pulsing connect button, navigation borders, toggle switches, and background accents.

---

## 7. About Tab
- Displays the currently installed version of CrimsonX.
- Automatically checks for updates and handles the OTA (Over-The-Air) download and extraction of new versions from GitHub.
- Provides quick links to the project's GitHub repository and community Telegram channels.
