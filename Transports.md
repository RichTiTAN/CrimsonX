# Transports

CrimsonX reads share links using the v2rayN conventions (`type`, `path`, `host`, `sni`, `fp`, `alpn`, `serviceName`, `headerType`, `pbk`, `sid`, `spx`, `flow`, `fm`). Xray itself defines no link format, so these are the names every panel and client emits.

Stream settings are written as `method`. Configs that still carry the legacy `network` field keep working, because every reader accepts both.

## Supported transports

| Transport | Accepted | Notes |
|-----------|----------|-------|
| `raw` / `tcp` | yes | written as `raw`; `headerType=http` builds the HTTP header with `path` + `host` |
| `ws` | yes | `path` + `host`; there is no early-data field |
| `grpc` | yes | `serviceName` (`path` is accepted as its alias), `authority`, `mode=multi` → `multiMode` |
| `httpupgrade` | yes | `path` + `host` |
| `xhttp` / `splithttp` | yes | both names are read; written as `xhttp` with `path`, `host`, `mode`, `extra` |
| `kcp` / `mkcp` | yes | `mtu`, `tti`, `uplinkCapacity` (`up`), `downlinkCapacity` (`down`), `cwndMultiplier`, `maxSendingWindow`; the old `header`/`seed` fields are dropped because FinalMask owns them now |
| `masque` | yes | through the `masque://` scheme below |
| `h2`, `h3`, `http`, `quic` | no | removed from Xray and folded into `http`; use `xhttp` with `alpn=h2` or `alpn=h3` |

`hysteria` and `xdrive` exist in Xray but have no share-link syntax in CrimsonX, so they only arrive as custom configs; they are still classified as UDP-like and TCP-like (UDP-like when `alpn` contains `h3`) for the context checks below.

Where each transport can be used:

- **Custom config** (raw JSON or a share link): everything above.
- **Custom exit node**: xray-only transports are allowed, but UDP transports are not.
- **Apps & Games custom proxy**: only transports that sing-box can dial, and only TCP ones.
- **UDP transports** (`kcp`, `masque` over HTTP/3, `xhttp` with `alpn=h3`): custom configs only, because chains and app rules hop through a local TCP proxy.

## MASQUE

`masque://` is a CrimsonX extension: neither Xray nor v2rayN defines a MASQUE share link, so other clients will not understand it. The JSON it produces is the standard Xray MASQUE outbound.

```text
masque://user:pass@host:443?path=<uri-template>&host=<authority>&sni=<name>&alpn=h2|h3&fp=<fingerprint>&rdns=<ip,ip>#label
```

Working sample, verified with the bundled Xray (`xray -test` returns OK):

```text
masque://love:secret@example.com:443?path=%2F.well-known%2Fmasque%2Fip%2F%7Btarget%7D%2F%7Bipproto%7D%2F&host=example.com&sni=example.com&alpn=h2&rdns=1.1.1.1,2606:4700:4700::1111&fp=chrome#MASQUE-H2-Demo
```

```json
{
  "protocol": "masque",
  "settings": {
    "address": "example.com",
    "port": 443,
    "remoteDNS": ["1.1.1.1", "2606:4700:4700::1111"]
  },
  "streamSettings": {
    "method": "masque",
    "masqueSettings": {
      "host": "example.com",
      "path": "/.well-known/masque/ip/{target}/{ipproto}/",
      "user": "love",
      "pass": "secret"
    },
    "security": "tls",
    "tlsSettings": {
      "serverName": "example.com",
      "alpn": ["h2"],
      "fingerprint": "chrome"
    }
  }
}
```

- `path` is written only when the link carries it; when it is missing, Xray falls back to its built-in `.well-known/masque/ip/{target}/{ipproto}/` template.
- `alpn` defaults to `h2`. A profile whose ALPN contains `h2` and not `h3` is treated as TCP-like; anything else, `h3` included, counts as UDP-like and is therefore custom-config only.
- The parser always writes `security: tls` and never REALITY, so `fp` only feeds `tlsSettings.fingerprint`.
- `rdns` fills `settings.remoteDNS`; Xray validates those entries as IP addresses.
- `masqueSettings.host` is the `:authority` of the CONNECT-IP request, and the parser writes it only when the link has `host`.
- Header-based authentication has no link field: raw JSON can put `masqueSettings.headers` next to `user`/`pass`.
- WARP-over-MASQUE has no link syntax either: a raw config adds a `masqueSettings.warp` object.

## WireGuard

Links use `wireguard://`, or the `wg://` alias. The private key sits in the userinfo, everything else in the query.

```text
wireguard://<private-key>@host:51820?publickey=<peer-public-key>&address=10.0.0.2/32&allowedips=0.0.0.0/0&keepalive=25#wg-demo
```

Accepted query keys (aliases in parentheses): `publickey` (`public_key`, `pk`, `peerpublickey`), `address` (`ip`, `local_address`), `allowedips` (`allowed_ips`, `allowedip`), `psk` (`presharedkey`, `pre_shared_key`), `keepalive` (`persistentkeepalive`, `persistent_keepalive`; `25-50` is read as `25`), `reserved` (`a+b+c`), `mtu`, `listenport` (`listen_port`), `workers`, `dns`. The private key, the server and the port may also arrive in the query as `privatekey`/`secretkey`, `host`/`server`/`remote` and `port`/`server_port`.

### WireGuard JSON in both dialects

Paste either an Xray outbound or a sing-box endpoint; both are accepted and both are rewritten into the sing-box form before the tunnel starts.

Xray outbound — `protocol: wireguard`, everything under `settings`, peers use one combined `endpoint` and Xray's camelCase keys:

```json
{
  "protocol": "wireguard",
  "settings": {
    "secretKey": "<private-key>",
    "address": ["10.0.0.2/32"],
    "mtu": 1420,
    "workers": 2,
    "peers": [
      {
        "endpoint": "example.com:51820",
        "publicKey": "<peer-public-key>",
        "preSharedKey": "<psk>",
        "keepAlive": 25,
        "allowedIPs": ["0.0.0.0/0", "::/0"],
        "reserved": [0, 0, 0]
      }
    ]
  }
}
```

sing-box endpoint — `type: wireguard`, snake_case keys, peers split into `address` + `port`:

```json
{
  "type": "wireguard",
  "private_key": "<private-key>",
  "address": ["10.0.0.2/32"],
  "mtu": 1420,
  "peers": [
    {
      "address": "example.com",
      "port": 51820,
      "public_key": "<peer-public-key>",
      "pre_shared_key": "<psk>",
      "allowed_ips": ["0.0.0.0/0", "::/0"],
      "persistent_keepalive_interval": 25
    }
  ]
}
```

Every input below ends up as the endpoint shown second:

| Input | Notes |
|-------|-------|
| `wireguard://` / `wg://` link | the form at the top of this section |
| `.conf` file | `[Interface]` Address/PrivateKey/MTU/ListenPort, `[Peer]` Endpoint/PublicKey/PresharedKey/AllowedIPs/PersistentKeepalive |
| sing-box endpoint | a bare `{"type":"wireguard", …}` object, or an entry in `"endpoints": [ … ]` |
| sing-box outbound | an `outbounds` entry with `"type": "wireguard"` |
| Xray outbound | `{"protocol":"wireguard","settings":{…}}` |
| Flat legacy object | `private_key` + `peer_public_key` + `server`/`address` + `server_port` |

- `allowed_ips` defaults to `0.0.0.0/0, ::/0` and the peer port defaults to `51820`.
- `mtu`, `listen_port` and `workers` are written only when the input carries them.
- A JSON file holding several `endpoints` keeps the first supported one and logs how many were skipped.

### AmneziaWG

Add any AmneziaWG value — in the link query, in the `.conf`, or by pasting `"type": "awg"` — and the endpoint moves to the AmneziaWG engine: `type` becomes `awg`, `useIntegratedTun: false` is added, peers lose `reserved`, and `pre_shared_key` becomes `preshared_key`.

| Input key | Endpoint field |
|-----------|----------------|
| `jc`, `jmin`, `jmax` | same names, integers |
| `s1` … `s4` | same names, integers |
| `h1` … `h4` | same names, strings |
| `i1` … `i5` | same names, strings |
| `hpk` / `headerprotectionkey` | `header_protection_key` |
| `cpa` / `contentpaddingaddition` | `content_padding_addition` |
| `rekeyaftertime` | `rekey_after_time` |
| `rekeytimeout` | `rekey_timeout` |
| `rejectaftertime` | `reject_after_time` |
| `keepalivetimeout` | `keepalive_timeout` |
| `maxhandshakeattempts` | `max_handshake_attempts` |
| `randomtrailers` | `random_trailers` |
| `disablecookies` | `disable_cookies` |

`dns` in the link or `DNS` in the `.conf` is taken by the AmneziaWG instance itself and accepts plain IP addresses with an optional port; a `jmin` larger than `jmax` is rejected.

## IKEv2

```text
ikev2://user:pass@host:500?psk=<shared-secret>&remote_id=<id>&local_id=<id>&dns=1.1.1.1&mtu=1400#ikev2-demo
```

Accepted query keys (aliases in parentheses): `psk` (`pre_shared_key`, `preshared_key`, `shared_secret`), `auth` (`auth_method`, `method` — `eap`/`eap-mschapv2` or `psk`), `remote_id` (`remote_identifier`, `remoteid`, `idr`), `local_id` (`local_identifier`, `localid`, `idi`), `ca` (`ca_pem`, `certificate_authority` — PEM text or base64), `dns` (`dns_server`), `mtu`, `port`/`server_port`, `name` (`label`, `tag`).

```json
{
  "type": "ipsec",
  "server": "example.com",
  "server_port": 500,
  "auth": "eap",
  "username": "user",
  "password": "pass",
  "remote_id": "vpn.example.com",
  "local_id": "user",
  "ca": "-----BEGIN CERTIFICATE-----\n…",
  "dns": "1.1.1.1",
  "mtu": 1400
}
```

- The same object is read from a link query, from raw JSON (`type: ipsec` or `type: ikev2`, alone or inside `endpoints`) and from a `.mobileconfig` profile; all three normalize to the shape above.
- `auth` is optional: without it a pre-shared key plus no username means `psk`, anything else means `eap`. In `eap` mode `local_id` defaults to the username.
- EAP-TLS client certificates are not supported; `cert`, `client_cert`, `client_certificate`, `p12` and similar keys are rejected. Use a username and password, or a pre-shared key.
- EAP mode with a missing username or password is not an error at import: the credentials are requested afterwards.

## OpenVPN

There is no OpenVPN URI scheme: paste the `.ovpn` text, including its inline blocks (`<ca>`, `<cert>`, `<key>`, `<tls-auth>`, `<tls-crypt>`, `<tls-crypt-v2>`, `<auth-user-pass>`).

```json
{
  "type": "openvpn-client",
  "mode": "tls",
  "network": "udp",
  "servers": [ { "server": "example.com", "server_port": 1194 } ],
  "tls": {
    "certificate": "<ca pem>",
    "client_certificate": "<cert pem>",
    "client_key": "<key pem>",
    "server_name": "vpn.example.com",
    "control_wrap": { "type": "tls_crypt", "key": "<key>" }
  },
  "username": "user",
  "password": "pass"
}
```

The `tls` object is not a `ca`/`cert`/`key` trio; the inline blocks map like this:

| Directive | Field |
|-----------|-------|
| `<ca>` | `tls.certificate` |
| `<cert>` | `tls.client_certificate` |
| `<key>` | `tls.client_key` |
| `<tls-auth>` / `<tls-crypt>` / `<tls-crypt-v2>` | `tls.control_wrap` = `{ "type": "tls_auth" \| "tls_crypt" \| "tls_crypt_v2", "key": … }`, plus `direction` from `key-direction` for `tls_auth` |
| `verify-x509-name` | `tls.server_name` (+ `tls.server_name_type`) |
| `remote-cert-tls`, `ns-cert-type`, `remote-cert-ku`, `remote-cert-eku`, `peer-fingerprint` | `tls.remote_certificate_tls`, `tls.ns_certificate_type`, `tls.remote_certificate_ku`, `tls.remote_certificate_eku`, `tls.peer_fingerprint` |
| `tls-version-min`, `tls-version-max`, `tls-cipher`, `tls-groups` | `tls.version_min`, `tls.version_max`, `tls.cipher`, `tls.groups` |

The rest of the profile is carried through as endpoint fields: `proto` → `network` (with `servers[].network` on any `remote` line that names a different proto), `remote-random` → `remote_random`, `auth-user-pass` → `username`/`password`, `auth-retry` → `auth_retry`, `data-ciphers`/`cipher` → `data_ciphers`, `data-ciphers-fallback`, `auth` (upper-cased), `tun-mtu`/`mtu` → `mtu`, `mssfix` → `mss_fix` or `mss_fix_disabled`, `reneg-sec`/`reneg-bytes`/`reneg-pkts` → `renegotiate_interval`/`renegotiate_bytes`/`renegotiate_packets`, `ping`/`ping-restart` → `ping_interval`/`ping_restart`, `tls-timeout`, `handshake-window`, `explicit-exit-notify`, `fragment`, `comp-lzo` → `compression_lzo`, `compress` → `compression`, `allow-compression`, `redirect-gateway` (+ `redirect_gateway_flags`), `redirect-private`, `block-ipv6`, `route-nopull`, `pull-filter` → `pull_filters`.

Static key mode:

```json
{
  "type": "openvpn-client",
  "mode": "static_key",
  "network": "udp",
  "servers": [ { "server": "example.com", "server_port": 1194 } ],
  "static_key": "<secret pem>",
  "address": "10.8.0.2",
  "peer_address": "10.8.0.1"
}
```

- `mode` becomes `tls` as soon as any TLS material is present (`client`, `tls-auth`, an inline `<ca>`/`<cert>`/`<key>`, `remote-cert-tls`); otherwise a `<secret>` block makes it `static_key`.
- TAP configs (`dev tap*`, `dev-type tap`) are refused; use TUN.
- Static key mode keeps a single remote — extra `remote` lines are dropped and logged — and needs both an inline `<secret>` and an `ifconfig` address.
- Referenced files are ignored: `ca`, `cert`, `key`, `tls-crypt` and `pkcs12` as file paths are skipped with a warning, so the material has to be inline.
- `http-proxy`, `socks-proxy` and the server-side directives `server`, `push`, `tls-server` are ignored with a warning.
- A JSON file that already holds an `openvpn-client` endpoint is accepted too, as is `"type": "openvpn"`.
